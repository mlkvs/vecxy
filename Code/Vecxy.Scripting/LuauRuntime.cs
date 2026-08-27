using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.RegularExpressions;
using NuLua;
using NuLua.Luau;
using Vecxy.Assets;
using Vecxy.Diagnostics;

namespace Vecxy.Scripting;

internal sealed class LuauRuntime(IAssetsManager assets) : IScriptRuntime
{
    private readonly List<LuauInstance> _instances = [];

    public IScriptInstance Create(IAssetHandle script, ScriptContext? context = null, ScriptRuntimeOptions? options = null) =>
        Create(assets.GetPath(script), context, options);

    public IScriptInstance Create(string path, ScriptContext? context = null, ScriptRuntimeOptions? options = null)
    {
        var instance = new LuauInstance(assets, path, context ?? new ScriptContext(), Remove);
        _instances.Add(instance);
        return instance;
    }

    public void Update()
    {
        foreach (var instance in _instances.ToArray()) instance.ReloadIfChanged();
    }

    public void DisposeAll()
    {
        foreach (var instance in _instances.ToArray()) instance.Dispose();
        _instances.Clear();
    }

    private void Remove(LuauInstance instance) => _instances.Remove(instance);
}

internal sealed partial class LuauInstance : IScriptInstance
{
    private sealed record Program(LuauState State, LuaTable Exports, IReadOnlyList<AssetRef<ScriptAsset>> Assets);

    private readonly IAssetsManager _assets;
    private readonly string _entryPath;
    private readonly ScriptContext _context;
    private readonly Action<LuauInstance> _onDisposed;
    private Program _program;
    private Dictionary<AssetId, int> _versions;
    private bool _disposed;

    public string Path => _entryPath;

    public LuauInstance(IAssetsManager assets, string entryPath, ScriptContext context, Action<LuauInstance> onDisposed)
    {
        _assets = assets;
        _entryPath = Normalize(entryPath);
        _context = context;
        _onDisposed = onDisposed;
        _program = CompileProgram();
        _versions = CaptureVersions(_program.Assets);
    }

    public bool HasFunction(string name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _program.Exports[name].Type == LuaValueType.Function;
    }

