using System.Collections.Concurrent;
using System.Security.Cryptography;
using Vecxy.Diagnostics;

namespace Vecxy.Assets;

public sealed class RemotePackageManager : IAsyncDisposable
{
    private readonly VPackPlatform _platform;
    private readonly string _architecture;
    private readonly Dictionary<PackageId, AssetPackageManifestEntry> _definitions;
    private readonly PackageCache _cache;
    private readonly IRemotePackageTransport _transport;
    private readonly Action<PackageId, string> _activate;
    private readonly bool _ownsTransport;
    private readonly ConcurrentDictionary<Uri, Task<RemotePackageManifest>> _manifests = [];
    private readonly ConcurrentDictionary<PackageId, SharedDownload> _downloads = [];
    private readonly Dictionary<PackageId, BundledPackage> _bundled = [];
    private readonly Dictionary<PackageId, PackageState> _states = [];

    public RemotePackageManager(VPackPlatform platform, string architecture,
        IEnumerable<AssetPackageManifestEntry> definitions, PackageCache cache,
        IRemotePackageTransport transport, Action<PackageId, string> activate, bool ownsTransport = false)
    {
        _platform = platform; _architecture = architecture; _definitions = definitions.ToDictionary(x => x.Id);
        _cache = cache; _transport = transport; _activate = activate; _ownsTransport = ownsTransport;
        foreach (var definition in _definitions.Values) _states[definition.Id] = PackageState.NotInstalled;
    }

    internal void SetBundled(VPackBuildManifest manifest, string directory)
    {
        foreach (var entry in manifest.Packages)
        {
            var path = Path.GetFullPath(entry.File, directory);
            _bundled[entry.Id] = new(entry.Version, path, entry.Size > 0 ? entry.Size : File.Exists(path) ? new FileInfo(path).Length : 0, entry.Sha256);
            _activate(entry.Id, path);
        }
    }

    public async Task<RemotePackageStatus> GetStatusAsync(PackageId id, bool refreshRemote = false, CancellationToken cancellationToken = default)
    {
        var definition = GetDefinition(id); var local = await ResolveLocalAsync(definition, cancellationToken);
        RemoteResolvedPackage? remote = null; Exception? remoteFailure = null;
        if (definition.Remote is not null && (refreshRemote || definition.Remote.Update != PackageUpdatePolicy.Manual))
        {
            try { remote = await ResolveRemoteAsync(definition, refreshRemote, cancellationToken); }
            catch (Exception exception) when (local is not null && exception is not OperationCanceledException) { remoteFailure = exception; }
        }
        var update = local is not null && remote is not null && remote.Version > local.Version;
        var state = update ? PackageState.UpdateAvailable : local is not null ? PackageState.Ready : remote is not null ? PackageState.NotInstalled : PackageState.Unavailable;
        _states[id] = state;
        return new(local is not null, update, local?.Version, remote?.Version, remote?.Size > 0 ? remote.Size : null,
            local?.Size, state, local?.Source == PackageSource.Cached, remote is not null, remote?.Uri);
    }

    public Task<RemotePackageStatus> CheckForUpdatesAsync(PackageId id, CancellationToken cancellationToken = default) => GetStatusAsync(id, true, cancellationToken);
    public async Task DownloadAsync(PackageId id, IProgress<PackageDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var shared = _downloads.GetOrAdd(id, key =>
        {
            var operation = new SharedDownload(token => DownloadCoreAsync(GetDefinition(key), progress, token));
            _ = operation.Task.ContinueWith(_ => _downloads.TryRemove(new KeyValuePair<PackageId, SharedDownload>(key, operation)),
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return operation;
        });
        shared.AddWaiter();
        try { await shared.Task.WaitAsync(cancellationToken); }
        finally
        {
            shared.RemoveWaiter();
            if (shared.Task.IsCompleted) _downloads.TryRemove(new KeyValuePair<PackageId, SharedDownload>(id, shared));
        }
    }
    public Task DownloadUpdateAsync(PackageId id, IProgress<PackageDownloadProgress>? progress = null, CancellationToken cancellationToken = default) => DownloadAsync(id, progress, cancellationToken);

    public async Task EnsureAvailableAsync(PackageId id, IProgress<PackageDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var definition = GetDefinition(id);
        foreach (var dependency in definition.Dependencies) await EnsureAvailableAsync(dependency, progress, cancellationToken);
        var local = await ResolveLocalAsync(definition, cancellationToken);
        if (definition.Remote is null)
        {
            if (local is null) throw new PackageUnavailableException($"Package '{definition.Name}' is neither bundled nor remotely configured.");
            _activate(id, local.Path); return;
        }
        if (local is not null && definition.Remote.Update is PackageUpdatePolicy.Manual or PackageUpdatePolicy.Check)
        { _activate(id, local.Path); return; }
        try
        {
            var remote = await ResolveRemoteAsync(definition, refresh: definition.Remote.Update == PackageUpdatePolicy.Always, cancellationToken);
            if (local is null || remote.Version > local.Version) await DownloadAsync(id, progress, cancellationToken);
            else _activate(id, local.Path);
        }
        catch (Exception) when (local is not null)
        { _activate(id, local.Path); }
    }

    public Task<PackageCacheInfo> GetCacheInfoAsync(PackageId id, CancellationToken cancellationToken = default) =>
        _cache.GetInfoAsync(id, GetDefinition(id).Remote?.Cache ?? PackageCacheMode.Persistent, cancellationToken);
    public Task RemoveCachedAsync(PackageId id, CancellationToken cancellationToken = default) =>
        _cache.RemoveAsync(id, GetDefinition(id).Remote?.Cache ?? PackageCacheMode.Persistent, cancellationToken);
    internal Task CleanupAsync(PackageId id) => _cache.RemoveSupersededAsync(id,
        GetDefinition(id).Remote?.Cache ?? PackageCacheMode.Persistent);
    public Task RefreshAsync(CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); _manifests.Clear(); return Task.CompletedTask; }

