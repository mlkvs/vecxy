using StbImageSharp;

namespace Vecxy.Assets;

public sealed class ImageAsset : Asset, IHotReloadableAsset
{
    public override ASSET_TYPE Type => ASSET_TYPE.TEXTURE;
    public int Width { get; private set; }
    public int Height { get; private set; }
    public byte[] Pixels { get; private set; } = [];

    public override void Load(byte[] data)
    {
        using var stream = new MemoryStream(data, writable: false);
        var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        Width = image.Width;
        Height = image.Height;
        Pixels = image.Data;
    }

    public void OnHotReload(byte[] newData)
    {
        Load(newData);
        NotifyReloaded();
    }
}
