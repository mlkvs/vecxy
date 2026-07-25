using System.Diagnostics;
using Autofac;
using Vecxy.Diagnostics;
using Vecxy.Kernel;

namespace Vecxy.Assets;

public interface IAssetsManager
{
    string AssetsDirectory { get; }
    AssetRegistry Registry { get; }
    event Action<AssetId, Type>? Unloaded;

    void RegisterImporter<T>(IAssetImporter<T> importer) where T : class;
    void UnregisterImporter<T>() where T : class;
    AssetId Find(string path);
    AssetRef<T> Load<T>(AssetId id) where T : class;
    AssetRef<T> Load<T>(string path) where T : class;
    bool IsLoaded(AssetId id);
    void Reload(AssetId id);
    void Unload<T>() where T : class;
}

public sealed class AssetsModule :
    IModule,
    IModule.IUpdatable,
    IAssetsManager
{
    public sealed class Options
    {
        public string? AssetsDirectory { get; init; }
        public bool HotReloadEnabled { get; init; } = true;
        public TimeSpan HotReloadDelay { get; init; } =
            TimeSpan.FromMilliseconds(150);
    }

    public sealed class Definition : AModuleDefinition<AssetsModule>
    {
        private readonly Options _options;

        protected override IReadOnlyList<Type> Exports => [typeof(IAssetsManager)];

        public Definition(Options? options = null)
        {
            _options = options ?? new Options();
        }

        protected override void RegisterModule(ContainerBuilder builder)
        {
            builder
                .RegisterInstance(_options)
                .AsSelf()
                .SingleInstance();

            builder
                .RegisterType<AssetsModule>()
                .AsSelf()
                .SingleInstance();
        }
    }

    private readonly Dictionary<Type, IAssetImporter> _importers = [];
    private readonly Dictionary<string, Type> _extensionTypes =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<AssetId, IAssetRefEntry> _loaded = [];
    private readonly Dictionary<string, long> _pendingReloads =
        new(StringComparer.Ordinal);
    private readonly AssetImportContext _importContext;
    private readonly Options _options;
    private AssetFileWatcher? _fileWatcher;
    private bool _disposed;

    public string AssetsDirectory { get; }
    public AssetRegistry Registry { get; } = new();
    public event Action<AssetId, Type>? Unloaded;

    public AssetsModule(Options options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.HotReloadDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Hot reload delay cannot be negative.");
        }

        _options = options;
        AssetsDirectory = Path.GetFullPath(
            options.AssetsDirectory ??
            Path.Combine(AppContext.BaseDirectory, "Assets"));
        _importContext = new AssetImportContext(AssetsDirectory, this);
    }

    public void RegisterImporter<T>(IAssetImporter<T> importer) where T : class
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(importer);

        var assetType = typeof(T);
        var adapter = new AssetImporter<T>(importer);
        if (!_importers.TryAdd(assetType, adapter))
        {
            throw new InvalidOperationException(
                $"An importer for {assetType.Name} is already registered.");
        }

        var registeredExtensions = new List<string>();
        foreach (var extension in importer.Extensions)
        {
            var normalized = NormalizeExtension(extension);
            if (!_extensionTypes.TryAdd(normalized, assetType))
            {
                foreach (var registered in registeredExtensions)
                {
                    _extensionTypes.Remove(registered);
                }

                _importers.Remove(assetType);
                throw new InvalidOperationException(
                    $"An importer for extension '{normalized}' is already registered.");
            }

            registeredExtensions.Add(normalized);
        }
    }

    public void UnregisterImporter<T>() where T : class
    {
        var assetType = typeof(T);
        if (!_importers.Remove(assetType, out var importer))
        {
            return;
        }

        foreach (var extension in importer.Extensions)
        {
            _extensionTypes.Remove(NormalizeExtension(extension));
        }
    }

    public AssetId Find(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var normalized = NormalizePath(path);

        if (Registry.TryFind(normalized, out var existing))
        {
            return existing;
        }

        var extension = Path.GetExtension(normalized);
        if (!_extensionTypes.TryGetValue(extension, out var assetType))
        {
            throw new NotSupportedException($"No asset importer is registered for '{extension}'.");
        }

        var id = AssetId.FromPath(normalized);
        Registry.Add(new AssetMetadata
        {
            Id = id,
            AssetType = assetType,
            Path = normalized
        });

        return id;
    }

    public AssetRef<T> Load<T>(string path) where T : class =>
        Load<T>(Find(path));

    public AssetRef<T> Load<T>(AssetId id) where T : class
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_loaded.TryGetValue(id, out var cached))
        {
            return Cast<T>(cached).CreateReference();
        }

        if (!Registry.TryGet(id, out var metadata) || metadata is null)
        {
            throw new KeyNotFoundException($"Unknown asset ID: {id}");
        }

        if (metadata.AssetType != typeof(T))
        {
            throw new InvalidCastException(
                $"Asset '{metadata.Path}' is {metadata.AssetType.Name}, not {typeof(T).Name}.");
        }

        AssetRefEntry<T> entry;
        try
        {
            var value = Import(metadata);
            entry = new AssetRefEntry<T>(
                metadata,
                (T)value,
                ReleaseEntry);
        }
        catch (Exception exception)
        {
            entry = new AssetRefEntry<T>(
                metadata,
                exception,
                ReleaseEntry);
            Logger.Error(
                exception,
                $"Asset import failed, using fallback: {metadata.Path}");
        }

        _loaded.Add(id, entry);
        metadata.IsLoaded = true;
        return entry.CreateReference();
    }

    public bool IsLoaded(AssetId id) => _loaded.ContainsKey(id);

    public void Reload(AssetId id)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_loaded.TryGetValue(id, out var assetRef))
        {
            return;
        }

        if (!Registry.TryGet(id, out var metadata) || metadata is null)
        {
            throw new KeyNotFoundException($"Unknown asset ID: {id}");
        }

        object replacement;
        try
        {
            replacement = Import(metadata);
        }
        catch (Exception exception)
        {
            assetRef.MarkFailed(exception);
            throw;
        }

        var previous = assetRef.Replace(replacement);
        DisposeValue(previous);
        Logger.Info($"Reloaded asset: {metadata.Path}");
    }

    public void Unload<T>() where T : class
    {
        var assetType = typeof(T);
        var ids = _loaded
            .Where(pair => pair.Value.ValueType == assetType)
            .Select(pair => pair.Key)
            .ToArray();

        foreach (var id in ids)
        {
            UnloadEntry(_loaded[id]);
        }
    }

    public void OnInitialize()
    {
        Directory.CreateDirectory(AssetsDirectory);
        RegisterImporter<TextAsset>(new TextAssetImporter());
        RegisterImporter<ShaderAsset>(new ShaderAssetImporter());
        RegisterImporter<TextureAsset>(new TextureAssetImporter());
        RegisterImporter<MaterialAsset>(new MaterialAssetImporter());
        RegisterImporter<ModelAsset>(new ModelAssetImporter());
        Logger.Info($"Assets directory: {AssetsDirectory}");

        if (_options.HotReloadEnabled)
        {
            _fileWatcher = new AssetFileWatcher(AssetsDirectory);
            _fileWatcher.Start();
            Logger.Info("Asset hot reload is enabled.");
        }
    }

    public void OnUpdate(float deltaTime)
    {
        if (_fileWatcher is null)
        {
            return;
        }

        var now = Stopwatch.GetTimestamp();
        _fileWatcher.Drain(path => _pendingReloads[path] = now);

        foreach (var (path, changedAt) in _pendingReloads.ToArray())
        {
            if (Stopwatch.GetElapsedTime(changedAt, now) < _options.HotReloadDelay)
            {
                continue;
            }

            _pendingReloads.Remove(path);
            ReloadChangedAsset(path);
        }
    }

    public void OnShutdown()
    {
        StopFileWatcher();
    }

    private void ReloadChangedAsset(string path)
    {
        if (!Registry.TryFind(path, out var id) || !IsLoaded(id))
        {
            return;
        }

        try
        {
            Reload(id);
        }
        catch (Exception exception)
        {
            Logger.Error(exception, $"Asset hot reload failed, keeping previous value: {path}");
        }
    }

    private void StopFileWatcher()
    {
        _fileWatcher?.Dispose();
        _fileWatcher = null;
        _pendingReloads.Clear();
    }

    private object Import(AssetMetadata metadata)
    {
        if (!_importers.TryGetValue(metadata.AssetType, out var importer))
        {
            throw new InvalidOperationException(
                $"No importer is registered for {metadata.AssetType.Name}.");
        }

        return importer.Import(metadata, _importContext);
    }

    private static AssetRefEntry<T> Cast<T>(IAssetRefEntry assetRef) where T : class =>
        assetRef is AssetRefEntry<T> entry
            ? entry
            : throw new InvalidCastException(
                $"Asset {assetRef.Id} is {assetRef.ValueType.Name}, not {typeof(T).Name}.");

    private void ReleaseEntry<T>(AssetRefEntry<T> entry) where T : class
    {
        if (_disposed ||
            !_loaded.TryGetValue(entry.Id, out var current) ||
            !ReferenceEquals(current, entry))
        {
            return;
        }

        UnloadEntry(entry);
    }

    private void UnloadEntry(IAssetRefEntry entry)
    {
        _loaded.Remove(entry.Id);
        entry.Metadata.IsLoaded = false;

        var value = entry.ForceUnload();
        DisposeValue(value);
        Unloaded?.Invoke(entry.Id, entry.ValueType);
    }

    private static void DisposeValue(object? value)
    {
        if (value is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    internal static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalized = path.Replace('\\', '/').TrimStart('/');

        if (normalized.Length == 0)
        {
            throw new ArgumentException("Asset path cannot be empty.", nameof(path));
        }

        if (normalized.Split('/').Any(segment => segment is "." or ".."))
        {
            throw new ArgumentException("Asset path cannot contain '.' or '..' segments.", nameof(path));
        }

        return normalized;
    }

    private static string NormalizeExtension(string extension) =>
        extension.StartsWith('.') ? extension : $".{extension}";

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopFileWatcher();
        foreach (var assetRef in _loaded.Values.ToArray())
        {
            UnloadEntry(assetRef);
        }

        _loaded.Clear();
        _importers.Clear();
        _extensionTypes.Clear();
    }
}
