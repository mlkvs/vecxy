using System.Xml.Linq;
using Vecxy.Assets;

namespace Vecxy.UI;

public sealed class UiDocumentAsset
{
    public required string Source { get; init; }
    public required string Path { get; init; }
}

public sealed class UiDocumentAssetImporter : IAssetImporter<UiDocumentAsset>
{
    public IReadOnlyCollection<string> Extensions { get; } = [".xml"];

    public UiDocumentAsset Import(
        AssetMetadata metadata,
        AssetImportContext context)
    {
        var source = context.ReadAllText(metadata.Path);

        try
        {
            _ = XDocument.Parse(
                source,
                LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
        }
        catch (Exception exception)
        {
            throw new InvalidDataException(
                $"UI document contains invalid XML: {metadata.Path}",
                exception);
        }

        return new UiDocumentAsset
        {
            Source = source,
            Path = metadata.Path
        };
    }
}

public sealed class UiStyleSheetAsset
{
    public required string Source { get; init; }
    public required string Path { get; init; }
}

public sealed class UiStyleSheetAssetImporter : IAssetImporter<UiStyleSheetAsset>
{
    public IReadOnlyCollection<string> Extensions { get; } = [".css"];

    public UiStyleSheetAsset Import(
        AssetMetadata metadata,
        AssetImportContext context) =>
        new()
        {
            Source = context.ReadAllText(metadata.Path),
            Path = metadata.Path
        };
}
