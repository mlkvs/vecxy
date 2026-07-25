namespace Vecxy.Assets;

public sealed class TextAsset
{
    public required string Content { get; init; }
}

public sealed class TextAssetImporter : IAssetImporter<TextAsset>
{
    public IReadOnlyCollection<string> Extensions { get; } =
        [".txt", ".vert", ".frag"];

    public TextAsset Import(AssetMetadata metadata, AssetImportContext context) =>
        new()
        {
            Content = context.ReadAllText(metadata.Path)
        };
}
