using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vecxy.Assets;

[JsonConverter(typeof(PackageVersionJsonConverter))]
public readonly record struct PackageVersion(int Major, int Minor, int Patch) : IComparable<PackageVersion>
{
    public static PackageVersion Default { get; } = new(1, 0, 0);
    public static PackageVersion Parse(string value) => TryParse(value, out var version)
        ? version : throw new FormatException($"Invalid semantic package version '{value}'. Expected major.minor.patch.");
    public static bool TryParse(string? value, out PackageVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var parts = value.Split('.');
        if (parts.Length != 3 || parts.Any(x => x.Length == 0 || x.Length > 1 && x[0] == '0') ||
            !int.TryParse(parts[0], out var major) || !int.TryParse(parts[1], out var minor) || !int.TryParse(parts[2], out var patch) ||
            major < 0 || minor < 0 || patch < 0) return false;
        version = new PackageVersion(major, minor, patch);
        return true;
    }
    public int CompareTo(PackageVersion other) =>
        Major != other.Major ? Major.CompareTo(other.Major) : Minor != other.Minor ? Minor.CompareTo(other.Minor) : Patch.CompareTo(other.Patch);
    public override string ToString() => $"{Major}.{Minor}.{Patch}";
    public static bool operator <(PackageVersion left, PackageVersion right) => left.CompareTo(right) < 0;
    public static bool operator >(PackageVersion left, PackageVersion right) => left.CompareTo(right) > 0;
    public static bool operator <=(PackageVersion left, PackageVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >=(PackageVersion left, PackageVersion right) => left.CompareTo(right) >= 0;
}

public sealed class PackageVersionJsonConverter : JsonConverter<PackageVersion>
{
    public override PackageVersion Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String ? PackageVersion.Parse(reader.GetString()!) : throw new JsonException("Package version must be a semantic version string.");
    public override void Write(Utf8JsonWriter writer, PackageVersion value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
}

public enum PackageCacheMode { Persistent, Session, None }
public enum PackageUpdatePolicy { Manual, Check, Always }
public enum PackageIntegrityMode { Sha256 }
public enum PackageState { Unavailable, NotInstalled, Checking, Downloading, Verifying, Ready, Loaded, UpdateAvailable, Failed }
public enum PackageSource { Bundled, Cached, Remote }

public sealed class VPackRemoteConfig
{
    public string? Manifest { get; init; }
    public string? Url { get; init; }
    public PackageCacheMode Cache { get; init; } = PackageCacheMode.Persistent;
    public PackageUpdatePolicy Update { get; init; } = PackageUpdatePolicy.Check;
    public PackageIntegrityMode Integrity { get; init; } = PackageIntegrityMode.Sha256;
    public long? Size { get; init; }
    public string? Sha256 { get; init; }
}

public readonly record struct PackageDownloadProgress(
    long DownloadedBytes, long TotalBytes, double Fraction, double BytesPerSecond,
    TimeSpan? EstimatedRemaining, long ResumedBytes);

public sealed record RemotePackageStatus(
    bool IsInstalled, bool IsUpdateAvailable, PackageVersion? LocalVersion,
    PackageVersion? RemoteVersion, long? DownloadSize, long? InstalledSize,
    PackageState State, bool IsCached, bool IsRemoteAvailable, Uri? RemoteUri);

public sealed record PackageCacheInfo(long CachedSize, int Versions);

public sealed class RemotePackageManifest
{
    public const int CurrentVersion = 1;
    public int Version { get; init; }
    public Dictionary<string, RemotePackageManifestPackage> Packages { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public static RemotePackageManifest Parse(string json)
    {
        var manifest = JsonSerializer.Deserialize<RemotePackageManifest>(json, AssetManifest.SerializerOptions)
            ?? throw new RemoteManifestException("Remote package manifest is empty.");
        if (manifest.Version != CurrentVersion) throw new RemoteManifestException($"Unsupported remote package manifest version {manifest.Version}.");
        return manifest;
    }
}

public sealed class RemotePackageManifestPackage
{
    public PackageId Id { get; init; }
    public PackageVersion Version { get; init; } = PackageVersion.Default;
    public Dictionary<string, RemotePackagePlatformEntry> Platforms { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class RemotePackagePlatformEntry
{
    public required string Url { get; init; }
    public long Size { get; init; }
    public required string Sha256 { get; init; }
    public ushort VPackFormatVersion { get; init; } = VPackFormat.Version;
    public string? Architecture { get; init; }
    public string? ETag { get; init; }
    public string? LastModified { get; init; }
}

public class RemotePackageException(string message, Exception? inner = null) : IOException(message, inner);
public sealed class RemoteNetworkException(string message, Exception? inner = null) : RemotePackageException(message, inner);
public sealed class RemoteManifestException(string message, Exception? inner = null) : RemotePackageException(message, inner);
public sealed class PackageUnavailableException(string message, Exception? inner = null) : RemotePackageException(message, inner);
public sealed class PackageDownloadException(string message, Exception? inner = null) : RemotePackageException(message, inner);
public sealed class PackageIntegrityException(string message, Exception? inner = null) : RemotePackageException(message, inner);
public sealed class PackageCacheException(string message, Exception? inner = null) : RemotePackageException(message, inner);

public sealed record RemoteDownloadResult(long TotalBytes, long ResumedBytes, string? ETag, string? LastModified);

public interface IRemotePackageTransport
{
    Task<string> GetStringAsync(Uri uri, CancellationToken cancellationToken = default);
    Task<RemoteDownloadResult> DownloadAsync(Uri uri, Stream destination, long resumeOffset,
        IProgress<PackageDownloadProgress>? progress = null, CancellationToken cancellationToken = default);
}

public sealed class HttpRemotePackageTransport : IRemotePackageTransport, IDisposable
{
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    public HttpRemotePackageTransport(HttpClient? client = null)
    {
        _client = client ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        _ownsClient = client is null;
    }
    public async Task<string> GetStringAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        ValidateUri(uri);
        HttpResponseMessage response;
        try { response = await _client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken); }
        catch (HttpRequestException exception) { throw new RemoteNetworkException($"Remote manifest network request failed: {uri}", exception); }
        using (response)
        {
        if (!response.IsSuccessStatusCode) throw new RemoteManifestException($"Remote manifest request failed: {(int)response.StatusCode} {response.ReasonPhrase}.");
        return await response.Content.ReadAsStringAsync(cancellationToken);
        }
    }
    public async Task<RemoteDownloadResult> DownloadAsync(Uri uri, Stream destination, long resumeOffset,
        IProgress<PackageDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ValidateUri(uri);
        if (!destination.CanWrite || !destination.CanSeek) throw new ArgumentException("Download destination must be writable and seekable.", nameof(destination));
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (resumeOffset > 0) request.Headers.Range = new RangeHeaderValue(resumeOffset, null);
        HttpResponseMessage response;
        try { response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken); }
        catch (HttpRequestException exception) { throw new RemoteNetworkException($"Package network request failed: {uri}", exception); }
        using var ownedResponse = response;
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            return await DownloadAsync(uri, destination, 0, progress, cancellationToken);
        else if (!response.IsSuccessStatusCode) throw new PackageDownloadException($"Package download failed: {(int)response.StatusCode} {response.ReasonPhrase}.");
        var resumed = resumeOffset > 0 && response.StatusCode == HttpStatusCode.PartialContent ? resumeOffset : 0;
        if (resumed == 0) { destination.Position = 0; destination.SetLength(0); }
        else destination.Position = resumed;
        var responseLength = response.Content.Headers.ContentLength;
        var total = response.Content.Headers.ContentRange?.Length ?? (responseLength is null ? 0 : resumed + responseLength.Value);
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[128 * 1024];
        var downloaded = resumed; var watch = Stopwatch.StartNew(); var lastReport = TimeSpan.Zero;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken); if (read == 0) break;
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken); downloaded += read;
            if (watch.Elapsed - lastReport >= TimeSpan.FromMilliseconds(100)) { Report(progress, downloaded, total, resumed, watch.Elapsed); lastReport = watch.Elapsed; }
        }
        await destination.FlushAsync(cancellationToken);
        Report(progress, downloaded, total == 0 ? downloaded : total, resumed, watch.Elapsed, complete: true);
        return new RemoteDownloadResult(total == 0 ? downloaded : total, resumed, response.Headers.ETag?.Tag,
            response.Content.Headers.LastModified?.ToString("R"));
    }
    private static void Report(IProgress<PackageDownloadProgress>? progress, long downloaded, long total, long resumed, TimeSpan elapsed, bool complete = false)
    {
        if (progress is null) return;
        var transferred = Math.Max(0, downloaded - resumed); var seconds = elapsed.TotalSeconds;
        var speed = seconds <= 0 ? 0 : transferred / seconds;
        var fraction = complete ? 1d : total <= 0 ? 0d : Math.Clamp((double)downloaded / total, 0d, 1d);
        TimeSpan? remaining = speed > 0 && total > downloaded ? TimeSpan.FromSeconds((total - downloaded) / speed) : null;
        progress.Report(new(downloaded, total, fraction, speed, remaining, resumed));
    }
    private static void ValidateUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme is not ("http" or "https")) throw new ArgumentException($"Only absolute HTTP(S) package URIs are supported: {uri}.");
    }
    public void Dispose() { if (_ownsClient) _client.Dispose(); }
}
