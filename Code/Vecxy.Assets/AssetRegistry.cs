namespace Vecxy.Assets;

public sealed class AssetRegistry
{
    private readonly Dictionary<AssetId, AssetMetadata> _assets = [];
    private readonly Dictionary<string, AssetId> _paths = new(StringComparer.Ordinal);

    public IEnumerable<AssetMetadata> Assets => _assets.Values;

    public void Add(AssetMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        if (_assets.ContainsKey(metadata.Id) || _paths.ContainsKey(metadata.Path))
        {
            throw new InvalidOperationException($"Asset is already registered: {metadata.Path}");
        }

        _assets.Add(metadata.Id, metadata);
        _paths.Add(metadata.Path, metadata.Id);
    }

    public bool TryGet(AssetId id, out AssetMetadata? metadata) =>
        _assets.TryGetValue(id, out metadata);

    public bool TryFind(string path, out AssetId id) =>
        _paths.TryGetValue(path, out id);
}
