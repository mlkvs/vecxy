using Vecxy.Assets;

namespace Vecxy.Prototypes;

public sealed class PrototypeAssetImporter : IAssetImporter<PrototypeAsset>
{
    public IReadOnlyCollection<string> Extensions { get; } = [".prototype"];
    public PrototypeAsset Import(AssetMetadata metadata, AssetImportContext context) => new()
    { Document = PrototypeSerializer.Deserialize(context.ReadAllText(metadata.Path), metadata.Path) };
}
