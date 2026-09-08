using System.Reflection;
using Autofac;
using Vecxy.Assets;
using Vecxy.Kernel;

namespace Vecxy.Prototypes;

public interface IPrototypes
{
    IReadOnlyList<PrototypeSystemInfo> Systems { get; }
    IReadOnlyList<PrototypeInfo> Types { get; }
    PrototypeDocument Create(Type targetType);
    PrototypeDocument Create<TTarget>() where TTarget : class;
    PrototypeDocument Load(string path);
    PrototypeDocument Load(PrototypeHandle handle);
    void Save(string path, PrototypeDocument document);
    PrototypeValidationResult Validate(PrototypeDocument document);
    PrototypeValidationResult Validate(string path);
    TTarget Instantiate<TTarget>(IPrototypeContext context, object? options = null) where TTarget : class;
    TTarget Instantiate<TTarget>(string path, IPrototypeContext context, PrototypeOverrides? overrides = null) where TTarget : class;
    TTarget Instantiate<TTarget>(PrototypeHandle handle, IPrototypeContext context, PrototypeOverrides? overrides = null) where TTarget : class;
    object Instantiate(Type targetType, IPrototypeContext context, object? options = null);
    object Instantiate(string path, IPrototypeContext context, PrototypeOverrides? overrides = null);
}

public sealed class PrototypesModule : IModule, IPrototypes
{
    public sealed class Definition : AModuleDefinition<PrototypesModule>
    {
        private readonly Assembly[] _assemblies;

        public Definition(IEnumerable<Assembly>? assemblies = null)
        {
            _assemblies = (assemblies ?? DiscoverAssemblies())
                .Where(x => !x.IsDynamic).Distinct().ToArray();
        }

        protected override IReadOnlyList<Type> Exports => [typeof(IPrototypes)];

        protected override void RegisterModule(ContainerBuilder builder)
        {
            builder.RegisterType<PrototypesModule>().AsSelf().SingleInstance();
            builder.RegisterAssemblyTypes(_assemblies)
                .Where(type => typeof(IPrototype).IsAssignableFrom(type) && !type.IsAbstract && !type.IsInterface)
                .As<IPrototype>().SingleInstance();
            builder.RegisterAssemblyTypes(_assemblies)
                .Where(type => typeof(IPrototypeSystem).IsAssignableFrom(type) && !type.IsAbstract && !type.IsInterface)
                .As<IPrototypeSystem>().SingleInstance();
        }

        private static IReadOnlyList<Assembly> DiscoverAssemblies()
        {
            var loaded = AppDomain.CurrentDomain.GetAssemblies().Where(x => !x.IsDynamic).ToList();
            var entry = Assembly.GetEntryAssembly();
            if (entry is not null && !loaded.Contains(entry)) loaded.Add(entry);
            var queue = new Queue<Assembly>(loaded);
            var names = loaded.Select(x => x.GetName().Name).OfType<string>().ToHashSet(StringComparer.Ordinal);
            while (queue.TryDequeue(out var assembly))
            {
                foreach (var reference in assembly.GetReferencedAssemblies())
                {
                    if (!names.Add(reference.Name ?? string.Empty)) continue;
                    try
                    {
                        var dependency = Assembly.Load(reference);
                        loaded.Add(dependency);
                        queue.Enqueue(dependency);
                    }
                    catch { /* Optional platform or plugin dependency. */ }
                }
            }
            var contractName = typeof(IPrototype).Assembly.GetName().Name;
            return loaded.Where(x => x == typeof(IPrototype).Assembly ||
                x.GetReferencedAssemblies().Any(reference => reference.Name == contractName)).ToArray();
        }
    }

    private readonly IAssetsManager _assets;
    private readonly Dictionary<Type, IPrototype> _prototypes = [];
    private readonly Dictionary<string, IPrototype> _names = new(StringComparer.Ordinal);
    private readonly IReadOnlyList<IPrototypeSystem> _systems;
    private readonly AsyncLocal<Stack<string>?> _instantiationPaths = new();
    private bool _initialized;
    private bool _disposed;

    public IReadOnlyList<PrototypeSystemInfo> Systems { get; }
    public IReadOnlyList<PrototypeInfo> Types { get; }

