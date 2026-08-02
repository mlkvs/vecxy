namespace Vecxy.Platforms;

public sealed record PlatformContext(
    PlatformKind Platform,
    string AssetsDirectory);
