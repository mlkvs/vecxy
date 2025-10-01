namespace Vecxy.Rendering;

public class Sprite
{
    public Material Material { get; }

    public void SetTexture(Texture texture)
    {
        Material.SetTexture(texture);
    }
}