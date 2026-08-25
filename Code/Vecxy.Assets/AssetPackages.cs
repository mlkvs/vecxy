using System.Text.Json;

namespace Vecxy.Assets;

public sealed class VPackBuildManifest
{
    public ushort FormatVersion { get; init; }
    public VPackPlatform Platform { get; init; }
    public List<VPackBuildManifestEntry> Packages { get; init; } = [];
}

public sealed class VPackBuildManifestEntry
{
    public PackageId Id { get; init; }
    public required string Name { get; init; }
    public required string File { get; init; }
    public PackageVersion Version { get; init; } = PackageVersion.Default;
    public long Size { get; init; }
    public string? Sha256 { get; init; }
}

public sealed class AssetPackageNotLoadedException : InvalidOperationException
{
    public AssetPackageNotLoadedException(string packageName, AssetId asset) :
        base($"Asset package '{packageName}' is not loaded. Load it before materializing asset {asset}.") { }
}

public sealed class AssetPackage
{
    private readonly AssetPackageManager _manager;
    internal AssetPackage(AssetPackageManager manager, AssetPackageManifestEntry definition)
    { _manager = manager; Id = definition.Id; Name = definition.Name; LoadMode = definition.Load; Dependencies = definition.Dependencies; }
    public PackageId Id { get; }
    public string Name { get; }
    public PackageLoadMode LoadMode { get; }
    public IReadOnlyList<PackageId> Dependencies { get; }
    public bool IsLoaded => _manager.IsLoaded(Id);
    public ValueTask<AssetPackageLease> LoadAsync(CancellationToken cancellationToken = default) => _manager.AcquireAsync(Id, cancellationToken);
    public Task<RemotePackageStatus> GetRemoteStatusAsync(CancellationToken cancellationToken = default) => _manager.GetRemoteStatusAsync(Id, cancellationToken);
    public Task<RemotePackageStatus> CheckForUpdatesAsync(CancellationToken cancellationToken = default) => _manager.CheckForUpdatesAsync(Id, cancellationToken);
    public Task DownloadAsync(IProgress<PackageDownloadProgress>? progress = null, CancellationToken cancellationToken = default) => _manager.DownloadAsync(Id, progress, cancellationToken);
    public Task DownloadUpdateAsync(IProgress<PackageDownloadProgress>? progress = null, CancellationToken cancellationToken = default) => _manager.DownloadUpdateAsync(Id, progress, cancellationToken);
    public async ValueTask<AssetPackageLease> EnsureLoadedAsync(IProgress<PackageDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    { await _manager.EnsureAvailableAsync(Id, progress, cancellationToken); return await _manager.AcquireAsync(Id, cancellationToken); }
    public Task<PackageCacheInfo> GetCacheInfoAsync(CancellationToken cancellationToken = default) => _manager.GetCacheInfoAsync(Id, cancellationToken);
    public Task RemoveCachedAsync(CancellationToken cancellationToken = default) => _manager.RemoveCachedAsync(Id, cancellationToken);
    public bool Unload() => _manager.TryUnload(Id);
}

public sealed class AssetPackageLease : IDisposable, IAsyncDisposable
{
    private AssetPackageManager? _manager;
    public AssetPackage Package { get; }
    internal AssetPackageLease(AssetPackageManager manager, AssetPackage package) { _manager = manager; Package = package; }
    public void Dispose() => Interlocked.Exchange(ref _manager, null)?.Release(Package.Id);
    public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
}

public static class AssetPackages
{
    private static AssetPackageManager? _current;
    internal static void Bind(AssetPackageManager manager) => _current = manager;
    internal static void Unbind(AssetPackageManager manager) { if (ReferenceEquals(_current, manager)) _current = null; }
    public static AssetPackage Get(PackageId id) => (_current ?? throw new InvalidOperationException("Asset packages are not initialized.")).Get(id);
}

internal sealed class AssetPackageManager : IAsyncDisposable
{
    private readonly string _directory;
    private readonly Dictionary<PackageId, State> _states = [];
    private readonly Func<PackageId, bool> _hasLiveAssets;
    private RemotePackageManager? _remote;
    public AssetPackageManager(string directory, IEnumerable<AssetPackageManifestEntry> definitions, Func<PackageId, bool> hasLiveAssets)
    {
        _directory = directory; _hasLiveAssets = hasLiveAssets;
        foreach (var definition in definitions) { var package = new AssetPackage(this, definition); _states.Add(definition.Id, new State(package)); }
    }
    public AssetPackage Get(PackageId id) => _states.TryGetValue(id, out var state) ? state.Package : throw new KeyNotFoundException($"Unknown asset package {id}.");
    public bool IsLoaded(PackageId id) => _states.TryGetValue(id, out var state) && state.Reader is not null;
    internal void SetRemote(RemotePackageManager remote) => _remote = remote;

