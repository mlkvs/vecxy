using Vecxy.Assets;

namespace Vecxy.Rendering;

internal sealed class ModelImporter : IAssetImporter<Model>
{
    public IReadOnlyCollection<string> Extensions { get; } =
        [".gltf", ".glb"];

    public Model Import(
        AssetMetadata metadata,
        AssetImportContext context)
    {
        return new Model(context.Load<ModelAsset>(metadata.Path));
    }
}

internal sealed class MaterialImporter : IAssetImporter<Material>
{
    public IReadOnlyCollection<string> Extensions { get; } =
        [".material"];

    public Material Import(
        AssetMetadata metadata,
        AssetImportContext context)
    {
        return new Material(context.Load<MaterialAsset>(metadata.Path));
    }
}
