namespace Vecxy.Assets;

public sealed class AssetMetadata
{
    public required AssetId Id { get; init; }
    public required Type AssetType { get; init; }
    public required string Path { get; init; }
    public PackageId Package { get; init; } = PackageId.Game;
    public bool IsLoaded { get; internal set; }
}