    public async ValueTask<AssetPackageLease> AcquireAsync(PackageId id, CancellationToken cancellationToken)
    {
        var state = GetState(id);
        await state.Gate.WaitAsync(cancellationToken);
        try
        {
            if (state.References == 0)
            {
                var dependencyLeases = new List<AssetPackageLease>();
                try
                {
                    foreach (var dependency in state.Package.Dependencies) dependencyLeases.Add(await AcquireAsync(dependency, cancellationToken));
                    var file = state.File ?? state.Package.Name.ToLowerInvariant() + ".vpack";
                    var path = Path.IsPathFullyQualified(file) ? file : Path.Combine(_directory, file);
                    var reader = await VPackReader.OpenAsync(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, useAsync: true), cancellationToken);
                    if (reader.Package != id) { await reader.DisposeAsync(); throw new InvalidDataException($"VPack package ID mismatch in {path}."); }
                    state.Reader = reader; state.DependencyLeases = dependencyLeases;
                }
                catch { foreach (var lease in dependencyLeases) await lease.DisposeAsync(); throw; }
            }
            state.References++;
        }
        finally { state.Gate.Release(); }
        return new AssetPackageLease(this, state.Package);
    }

    public void Release(PackageId id)
    {
        var state = GetState(id); state.Gate.Wait();
        try { if (state.References <= 0) throw new InvalidOperationException($"Package '{state.Package.Name}' has no active leases."); state.References--; TryUnloadCore(state); }
        finally { state.Gate.Release(); }
    }
    public bool TryUnload(PackageId id) { var state=GetState(id); state.Gate.Wait(); try{return TryUnloadCore(state);}finally{state.Gate.Release();} }
    public async ValueTask<ReadOnlyMemory<byte>> ReadAsync(PackageId package, AssetId asset, CancellationToken cancellationToken = default)
    { var state=GetState(package); var reader=state.Reader ?? throw new AssetPackageNotLoadedException(state.Package.Name, asset); return await reader.ReadAssetAsync(asset,cancellationToken); }
    public void SetFiles(VPackBuildManifest manifest)
    { if(manifest.FormatVersion!=VPackFormat.Version)throw new NotSupportedException($"Unsupported package manifest version {manifest.FormatVersion}."); foreach(var entry in manifest.Packages) GetState(entry.Id).File=entry.File; _remote?.SetBundled(manifest, _directory); }
    internal void SetFile(PackageId id, string path)
    { var state=GetState(id); var full=Path.GetFullPath(path); if(state.Reader is not null){state.PendingFile=full;return;} state.File=full; }
    internal async Task<RemotePackageStatus> GetRemoteStatusAsync(PackageId id,CancellationToken token)
    { var status=await GetRemote().GetStatusAsync(id,false,token); return IsLoaded(id)?status with { State=PackageState.Loaded }:status; }
    internal async Task<RemotePackageStatus> CheckForUpdatesAsync(PackageId id,CancellationToken token)
    { var status=await GetRemote().CheckForUpdatesAsync(id,token); return IsLoaded(id)?status with { State=PackageState.Loaded }:status; }
    internal Task DownloadAsync(PackageId id,IProgress<PackageDownloadProgress>? progress,CancellationToken token)=>GetRemote().DownloadAsync(id,progress,token);
    internal Task DownloadUpdateAsync(PackageId id,IProgress<PackageDownloadProgress>? progress,CancellationToken token)=>GetRemote().DownloadUpdateAsync(id,progress,token);
    internal Task EnsureAvailableAsync(PackageId id,IProgress<PackageDownloadProgress>? progress,CancellationToken token)=>GetRemote().EnsureAvailableAsync(id,progress,token);
    internal Task<PackageCacheInfo> GetCacheInfoAsync(PackageId id,CancellationToken token)=>GetRemote().GetCacheInfoAsync(id,token);
    internal Task RemoveCachedAsync(PackageId id,CancellationToken token)=>GetRemote().RemoveCachedAsync(id,token);
    private RemotePackageManager GetRemote()=>_remote??throw new InvalidOperationException("Remote package services are not initialized.");

    private bool TryUnloadCore(State state)
    {
        if (state.References != 0 || state.Reader is null || _hasLiveAssets(state.Package.Id)) return false;
        state.Reader.DisposeAsync().AsTask().GetAwaiter().GetResult(); state.Reader=null;
        if(state.PendingFile is not null){state.File=state.PendingFile;state.PendingFile=null;}
        _remote?.CleanupAsync(state.Package.Id).GetAwaiter().GetResult();
        foreach(var lease in state.DependencyLeases) lease.Dispose(); state.DependencyLeases=[]; return true;
    }
    private State GetState(PackageId id)=>_states.TryGetValue(id,out var state)?state:throw new KeyNotFoundException($"Unknown asset package {id}.");
    public async ValueTask DisposeAsync() { foreach(var state in _states.Values){if(state.Reader is not null)await state.Reader.DisposeAsync();state.Gate.Dispose();} if(_remote is not null)await _remote.DisposeAsync(); }
    internal sealed class State(AssetPackage package) { public AssetPackage Package { get; }=package; public SemaphoreSlim Gate { get; }=new(1,1); public int References; public string? File; public string? PendingFile; public VPackReader? Reader; public List<AssetPackageLease> DependencyLeases=[]; }
}
