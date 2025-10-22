namespace Vecxy.Editor;

public class Packet
{
    public IReadOnlyDictionary<string, Asset> Assets => _assets;
    
    private readonly Dictionary<string, Asset> _assets = new();

    public void Pack()
    {
        
    }

    public void AddAsset(Asset asset)
    {
        var id = asset.Meta.Id;

        if (_assets.ContainsKey(id))
        {
            throw new Exception($"Asset with id {id} has already been added");
        }
        
        _assets.Add(asset.Meta.Id, asset);
    }

    public void RemoveAsset(Asset asset)
    {
        _assets.Remove(asset.Meta.Id);
    }
}