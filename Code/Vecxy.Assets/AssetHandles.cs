namespace Vecxy.Assets;

public interface IAssetHandle
{
    AssetId Id { get; }
}

public readonly record struct AssetHandle(AssetId Id) : IAssetHandle { public AssetHandle(Guid id) : this(new AssetId(id)) { } }
public readonly record struct ConfigHandle(AssetId Id) : IAssetHandle { public ConfigHandle(Guid id) : this(new AssetId(id)) { } }
public readonly record struct TextHandle(AssetId Id) : IAssetHandle { public TextHandle(Guid id) : this(new AssetId(id)) { } }
public readonly record struct ShaderHandle(AssetId Id) : IAssetHandle { public ShaderHandle(Guid id) : this(new AssetId(id)) { } }
public readonly record struct InputHandle(AssetId Id) : IAssetHandle { public InputHandle(Guid id) : this(new AssetId(id)) { } }

public readonly record struct TextureHandle(AssetId Id) : IAssetHandle
{
    public TextureHandle(Guid id) : this(new AssetId(id)) { }
}

public readonly record struct ModelHandle(AssetId Id) : IAssetHandle
{
    public ModelHandle(Guid id) : this(new AssetId(id)) { }
}

public readonly record struct SoundHandle(AssetId Id) : IAssetHandle
{
    public SoundHandle(Guid id) : this(new AssetId(id)) { }
}

public readonly record struct MaterialHandle(AssetId Id) : IAssetHandle
{
    public MaterialHandle(Guid id) : this(new AssetId(id)) { }
}
