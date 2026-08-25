using System.Security.Cryptography;
using System.Text.Json;

namespace Vecxy.Assets;

public sealed class PackageCache : IAsyncDisposable
{
    private readonly string _persistentRoot;
    private readonly string _sessionRoot;
    private readonly string _noneRoot;

    public PackageCache(string applicationId, string? persistentRoot = null)
    {
        var safeId = SafeSegment(applicationId);
        _persistentRoot = Path.GetFullPath(persistentRoot ?? DefaultPersistentRoot(safeId));
        var session = Path.Combine(Path.GetTempPath(), "Vecxy", safeId, Environment.ProcessId.ToString(), Guid.NewGuid().ToString("N"));
        _sessionRoot = Path.Combine(session, "Session"); _noneRoot = Path.Combine(session, "Temporary");
    }

    public async Task<CachedPackage?> GetActiveAsync(PackageId id, PackageCacheMode mode, CancellationToken cancellationToken = default)
    {
        var root = Root(mode); var pointer = Path.Combine(root, id.ToString(), "active.json");
        if (!File.Exists(pointer)) return null;
        try
        {
            await using var stream = File.OpenRead(pointer);
            var metadata = await JsonSerializer.DeserializeAsync<PackageCacheMetadata>(stream, AssetManifest.SerializerOptions, cancellationToken);
            if (metadata is null || metadata.PackageId != id || !PackageVersion.TryParse(metadata.Version, out var version)) return null;
            var file = GetFinalPath(root, id, version);
            if (!File.Exists(file) || new FileInfo(file).Length != metadata.Size || !metadata.Verified) return null;
            return new CachedPackage(id, version, file, metadata.Size, metadata.Sha256);
        }
        catch (Exception exception) when (exception is IOException or JsonException) { return null; }
    }

    internal CacheDownloadPaths PrepareDownload(PackageId id, PackageVersion version, PackageCacheMode mode,
        long expectedSize, string expectedHash, Uri uri)
    {
        var root = Root(mode); var directory = Path.GetDirectoryName(GetFinalPath(root, id, version))!;
        Directory.CreateDirectory(directory);
        var partial = Path.Combine(directory, "package.vpack.download");
        var resume = Path.Combine(directory, "package.download.json");
        if (File.Exists(resume))
        {
            try
            {
                var info = JsonSerializer.Deserialize<PartialDownloadMetadata>(File.ReadAllText(resume), AssetManifest.SerializerOptions);
                if (info?.Size != expectedSize || !string.Equals(info.Sha256, expectedHash, StringComparison.OrdinalIgnoreCase) || info.Url != uri.AbsoluteUri)
                { File.Delete(partial); File.Delete(resume); }
            }
            catch { File.Delete(partial); File.Delete(resume); }
        }
        File.WriteAllText(resume, JsonSerializer.Serialize(new PartialDownloadMetadata(expectedSize, expectedHash, uri.AbsoluteUri), AssetManifest.SerializerOptions));
        return new(partial, resume, GetFinalPath(root, id, version), Path.Combine(root, id.ToString(), "active.json"));
    }

    internal async Task<CachedPackage> CommitAsync(PackageId id, PackageVersion version, PackageCacheMode mode,
        CacheDownloadPaths paths, long size, string sha256, CancellationToken cancellationToken)
    {
        var finalDirectory = Path.GetDirectoryName(paths.Final)!; Directory.CreateDirectory(finalDirectory);
        File.Move(paths.Partial, paths.Final, overwrite: true);
        if (File.Exists(paths.ResumeMetadata)) File.Delete(paths.ResumeMetadata);
        var metadata = new PackageCacheMetadata(id, version.ToString(), size, sha256, true);
        var pointerTemp = paths.ActiveMetadata + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(paths.ActiveMetadata)!);
        await File.WriteAllTextAsync(pointerTemp, JsonSerializer.Serialize(metadata, AssetManifest.SerializerOptions), cancellationToken);
        File.Move(pointerTemp, paths.ActiveMetadata, overwrite: true);
        foreach (var directory in Directory.EnumerateDirectories(Path.Combine(Root(mode), id.ToString())))
            if (!string.Equals(Path.GetFullPath(directory), finalDirectory, StringComparison.OrdinalIgnoreCase))
                try { Directory.Delete(directory, recursive: true); } catch (IOException) { /* Active VPack reader still owns the previous version. */ }
        return new(id, version, paths.Final, size, sha256);
    }

    internal async Task RemoveSupersededAsync(PackageId id, PackageCacheMode mode, CancellationToken cancellationToken = default)
    {
        var active = await GetActiveAsync(id, mode, cancellationToken); if (active is null) return;
        var packageRoot = Path.Combine(Root(mode), id.ToString()); var keep = Path.GetDirectoryName(active.Path)!;
        foreach (var directory in Directory.EnumerateDirectories(packageRoot))
            if (!string.Equals(Path.GetFullPath(directory), keep, StringComparison.OrdinalIgnoreCase))
                try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
    }

    public Task RemoveAsync(PackageId id, PackageCacheMode mode = PackageCacheMode.Persistent, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Path.Combine(Root(mode), id.ToString()); if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        return Task.CompletedTask;
    }
    public Task ClearAsync(PackageCacheMode mode = PackageCacheMode.Persistent, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); var root = Root(mode);
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true); return Task.CompletedTask;
    }
    public Task<PackageCacheInfo> GetInfoAsync(PackageId id, PackageCacheMode mode = PackageCacheMode.Persistent, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); var path = Path.Combine(Root(mode), id.ToString());
        if (!Directory.Exists(path)) return Task.FromResult(new PackageCacheInfo(0, 0));
        long size = 0; var versions = 0;
        foreach (var file in Directory.EnumerateFiles(path, "package.vpack", SearchOption.AllDirectories)) { size += new FileInfo(file).Length; versions++; }
        return Task.FromResult(new PackageCacheInfo(size, versions));
    }
    private string Root(PackageCacheMode mode) => mode switch { PackageCacheMode.Persistent => _persistentRoot, PackageCacheMode.Session => _sessionRoot, _ => _noneRoot };
    private static string GetFinalPath(string root, PackageId id, PackageVersion version) => Path.Combine(root, id.ToString(), version.ToString(), "package.vpack");
    private static string SafeSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Application identity is required.", nameof(value));
        var safe = string.Concat(value.Select(x => char.IsLetterOrDigit(x) || x is '.' or '-' or '_' ? x : '_'));
        if (safe is "." or ".." || safe.Length == 0) throw new ArgumentException("Invalid application identity.", nameof(value));
        return safe;
    }
    private static string DefaultPersistentRoot(string applicationId)
    {
        string basePath;
        if (OperatingSystem.IsLinux() && !OperatingSystem.IsAndroid())
            basePath = Environment.GetEnvironmentVariable("XDG_CACHE_HOME") ??
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
        else
            basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(basePath, applicationId, "Packages");
    }
    public ValueTask DisposeAsync()
    {
        var sessionParent = Directory.GetParent(_sessionRoot)?.FullName;
        if (sessionParent is not null && Directory.Exists(sessionParent)) Directory.Delete(sessionParent, recursive: true);
        return ValueTask.CompletedTask;
    }
    private sealed record PackageCacheMetadata(PackageId PackageId, string Version, long Size, string Sha256, bool Verified);
    private sealed record PartialDownloadMetadata(long Size, string Sha256, string Url);
}

public sealed record CachedPackage(PackageId Id, PackageVersion Version, string Path, long Size, string Sha256);
internal sealed record CacheDownloadPaths(string Partial, string ResumeMetadata, string Final, string ActiveMetadata);