    public object? Invoke(string name, params object?[] arguments)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var value = _program.Exports[name];
        if (value.Type != LuaValueType.Function)
            throw new MissingMethodException($"Luau module '{Path}' does not export function '{name}'.");
        try
        {
            var args = arguments.Select(argument => ToLuau(_program.State, argument)).ToArray();
            var results = value.Read<LuaFunction>().Invoke(args);
            return results.Length == 0 ? null : FromLuau(results[0]);
        }
        catch (Exception exception)
        {
            throw new ScriptExecutionException(Path, name, exception);
        }
    }

    public object? InvokeOptional(string name, params object?[] arguments) =>
        HasFunction(name) ? Invoke(name, arguments) : null;

    internal void ReloadIfChanged()
    {
        if (_disposed || !_program.Assets.Any(asset =>
                !_versions.TryGetValue(asset.Id, out var version) || asset.Version != version)) return;
        try
        {
            var replacement = CompileProgram();
            try { InvokeOn(replacement, "onReload"); }
            catch { DisposeProgram(replacement); throw; }
            var previous = _program;
            _program = replacement;
            _versions = CaptureVersions(replacement.Assets);
            DisposeProgram(previous);
            Logger.Info($"Luau module graph hot reloaded: {Path}");
        }
        catch (Exception exception)
        {
            _versions = CaptureVersions(_program.Assets);
            Logger.Error(exception, $"Luau hot reload failed, keeping previous VM: {Path}");
        }
    }

    private Program CompileProgram()
    {
        var loaded = new Dictionary<string, AssetRef<ScriptAsset>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            LoadGraph(_entryPath, null, loaded);
            var sources = loaded.ToDictionary(
                pair => pair.Key,
                pair => RewriteRequires(pair.Key, pair.Value.Value.Source),
                StringComparer.OrdinalIgnoreCase);
            var state = LuauState.Create();
            state.OpenLibraries();
            RegisterCapabilities(state);
            state.UseModuleLoader(new AssetRequirer(sources));
            var result = state.DoString(sources[_entryPath]);
            if (result.Length == 0 || result[0].Type != LuaValueType.Table)
            {
                state.Dispose();
                throw new InvalidDataException($"Luau entry module '{_entryPath}' must return a table.");
            }
            return new Program(state, result[0].Read<LuaTable>(), loaded.Values.ToArray());
        }
        catch (Exception exception)
        {
            DisposeAssets(loaded.Values);
            if (exception is ScriptExecutionException) throw;
            throw new ScriptExecutionException(_entryPath, null, exception);
        }
    }

    private void LoadGraph(string path, PackageId? ownerPackage, IDictionary<string, AssetRef<ScriptAsset>> loaded)
    {
        path = Normalize(path);
        if (loaded.ContainsKey(path)) return;
        var asset = _assets.Load<ScriptAsset>(path);
        try
        {
            if (ownerPackage is { } owner && asset.Metadata.Package != owner)
            {
                var package = _assets.GetPackage(owner);
                if (!package.Dependencies.Contains(asset.Metadata.Package))
                    throw new InvalidDataException($"Luau module '{path}' belongs to undeclared package dependency '{asset.Metadata.Package}'.");
            }
            loaded.Add(path, asset);
            foreach (Match match in RequirePattern().Matches(asset.Value.Source))
                LoadGraph(ResolveModulePath(path, match.Groups["path"].Value), asset.Metadata.Package, loaded);
        }
        catch
        {
            if (!loaded.ContainsKey(path)) asset.Dispose();
            throw;
        }
    }

    private string RewriteRequires(string ownerPath, string source) => RequirePattern().Replace(source, match =>
        $"require(\"{ResolveModulePath(ownerPath, match.Groups["path"].Value)}\")");

    private void RegisterCapabilities(LuauState state)
    {
        foreach (var (name, capability) in _context.Capabilities)
            state[name] = CreateObjectTable(state, capability);
    }

    private static LuaTable CreateObjectTable(LuauState state, object instance)
    {
        var table = state.CreateTable();
        foreach (var property in instance.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
                     .Where(property => property.CanRead && property.GetIndexParameters().Length == 0))
            table[property.Name] = ToLuau(state, property.GetValue(instance));
        foreach (var method in instance.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                     .Where(method => !method.IsSpecialName && method.DeclaringType != typeof(object)))
        {
            table[method.Name] = state.CreateFunction((callState, callArguments) =>
            {
                var parameters = method.GetParameters();
                if (callArguments.Length != parameters.Length)
                    throw new InvalidOperationException($"{method.Name} expects {parameters.Length} arguments.");
                var args = parameters.Select((parameter, index) => FromLuau(callArguments[index], parameter.ParameterType)).ToArray();
                var result = method.Invoke(instance, args);
                if (method.ReturnType == typeof(void)) return 0;
                callState.Push(ToLuau(callState, result));
                return 1;
            });
        }
        return table;
    }

    private static LuaValue ToLuau(LuauState state, object? value)
    {
        if (value is null) return LuaValue.Nil;
        if (value is LuaValue luau) return luau;
        if (value is string text) return text;
        if (value is bool boolean) return boolean;
        if (value is char character) return character.ToString();
        if (value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
            return LuaValue.FromNumber(Convert.ToDouble(value));
        if (value is IEnumerable enumerable)
        {
            var table = state.CreateTable();
            var index = 1;
            foreach (var item in enumerable) table[index++] = ToLuau(state, item);
            return table;
        }
        return CreateObjectTable(state, value);
    }

    private static object? FromLuau(LuaValue value, Type? targetType = null)
    {
        if (value.IsNil) return null;
        if (targetType is not null && targetType != typeof(object))
        {
            if (targetType == typeof(string)) return value.Read<string>();
            if (targetType == typeof(bool)) return value.Read<bool>();
            if (targetType == typeof(int)) return value.Read<int>();
            if (targetType == typeof(float)) return value.Read<float>();
            if (targetType == typeof(double)) return value.Read<double>();
            if (targetType.IsArray && value.Type == LuaValueType.Table)
            {
                var table = value.Read<LuaTable>();
                var element = targetType.GetElementType()!;
                var array = Array.CreateInstance(element, table.Length);
                for (var i = 0; i < table.Length; i++) array.SetValue(FromLuau(table[i + 1], element), i);
                return array;
            }
        }
        return value.Type switch
        {
            LuaValueType.Boolean => value.Read<bool>(),
            LuaValueType.Number => value.Read<double>(),
            LuaValueType.String => value.Read<string>(),
            LuaValueType.Table => Enumerable.Range(1, value.Read<LuaTable>().Length)
                .Select(index => FromLuau(value.Read<LuaTable>()[index])).ToArray(),
            _ => value.Read<object>()
        };
    }

    private static string ResolveModulePath(string ownerPath, string request)
    {
        if (string.IsNullOrWhiteSpace(request) || System.IO.Path.IsPathRooted(request) || Uri.TryCreate(request, UriKind.Absolute, out _))
            throw new InvalidDataException($"Invalid Luau module path '{request}' in '{ownerPath}'.");
        var combined = request.StartsWith('.')
            ? System.IO.Path.Combine(System.IO.Path.GetDirectoryName(ownerPath) ?? string.Empty, request)
            : request;
        var normalized = Normalize(combined);
        if (normalized == ".." || normalized.StartsWith("../", StringComparison.Ordinal))
            throw new InvalidDataException($"Luau require escapes the asset root: '{request}' from '{ownerPath}'.");
        if (System.IO.Path.GetExtension(normalized).Length == 0) normalized += ".luau";
        if (!string.Equals(System.IO.Path.GetExtension(normalized), ".luau", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Luau modules may require only .luau assets: '{request}'.");
        return normalized;
    }

    private static string Normalize(string path)
    {
        var result = new List<string>();
        foreach (var segment in path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".") continue;
            if (segment == "..") { if (result.Count == 0) return "../"; result.RemoveAt(result.Count - 1); }
            else result.Add(segment);
        }
        return string.Join('/', result);
    }

    private static void InvokeOn(Program program, string name)
    {
        var value = program.Exports[name];
        if (value.Type == LuaValueType.Function)
            value.Read<LuaFunction>().Invoke([]);
    }

    private static Dictionary<AssetId, int> CaptureVersions(IEnumerable<AssetRef<ScriptAsset>> assets) =>
        assets.ToDictionary(asset => asset.Id, asset => asset.Version);
    private static void DisposeAssets(IEnumerable<AssetRef<ScriptAsset>> assets) { foreach (var asset in assets) asset.Dispose(); }
    private static void DisposeProgram(Program program) { program.State.Dispose(); DisposeAssets(program.Assets); }

    public void Dispose()
    {
        if (_disposed) return;
        try { InvokeOptional("dispose"); }
        finally { _disposed = true; DisposeProgram(_program); _onDisposed(this); }
    }

    [GeneratedRegex("\\brequire\\s*\\(\\s*[\"'](?<path>[^\"']+)[\"']\\s*\\)", RegexOptions.CultureInvariant)]
    private static partial Regex RequirePattern();

    private sealed class AssetRequirer(IReadOnlyDictionary<string, string> sources) : LuaModuleLoader
    {
        protected override bool TryLoadModule(ILuaState state, string fullPath, string requireArgument)
        {
            if (!sources.TryGetValue(fullPath, out var source)) return false;
            var result = state.DoString(source);
            state.Push(result.Length == 0 ? LuaValue.Nil : result[0]);
            return true;
        }

        protected override bool TryGetAliasPath(string alias, [NotNullWhen(true)] out string? path) { path = null; return false; }
    }
}