    public PrototypesModule(IAssetsManager assets, IEnumerable<IPrototype> prototypes, IEnumerable<IPrototypeSystem> systems)
    {
        _assets = assets;
        _systems = systems.ToArray();
        foreach (var prototype in prototypes)
        {
            if (!_prototypes.TryAdd(prototype.TargetType, prototype))
                throw new InvalidOperationException($"Multiple prototypes create '{prototype.TargetType.FullName}'.");
            AddName(GetTypeName(prototype.TargetType), prototype);
            foreach (var alias in prototype.TargetType.GetCustomAttributes<PrototypeAliasAttribute>().Select(x => x.Name))
                AddName(alias, prototype);
        }
        foreach (var prototype in _prototypes.Values) _ = ResolveSystem(prototype.TargetType);
        Systems = _systems.Select(x => new PrototypeSystemInfo(x.GetType(), x.ContextType, x.TargetBaseTypes)).ToArray();
        Types = _prototypes.Values.Select(x => new PrototypeInfo(x.TargetType, x.OptionsType,
            ResolveSystem(x.TargetType).GetType(), x.TargetType.Name, x.TargetType.Namespace ?? string.Empty,
            x.TargetType.GetCustomAttributes<PrototypeAliasAttribute>().Select(a => a.Name).ToArray())).ToArray();
    }

