using Vecxy.Assets;
using Vecxy.Diagnostics;

namespace Vecxy.Rendering;

public sealed class TextureLibrary : IDisposable
{
    private readonly IAssetsManager _assets;
    private readonly GraphicsDevice _device;
    private readonly Dictionary<AssetId, Entry> _textures = [];
    private bool _disposed;

    public TextureLibrary(IAssetsManager assets, GraphicsDevice device)
    {
        _assets = assets;
        _device = device;
        _assets.Unloaded += OnAssetUnloaded;
    }

    public Texture Get(AssetRef<TextureAsset> asset)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_textures.TryGetValue(asset.Id, out var entry))
        {
            if (entry.AssetVersion == asset.Version)
            {
                return entry.Texture;
            }

            try
            {
                var replacement = new Texture(_device, asset.Value);
                var previous = entry.Texture;
                entry.Texture = replacement;
                entry.AssetVersion = asset.Version;
                previous.Dispose();
                return replacement;
            }
            catch (Exception exception)
            {
                entry.AssetVersion = asset.Version;
                Logger.Error(
                    exception,
                    $"Texture reload failed, keeping previous texture: {asset.Metadata.Path}");
                return entry.Texture;
            }
        }

        var texture = new Texture(_device, asset.Value);
        _textures.Add(asset.Id, new Entry(asset.Version, texture));
        return texture;
    }

    private void OnAssetUnloaded(AssetId id, Type assetType)
    {
        if (assetType == typeof(TextureAsset) &&
            _textures.Remove(id, out var entry))
        {
            entry.Texture.Dispose();
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
        foreach (var entry in _textures.Values)
        {
            entry.Texture.Dispose();
        }

        _textures.Clear();
    }

    private sealed class Entry(int assetVersion, Texture texture)
    {
        public int AssetVersion { get; set; } = assetVersion;
        public Texture Texture { get; set; } = texture;
    }
}