    private async Task<CachedPackage> DownloadCoreAsync(AssetPackageManifestEntry definition,
        IProgress<PackageDownloadProgress>? progress, CancellationToken cancellationToken)
    {
        var remote = await ResolveRemoteAsync(definition, refresh: true, cancellationToken);
        if (definition.Remote?.Integrity == PackageIntegrityMode.Sha256 && !IsSha256(remote.Sha256))
            throw new PackageIntegrityException($"Remote metadata for '{definition.Name}' does not contain a valid SHA-256 hash.");
        _states[definition.Id] = PackageState.Downloading;
        var paths = _cache.PrepareDownload(definition.Id, remote.Version, definition.Remote!.Cache, remote.Size, remote.Sha256, remote.Uri);
        try
        {
            var existing = File.Exists(paths.Partial) ? new FileInfo(paths.Partial).Length : 0;
            Logger.Info($"[Packages] {(existing > 0 ? $"Resuming {definition.Name} at {existing} bytes" : $"Downloading {definition.Name} {remote.Version}")}");
            await using (var output = new FileStream(paths.Partial, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 128 * 1024, useAsync: true))
                await _transport.DownloadAsync(remote.Uri, output, existing, progress, cancellationToken);
            _states[definition.Id] = PackageState.Verifying;
            var info = new FileInfo(paths.Partial);
            if (remote.Size > 0 && info.Length != remote.Size) throw new PackageIntegrityException($"Package '{definition.Name}' size mismatch: expected {remote.Size}, got {info.Length}.");
            await using (var input = File.OpenRead(paths.Partial))
            {
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken)).ToLowerInvariant();
                if (IsSha256(remote.Sha256) && !string.Equals(actual, remote.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new PackageIntegrityException($"SHA-256 mismatch for package '{definition.Name}'.");
            }
            await using (var reader = await VPackReader.OpenAsync(new FileStream(paths.Partial, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true), cancellationToken))
            {
                if (reader.Package != definition.Id) throw new PackageIntegrityException($"Downloaded VPack identity mismatch for '{definition.Name}'.");
                if (reader.Platform != _platform) throw new PackageIntegrityException($"Downloaded VPack platform mismatch for '{definition.Name}'.");
            }
            var cached = await _cache.CommitAsync(definition.Id, remote.Version, definition.Remote.Cache, paths, info.Length, remote.Sha256, cancellationToken);
            _activate(definition.Id, cached.Path); _states[definition.Id] = PackageState.Ready;
            Logger.Info($"[Packages] Activated {definition.Name} {remote.Version}");
            return cached;
        }
        catch (PackageIntegrityException)
        {
            if (File.Exists(paths.Partial)) File.Delete(paths.Partial);
            if (File.Exists(paths.ResumeMetadata)) File.Delete(paths.ResumeMetadata);
            _states[definition.Id] = PackageState.Failed; throw;
        }
        catch (OperationCanceledException) { _states[definition.Id] = PackageState.NotInstalled; throw; }
        catch (RemotePackageException) { _states[definition.Id] = PackageState.Failed; throw; }
        catch (IOException exception) { _states[definition.Id] = PackageState.Failed; throw new PackageCacheException($"Cache I/O failed for package '{definition.Name}'.", exception); }
    }

    private async Task<LocalPackage?> ResolveLocalAsync(AssetPackageManifestEntry definition, CancellationToken cancellationToken)
    {
        var cached = await _cache.GetActiveAsync(definition.Id, definition.Remote?.Cache ?? PackageCacheMode.Persistent, cancellationToken);
        _bundled.TryGetValue(definition.Id, out var bundled);
        if (cached is not null && (bundled is null || cached.Version > bundled.Version)) return new(cached.Version, cached.Path, cached.Size, PackageSource.Cached);
        if (bundled is not null && File.Exists(bundled.Path)) return new(bundled.Version, bundled.Path, bundled.Size, PackageSource.Bundled);
        return cached is null ? null : new(cached.Version, cached.Path, cached.Size, PackageSource.Cached);
    }