    public void OnInitialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized) return;
        _assets.RegisterImporter(new PrototypeAssetImporter());
        _initialized = true;
    }
    public void OnShutdown() { }

    public PrototypeDocument Create(Type targetType)
    {
        var prototype = GetPrototype(targetType);
        return new PrototypeDocument
        { Type = GetTypeName(targetType), Data = PrototypeSerializer.SerializeOptions(prototype.CreateOptions()) };
    }

    public PrototypeDocument Create<TTarget>() where TTarget : class => Create(typeof(TTarget));

    public PrototypeDocument Load(string path)
    {
        using var asset = _assets.Load<PrototypeAsset>(path);
        return Clone(asset.Value.Document);
    }

    public PrototypeDocument Load(PrototypeHandle handle)
    {
        using var asset = _assets.Load<PrototypeAsset>(handle);
        return Clone(asset.Value.Document);
    }

    public void Save(string path, PrototypeDocument document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(document);
        if (!path.EndsWith(".prototype", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Prototype files must use the .prototype extension.", nameof(path));
        var fullPath = Path.IsPathFullyQualified(path) ? Path.GetFullPath(path) :
            Path.GetFullPath(Path.Combine(_assets.AssetsDirectory, path));
        var relative = Path.GetRelativePath(_assets.AssetsDirectory, fullPath);
        if (relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            throw new InvalidOperationException("Prototype path must be inside the assets directory.");
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary = fullPath + ".tmp";
        File.WriteAllText(temporary, PrototypeSerializer.Serialize(document));
        File.Move(temporary, fullPath, overwrite: true);
    }

    public PrototypeValidationResult Validate(PrototypeDocument document)
    {
        var diagnostics = new List<PrototypeDiagnostic>();
        if (document.Format != PrototypeDocument.CurrentFormat)
            diagnostics.Add(new("VXY3001", PrototypeDiagnosticSeverity.Error, "format", $"Unsupported format {document.Format}."));
        if (!_names.TryGetValue(document.Type, out var prototype))
        {
            diagnostics.Add(new("VXY3002", PrototypeDiagnosticSeverity.Error, "type", $"Unknown prototype type '{document.Type}'."));
            return new PrototypeValidationResult(diagnostics);
        }
        try
        {
            var options = PrototypeSerializer.DeserializeOptions(document.Data, prototype.OptionsType);
            diagnostics.AddRange(ResolveSystem(prototype.TargetType).Validate(prototype, options));
        }
        catch (Exception exception)
        { diagnostics.Add(new("VXY3003", PrototypeDiagnosticSeverity.Error, "data", exception.Message)); }
        return new PrototypeValidationResult(diagnostics);
    }

    public PrototypeValidationResult Validate(string path)
    {
        try { return Validate(ResolveDocument(path, [], 0)); }
        catch (Exception exception)
        {
            return new PrototypeValidationResult([
                new PrototypeDiagnostic("VXY3004", PrototypeDiagnosticSeverity.Error, path, exception.Message)
            ]);
        }
    }

    public TTarget Instantiate<TTarget>(IPrototypeContext context, object? options = null) where TTarget : class =>
        (TTarget)Instantiate(typeof(TTarget), context, options);

    public object Instantiate(Type targetType, IPrototypeContext context, object? options = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        Bind(context);
        var prototype = GetPrototype(targetType);
        options ??= prototype.CreateOptions();
        if (!prototype.OptionsType.IsInstanceOfType(options))
            throw new ArgumentException($"Prototype '{targetType.FullName}' expects options '{prototype.OptionsType.FullName}'.", nameof(options));
        return ResolveSystem(targetType).Instantiate(prototype, options, context);
    }

    public TTarget Instantiate<TTarget>(string path, IPrototypeContext context, PrototypeOverrides? overrides = null) where TTarget : class
    {
        var result = Instantiate(path, context, overrides);
        return result as TTarget ?? throw new InvalidCastException(
            $"Prototype returned '{result.GetType().FullName}', not '{typeof(TTarget).FullName}'.");
    }

    public object Instantiate(string path, IPrototypeContext context, PrototypeOverrides? overrides = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        Bind(context);
        var normalized = path.Replace('\\', '/');
        var stack = _instantiationPaths.Value ??= new Stack<string>();
        if (stack.Count >= 64 || stack.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"Circular or excessively deep prototype reference: {string.Join(" -> ", stack.Reverse())} -> {normalized}");
        stack.Push(normalized);
        try
        {
            var document = ResolveDocument(normalized, [], 0);
            if (overrides is not null) document.Data = PrototypeSerializer.Merge(document.Data, overrides.Data);
            var validation = Validate(document);
            if (!validation.IsValid) throw new PrototypeValidationException(validation);
            var prototype = _names[document.Type];
            var options = PrototypeSerializer.DeserializeOptions(document.Data, prototype.OptionsType);
            return ResolveSystem(prototype.TargetType).Instantiate(prototype, options, context);
        }
        finally
        {
            stack.Pop();
            if (stack.Count == 0) _instantiationPaths.Value = null;
        }
    }

    public TTarget Instantiate<TTarget>(PrototypeHandle handle, IPrototypeContext context, PrototypeOverrides? overrides = null) where TTarget : class =>
        Instantiate<TTarget>(_assets.GetPath(handle), context, overrides);

    private PrototypeDocument ResolveDocument(string path, HashSet<string> chain, int depth)
    {
        if (depth >= 64) throw new InvalidDataException("Prototype nesting exceeds 64 levels.");
        var normalized = path.Replace('\\', '/');
        if (!chain.Add(normalized))
            throw new InvalidDataException($"Circular prototype reference: {string.Join(" -> ", chain)} -> {normalized}");
        var document = Load(normalized);
        if (!string.IsNullOrWhiteSpace(document.Prototype))
        {
            var parentPath = ResolveRelativePrototype(normalized, document.Prototype);
            var parent = ResolveDocument(parentPath, chain, depth + 1);
            if (!string.Equals(parent.Type, document.Type, StringComparison.Ordinal))
                throw new InvalidDataException($"Prototype '{normalized}' and base '{parentPath}' have different types.");
            document.Data = PrototypeSerializer.Merge(parent.Data, document.Data);
        }
        chain.Remove(normalized);
        return document;
    }

    private static string ResolveRelativePrototype(string owner, string reference)
    {
        if (reference.StartsWith("/", StringComparison.Ordinal)) return reference.TrimStart('/');
        var directory = Path.GetDirectoryName(owner.Replace('/', Path.DirectorySeparatorChar)) ?? string.Empty;
        return Path.GetFullPath(Path.Combine("/", directory, reference)).TrimStart(Path.DirectorySeparatorChar).Replace('\\', '/');
    }

    private IPrototype GetPrototype(Type type) => _prototypes.TryGetValue(type, out var prototype) ? prototype :
        throw new KeyNotFoundException($"No nested '{type.FullName}.Prototype' was discovered.");

    private IPrototypeSystem ResolveSystem(Type targetType)
    {
        var candidates = _systems.SelectMany(system => system.TargetBaseTypes
                .Where(baseType => baseType.IsAssignableFrom(targetType))
                .Select(baseType => (System: system, Distance: InheritanceDistance(targetType, baseType))))
            .OrderBy(x => x.Distance).ToArray();
        if (candidates.Length == 0) throw new InvalidOperationException($"No prototype system handles '{targetType.FullName}'.");
        if (candidates.Skip(1).Any(x => x.Distance == candidates[0].Distance && !ReferenceEquals(x.System, candidates[0].System)))
            throw new InvalidOperationException($"Multiple prototype systems handle '{targetType.FullName}' with equal specificity.");
        return candidates[0].System;
    }

    private static int InheritanceDistance(Type target, Type baseType)
    {
        if (target == baseType) return 0;
        if (baseType.IsInterface) return 1;
        var distance = 0;
        for (var current = target; current is not null; current = current.BaseType, distance++)
            if (current == baseType) return distance;
        return int.MaxValue;
    }

    private void AddName(string name, IPrototype prototype)
    {
        if (!_names.TryAdd(name, prototype)) throw new InvalidOperationException($"Duplicate prototype name or alias '{name}'.");
    }

    private static string GetTypeName(Type type) => type.FullName ?? type.Name;
    private void Bind(IPrototypeContext context)
    {
        if (context is APrototypeContext prototypeContext) prototypeContext.Bind(this);
    }
    private static PrototypeDocument Clone(PrototypeDocument source) => new()
    { Format = source.Format, Type = source.Type, Prototype = source.Prototype,
        Data = PrototypeSerializer.CloneMapping(source.Data) };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_initialized) _assets.UnregisterImporter<PrototypeAsset>();
    }
}
