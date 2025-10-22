namespace Vecxy.Editor;

public enum ASSET_TYPE : byte
{
    UNKNOWN = 0,
    
    TEXTURE = 1,
    MODEL = 2,
    
    JSON = 3,
}

public class AssetMeta
{
    public string Id { get; }
    
}

public class Asset
{
    public ASSET_TYPE Type { get; }
    public AssetMeta Meta { get; }
}

public class TextureAsset : Asset
{
    
}

public class PNGAsset : TextureAsset
{
    
}