    private async Task<RemoteResolvedPackage> ResolveRemoteAsync(AssetPackageManifestEntry definition, bool refresh, CancellationToken cancellationToken)
    {
        var config = definition.Remote ?? throw new PackageUnavailableException($"Package '{definition.Name}' has no remote configuration.");
        if (config.Manifest is { } manifestValue)
        {
            var manifestUri = new Uri(manifestValue, UriKind.Absolute);
            if (refresh) _manifests.TryRemove(manifestUri, out _);
            var task = _manifests.GetOrAdd(manifestUri, uri => FetchManifestAsync(uri));
            var manifest = await task.WaitAsync(cancellationToken);
            if (!manifest.Packages.TryGetValue(definition.Name, out var package)) throw new PackageUnavailableException($"Remote manifest does not list package '{definition.Name}'.");
            if (package.Id != definition.Id) throw new RemoteManifestException($"Remote manifest PackageId mismatch for '{definition.Name}'.");
            var platformName = _platform.ToString().ToLowerInvariant();
            if (!package.Platforms.TryGetValue(platformName, out var entry)) throw new PackageUnavailableException($"Package '{definition.Name}' is unavailable for {platformName}.");
            if (entry.Size < 0 || !IsSha256(entry.Sha256)) throw new RemoteManifestException($"Invalid size or SHA-256 for '{definition.Name}'.");
            if (entry.VPackFormatVersion != VPackFormat.Version) throw new RemoteManifestException($"Package '{definition.Name}' requires unsupported VPack format {entry.VPackFormatVersion}.");
            if (entry.Architecture is not null && !string.Equals(entry.Architecture, _architecture, StringComparison.OrdinalIgnoreCase))
                throw new PackageUnavailableException($"Package '{definition.Name}' is unavailable for architecture {_architecture}.");
            var uri = Uri.TryCreate(entry.Url, UriKind.Absolute, out var absolute) ? absolute : new Uri(manifestUri, entry.Url);
            if (uri.Scheme is not ("http" or "https")) throw new RemoteManifestException($"Package '{definition.Name}' has an invalid URL scheme.");
            return new(package.Version, uri, entry.Size, entry.Sha256);
        }
        var direct = ResolveUrl(config.Url!, definition.Name, definition.Version);
        return new(definition.Version, direct, config.Size ?? 0, config.Sha256 ?? string.Empty);
    }
    private async Task<RemotePackageManifest> FetchManifestAsync(Uri uri)
    {
        Logger.Info($"[Packages] Checking remote manifest {uri}");
        try { return RemotePackageManifest.Parse(await _transport.GetStringAsync(uri)); }
        catch (RemotePackageException) { throw; }
        catch (Exception exception) { throw new RemoteManifestException($"Could not retrieve remote package manifest: {uri}", exception); }
    }
    private Uri ResolveUrl(string template, string name, PackageVersion version)
    {
        var resolved = template.Replace("{name}", name.ToLowerInvariant(), StringComparison.Ordinal)
            .Replace("{version}", version.ToString(), StringComparison.Ordinal)
            .Replace("{platform}", _platform.ToString().ToLowerInvariant(), StringComparison.Ordinal)
            .Replace("{architecture}", _architecture.ToLowerInvariant(), StringComparison.Ordinal);
        if (resolved.Contains('{') || !Uri.TryCreate(resolved, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new RemoteManifestException($"Invalid remote URL template for '{name}': {template}");
        return uri;
    }
    private AssetPackageManifestEntry GetDefinition(PackageId id) => _definitions.TryGetValue(id, out var value) ? value : throw new KeyNotFoundException($"Unknown asset package {id}.");
    private static bool IsSha256(string? value) => value?.Length == 64 && value.All(Uri.IsHexDigit);
    public async ValueTask DisposeAsync() { await _cache.DisposeAsync(); if (_ownsTransport && _transport is IDisposable disposable) disposable.Dispose(); }
    private sealed record BundledPackage(PackageVersion Version, string Path, long Size, string? Sha256);
    private sealed record LocalPackage(PackageVersion Version, string Path, long Size, PackageSource Source);
    private sealed record RemoteResolvedPackage(PackageVersion Version, Uri Uri, long Size, string Sha256);
    private sealed class SharedDownload
    {
        private readonly CancellationTokenSource _cancellation = new(); private int _waiters;
        public SharedDownload(Func<CancellationToken, Task<CachedPackage>> start) => Task = start(_cancellation.Token);
        public Task<CachedPackage> Task { get; }
        public void AddWaiter() => Interlocked.Increment(ref _waiters);
        public void RemoveWaiter() { if (Interlocked.Decrement(ref _waiters) == 0 && !Task.IsCompleted) _cancellation.Cancel(); }
    }
}
