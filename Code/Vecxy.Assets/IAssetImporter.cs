namespace Vecxy.Assets;

public interface IAssetImporter<T> where T : class
{
    IReadOnlyCollection<string> Extensions { get; }
    T Import(AssetMetadata metadata, AssetImportContext context);
}

internal interface IAssetImporter
{
    Type AssetType { get; }
    IReadOnlyCollection<string> Extensions { get; }
    object Import(AssetMetadata metadata, AssetImportContext context);
}

internal sealed class AssetImporter<T>(IAssetImporter<T> importer) : IAssetImporter
    where T : class
{
    public Type AssetType => typeof(T);
    public IReadOnlyCollection<string> Extensions => importer.Extensions;

    public object Import(AssetMetadata metadata, AssetImportContext context) =>
        importer.Import(metadata, context);
}
