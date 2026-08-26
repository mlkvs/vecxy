using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
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
    bool Exists(string path);
    string GetPath(IAssetHandle handle);
    byte[] ReadAllBytes(IAssetHandle handle);
    AssetRef<T> Load<T>(AssetId id) where T : class;
    AssetRef<T> Load<T>(IAssetHandle handle) where T : class;
    AssetRef<T> Load<T>(string path) where T : class;
    bool IsLoaded(AssetId id);
    void Reload(AssetId id);
    AssetPackage GetPackage(PackageId id);
    ValueTask<AssetPackageLease> LoadPackageAsync(PackageId id, CancellationToken cancellationToken = default);
    void Unload<T>() where T : class;
    void LoadManifest(string path);
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
        public IReadOnlyList<string> AdditionalAssetDirectories { get; init; } = [];
        public bool HotReloadEnabled { get; init; } = true;
        public TimeSpan HotReloadDelay { get; init; } =
            TimeSpan.FromMilliseconds(150);
        public string? PackagesDirectory { get; init; }
        public string? ApplicationId { get; init; }
        public string? PackageCacheDirectory { get; init; }
        public IRemotePackageTransport? RemoteTransport { get; init; }
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
    private readonly Dictionary<string, List<IConfigRef>> _configRefs =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, Type> _extensionTypes =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<AssetLoadKey, IAssetRefEntry> _loaded = [];
    private readonly Dictionary<string, long> _pendingReloads =
        new(StringComparer.Ordinal);
    private readonly AssetImportContext _importContext;
    private readonly IReadOnlyList<string> _assetDirectories;
    private readonly Options _options;
    private static readonly ISerializer ConfigSerializer =
        new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
    private readonly List<AssetFileWatcher> _fileWatchers = [];
    private AssetPackageManager? _packages;
    private AssetManifest? _manifest;
    private readonly List<AssetPackageLease> _startupPackageLeases = [];
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
        var assemblyAssetDirectories = Assembly.GetEntryAssembly()?
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(attribute => attribute.Key == "VecxyAdditionalAssetsDirectory")
            .Select(attribute => attribute.Value)
            .OfType<string>() ?? [];
        _assetDirectories = (options.AdditionalAssetDirectories ?? [])
            .Concat(assemblyAssetDirectories)
            .Select(Path.GetFullPath)
            .Where(directory => !string.Equals(
                directory,
                AssetsDirectory,
                StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _importContext = new AssetImportContext(
            AssetsDirectory,
            _assetDirectories,
            this,
            ReadPackagedAsset);
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

    public bool Exists(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Registry.TryFind(NormalizePath(path), out _);
    }

    public AssetRef<T> Load<T>(string path) where T : class =>
        Load<T>(Find(path));

    public AssetRef<T> Load<T>(IAssetHandle handle) where T : class
    {
        ArgumentNullException.ThrowIfNull(handle);
        return Load<T>(handle.Id);
    }

    public string GetPath(IAssetHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (!Registry.TryGet(handle.Id, out var metadata) || metadata is null)
            throw new KeyNotFoundException($"Unknown asset ID: {handle.Id}");
        return metadata.Path;
    }

    public byte[] ReadAllBytes(IAssetHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (!Registry.TryGet(handle.Id, out var metadata) || metadata is null)
            throw new KeyNotFoundException($"Unknown asset ID: {handle.Id}");
        if (_packages is not null && !_packages.IsLoaded(metadata.Package))
            throw new AssetPackageNotLoadedException(_packages.Get(metadata.Package).Name, handle.Id);
        return _importContext.ReadAllBytes(metadata.Path);
    }

    public void LoadManifest(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var manifest = AssetManifest.Load(path);
        _manifest = manifest;
        foreach (var entry in manifest.Assets)
        {
            var normalized = NormalizePath(entry.Path);
            if (Registry.TryGet(new AssetId(entry.Id), out _))
                continue;
            if (Registry.TryFind(normalized, out _))
                continue;

            var extension = Path.GetExtension(normalized);
            var assetType = _extensionTypes.GetValueOrDefault(extension) ?? typeof(object);

            Registry.Add(new AssetMetadata
            {
                Id = new AssetId(entry.Id),
                AssetType = assetType,
                Path = normalized,
                Package = entry.Package
            });
        }
    }

    public AssetPackage GetPackage(PackageId id) =>
        _packages?.Get(id) ?? throw new InvalidOperationException("Asset packages are not initialized.");

    public ValueTask<AssetPackageLease> LoadPackageAsync(PackageId id, CancellationToken cancellationToken = default) =>
        GetPackage(id).LoadAsync(cancellationToken);


    public ConfigRef<T> LoadConfig<T>(string path) where T : class, IYamlConfig
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var source = Load<TextAsset>(path);
        var config = new ConfigRef<T>(
            source,
            () => _importContext.ReadAllTextLayers(path),
            UnregisterConfigRef);
        RegisterConfigRef(config);
        return config;
    }

    public ConfigRef<T> LoadConfig<T>(ConfigHandle handle) where T : class, IYamlConfig
    {
        if (!Registry.TryGet(handle.Id, out var metadata) || metadata is null)
            throw new KeyNotFoundException($"Unknown config asset ID: {handle.Id}");
        return LoadConfig<T>(metadata.Path);
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

        if (_packages is not null && !_packages.IsLoaded(metadata.Package))
            throw new AssetPackageNotLoadedException(_packages.Get(metadata.Package).Name, id);

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
        var packagesDirectory = FindPackagesDirectory();
        if (packagesDirectory is null)
            Directory.CreateDirectory(AssetsDirectory);
        RegisterImporter<TextAsset>(new TextAssetImporter());
        RegisterImporter<ShaderAsset>(new ShaderAssetImporter());
        RegisterImporter<TextureAsset>(new TextureAssetImporter());
        RegisterImporter<MaterialAsset>(new MaterialAssetImporter());
        RegisterImporter<InputAsset>(new InputAssetImporter());
        RegisterImporter<ModelAsset>(new ModelAssetImporter());
        var manifestPath = new[]
            {
                Path.Combine(
                    Directory.GetParent(AssetsDirectory)?.FullName ?? AssetsDirectory,
                    "Assets.manifest"),
                Path.Combine(AssetsDirectory, "Assets.manifest")
            }
            .FirstOrDefault(File.Exists);
        if (manifestPath is not null)
            LoadManifest(manifestPath);
        InitializePackages();
        Logger.Info($"Assets directory: {AssetsDirectory}");
        foreach (var directory in _assetDirectories)
            Logger.Info($"Additional assets directory: {directory}");

        if (_options.HotReloadEnabled && packagesDirectory is null)
        {
            foreach (var directory in new[] { AssetsDirectory }.Concat(_assetDirectories))
            {
                if (!Directory.Exists(directory))
                {
                    Logger.Warning($"Cannot watch missing assets directory: {directory}");
                    continue;
                }

                var watcher = new AssetFileWatcher(directory);
                watcher.Start();
                _fileWatchers.Add(watcher);
            }
            Logger.Info("Asset hot reload is enabled.");
        }
    }

    public void OnUpdate(float deltaTime)
    {
        if (_fileWatchers.Count == 0)
        {
            return;
        }

        var now = Stopwatch.GetTimestamp();
        foreach (var watcher in _fileWatchers)
            watcher.Drain(path => _pendingReloads[path] = now);

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
        StopFileWatchers();
    }

    private void ReloadChangedAsset(string path)
    {
        if (!Registry.TryFind(path, out var id))
        {
            return;
        }

        var fullPath = Path.Combine(AssetsDirectory, path.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
        {
            Logger.Error($"[Vecxy Assets]\n\nMissing:\n{id}\n{path}");
            return;
        }

        if (!IsLoaded(id))
            return;

        try
        {
            Reload(id);
        }
        catch (Exception exception)
        {
            Logger.Error(exception, $"Asset hot reload failed, keeping previous value: {path}");
        }
    }

    private void StopFileWatchers()
    {
        foreach (var watcher in _fileWatchers)
            watcher.Dispose();
        _fileWatchers.Clear();
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
        _packages?.TryUnload(entry.Metadata.Package);
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

    private void InitializePackages()
    {
        if (_manifest is null || _manifest.Packages.Count == 0) return;
        var directory = FindPackagesDirectory();
        if (directory is null)
        {
            _packages = new AssetPackageManager(AssetsDirectory, _manifest.Packages, _ => false,
                useLooseAssets: true);
            AssetPackages.Bind(_packages);
            return;
        }
        var packageManifestPath = Path.Combine(directory, "packages.manifest");
        _packages = new AssetPackageManager(directory, _manifest.Packages, package => _loaded.Keys.Any(key =>
            Registry.TryGet(key.Id, out var metadata) && metadata?.Package == package));
        var applicationId = _options.ApplicationId ?? Assembly.GetEntryAssembly()?.GetName().Name ?? "Vecxy.Game";
        var platform = OperatingSystem.IsAndroid() ? VPackPlatform.Android : OperatingSystem.IsLinux() ? VPackPlatform.Linux : VPackPlatform.Windows;
        var architecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        var transport = _options.RemoteTransport ?? new HttpRemotePackageTransport();
        var remote = new RemotePackageManager(platform, architecture, _manifest.Packages,
            new PackageCache(applicationId, _options.PackageCacheDirectory),
            transport, _packages.SetFile, ownsTransport: _options.RemoteTransport is null);
        _packages.SetRemote(remote);
        var packageManifest = System.Text.Json.JsonSerializer.Deserialize<VPackBuildManifest>(
            File.ReadAllText(packageManifestPath), AssetManifest.SerializerOptions)
            ?? throw new InvalidDataException($"Package manifest is empty: {packageManifestPath}");
        _packages.SetFiles(packageManifest);
        AssetPackages.Bind(_packages);
        foreach (var package in _manifest.Packages.Where(x => x.Load == PackageLoadMode.Startup))
            _startupPackageLeases.Add(_packages.Get(package.Id).EnsureLoadedAsync(cancellationToken: CancellationToken.None).AsTask().GetAwaiter().GetResult());
    }

    private string? FindPackagesDirectory()
    {
        var candidates = _options.PackagesDirectory is null
            ? new[] { AssetsDirectory, AppContext.BaseDirectory }
            : new[] { Path.GetFullPath(_options.PackagesDirectory) };
        return candidates.FirstOrDefault(directory => File.Exists(Path.Combine(directory, "packages.manifest")));
    }

    private byte[]? ReadPackagedAsset(string path)
    {
        if (_packages is null || _packages.UsesLooseAssets || !Registry.TryFind(NormalizePath(path), out var id) ||
            !Registry.TryGet(id, out var metadata) || metadata is null)
            return null;
        return _packages.ReadAsync(metadata.Package, id).AsTask().GetAwaiter().GetResult().ToArray();
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
        StopFileWatchers();
        foreach (var assetRef in _loaded.Values.ToArray())
        {
            UnloadEntry(assetRef);
        }

        _loaded.Clear();
        _importers.Clear();
        _extensionTypes.Clear();
        foreach (var lease in _startupPackageLeases) lease.Dispose();
        _startupPackageLeases.Clear();
        if (_packages is not null)
        {
            AssetPackages.Unbind(_packages);
            _packages.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private readonly record struct AssetLoadKey(
        AssetId Id,
        Type ValueType);
}
