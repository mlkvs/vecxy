using Vecxy.Assets;

namespace Vecxy.Rendering;

public sealed class MaterialLibrary : IDisposable
{
    private readonly IAssetsManager _assets;
    private readonly ShaderLibrary _shaders;
    private readonly TextureLibrary _textures;
    private readonly Dictionary<AssetId, Material> _materials = [];
    private bool _disposed;

    public MaterialLibrary(
        IAssetsManager assets,
        ShaderLibrary shaders,
        TextureLibrary textures)
    {
        _assets = assets;
        _shaders = shaders;
        _textures = textures;
        _assets.Unloaded += OnAssetUnloaded;
    }

    public Material Get(AssetRef<MaterialAsset> asset)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_materials.TryGetValue(asset.Id, out var material))
        {
            return material;
        }

        material = new Material(_shaders, _textures);
        _materials.Add(asset.Id, material);
        return material;
    }

    public void Clear() => _materials.Clear();

    private void OnAssetUnloaded(AssetId id, Type assetType)
    {
        if (assetType == typeof(MaterialAsset))
        {
            _materials.Remove(id);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _assets.Unloaded -= OnAssetUnloaded;
        _materials.Clear();
    }
}
