namespace Vecxy.Platforms;

public sealed record PlatformContext(
    PlatformKind Platform,
    string AssetsDirectory,
    Func<string, Stream>? OpenBootstrapAsset = null)
{
    public Stream OpenAsset(string relativePath) => OpenBootstrapAsset is not null
        ? OpenBootstrapAsset(relativePath)
        : File.OpenRead(Path.Combine(AssetsDirectory, relativePath));
}
