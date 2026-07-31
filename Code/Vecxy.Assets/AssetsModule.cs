using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Autofac;
using Vecxy.Diagnostics;
using Vecxy.Kernel;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

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
    IAssetsManager,
    IConfigProvider
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

        protected override IReadOnlyList<Type> Exports => [typeof(IAssetsManager), typeof(IConfigProvider)];

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
    private readonly HashSet<Type> _registeredConfigs = [];
    private readonly Dictionary<string, List<IConfigRef>> _configRefs =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, Type> _extensionTypes =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<AssetLoadKey, IAssetRefEntry> _loaded = [];
    private readonly Dictionary<string, long> _pendingReloads =
        new(StringComparer.Ordinal);
    private readonly AssetImportContext _importContext;
    private readonly Options _options;
    private static readonly ISerializer ConfigSerializer =
        new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
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

        foreach (var extension in importer.Extensions)
        {
            var normalized = NormalizeExtension(extension);
            _extensionTypes.TryAdd(normalized, assetType);
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
            var normalized = NormalizeExtension(extension);
            if (_extensionTypes.TryGetValue(normalized, out var registeredType) &&
                registeredType == assetType)
            {
                _extensionTypes.Remove(normalized);
            }
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

    public void Register<T>() where T : class, IYamlConfig
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _registeredConfigs.Add(typeof(T));
    }

    public ConfigRef<T> LoadConfig<T>(string path) where T : class, IYamlConfig
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_registeredConfigs.Contains(typeof(T)))
        {
            throw new InvalidOperationException(
                $"Config type '{typeof(T).Name}' is not registered.");
        }

        using var source = Load<TextAsset>(path);
        var config = new ConfigRef<T>(source, UnregisterConfigRef);
        RegisterConfigRef(config);
        return config;
    }

    public IReadOnlyList<IConfigRef> GetLoadedConfigs()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _configRefs.Values
            .SelectMany(values => values)
            .GroupBy(config => config.Path, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(config => config.Path, StringComparer.Ordinal)
            .ToArray();
    }

    public void SaveConfig(IConfigRef config, object value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(value);

        if (!config.ValueType.IsInstanceOfType(value))
        {
            throw new InvalidOperationException(
                $"Config '{config.Path}' expects '{config.ValueType.Name}', got '{value.GetType().Name}'.");
        }

        if (value is not IYamlConfig yamlConfig)
        {
            throw new InvalidOperationException(
                $"Config '{config.Path}' does not implement IYamlConfig.");
        }

        try
        {
            yamlConfig.Validate();
        }
        catch (Exception e)
        {
            throw new ValidationException($"Config '{config.Path}' no validate!", e);
        }
       

        var fullPath = Path.Combine(AssetsDirectory, config.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var yaml = ConfigSerializer.Serialize(value);
        File.WriteAllText(fullPath, yaml);

        var assetId = Find(config.Path);
        Reload(assetId);
    }

    public AssetRef<T> Load<T>(AssetId id) where T : class
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var key = new AssetLoadKey(id, typeof(T));

        if (_loaded.TryGetValue(key, out var cached))
        {
            return Cast<T>(cached).CreateReference();
        }

        if (!Registry.TryGet(id, out var metadata) || metadata is null)
        {
            throw new KeyNotFoundException($"Unknown asset ID: {id}");
        }

        if (!CanImportAs(metadata, typeof(T)))
        {
            throw new InvalidCastException(
                $"Asset '{metadata.Path}' is {metadata.AssetType.Name}, not {typeof(T).Name}.");
        }

        AssetRefEntry<T> entry;
        try
        {
            var value = Import(metadata, typeof(T));
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

        _loaded.Add(key, entry);
        metadata.IsLoaded = true;
        return entry.CreateReference();
    }

    public bool IsLoaded(AssetId id) =>
        _loaded.Keys.Any(key => key.Id == id);

    public void Reload(AssetId id)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var entries = _loaded
            .Where(pair => pair.Key.Id == id)
            .Select(pair => pair.Value)
            .ToArray();

        if (entries.Length == 0)
        {
            return;
        }

        if (!Registry.TryGet(id, out var metadata) || metadata is null)
        {
            throw new KeyNotFoundException($"Unknown asset ID: {id}");
        }

        var reloaded = false;

        foreach (var assetRef in entries)
        {
            object replacement;
            try
            {
                replacement = Import(metadata, assetRef.ValueType);
            }
            catch (Exception exception)
            {
                assetRef.MarkFailed(exception);

                if (assetRef.IsLoaded)
                {
                    Logger.Error(
                        exception,
                        $"Asset hot reload failed, keeping previous value: {metadata.Path} ({assetRef.ValueType.Name})");
                    continue;
                }

                throw;
            }

            var previous = assetRef.Replace(replacement);
            DisposeValue(previous);
            reloaded = true;
        }

        if (reloaded)
        {
            NotifyConfigRefs(metadata.Path);
            Logger.Info($"Reloaded asset: {metadata.Path}");
        }
    }

    public void Unload<T>() where T : class
    {
        var assetType = typeof(T);
        var ids = _loaded
            .Where(pair => pair.Key.ValueType == assetType)
            .Select(pair => pair.Key)
            .ToArray();

        foreach (var key in ids)
        {
            UnloadEntry(_loaded[key]);
        }
    }

    public void OnInitialize()
    {
        Directory.CreateDirectory(AssetsDirectory);
        RegisterImporter<TextAsset>(new TextAssetImporter());
        RegisterImporter<ShaderAsset>(new ShaderAssetImporter());
        RegisterImporter<TextureAsset>(new TextureAssetImporter());
        RegisterImporter<MaterialAsset>(new MaterialAssetImporter());
        RegisterImporter<InputAsset>(new InputAssetImporter());
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

    private object Import(
        AssetMetadata metadata,
        Type assetType)
    {
        if (!_importers.TryGetValue(assetType, out var importer))
        {
            throw new InvalidOperationException(
                $"No importer is registered for {assetType.Name}.");
        }

        return importer.Import(metadata, _importContext);
    }

    private bool CanImportAs(
        AssetMetadata metadata,
        Type assetType)
    {
        if (metadata.AssetType == assetType)
        {
            return true;
        }

        if (!_importers.TryGetValue(assetType, out var importer))
        {
            return false;
        }

        var extension = NormalizeExtension(Path.GetExtension(metadata.Path));
        return importer.Extensions.Any(value =>
            string.Equals(
                NormalizeExtension(value),
                extension,
                StringComparison.OrdinalIgnoreCase));
    }

    private static AssetRefEntry<T> Cast<T>(IAssetRefEntry assetRef) where T : class =>
        assetRef is AssetRefEntry<T> entry
            ? entry
            : throw new InvalidCastException(
                $"Asset {assetRef.Id} is {assetRef.ValueType.Name}, not {typeof(T).Name}.");

    private void ReleaseEntry<T>(AssetRefEntry<T> entry) where T : class
    {
        if (_disposed ||
            !_loaded.TryGetValue(
                new AssetLoadKey(entry.Id, entry.ValueType),
                out var current) ||
            !ReferenceEquals(current, entry))
        {
            return;
        }

        UnloadEntry(entry);
    }

    private void UnloadEntry(IAssetRefEntry entry)
    {
        _loaded.Remove(new AssetLoadKey(entry.Id, entry.ValueType));
        entry.Metadata.IsLoaded = _loaded.Keys.Any(key => key.Id == entry.Id);

        var value = entry.ForceUnload();
        DisposeValue(value);
        Unloaded?.Invoke(entry.Id, entry.ValueType);
    }

    private void RegisterConfigRef(IConfigRef configRef)
    {
        if (!_configRefs.TryGetValue(configRef.Path, out var refs))
        {
            refs = [];
            _configRefs.Add(configRef.Path, refs);
        }

        refs.Add(configRef);
    }

    private void UnregisterConfigRef(IConfigRef configRef)
    {
        if (!_configRefs.TryGetValue(configRef.Path, out var refs))
            return;

        refs.Remove(configRef);
        if (refs.Count == 0)
            _configRefs.Remove(configRef.Path);
    }

    private void NotifyConfigRefs(string path)
    {
        if (!_configRefs.TryGetValue(path, out var refs))
            return;

        foreach (var configRef in refs.ToArray())
            configRef.NotifySourceChanged();
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

    private readonly record struct AssetLoadKey(
        AssetId Id,
        Type ValueType);
}
