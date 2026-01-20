using System.Numerics;

namespace Vecxy.Rendering;

public class Sprite(Texture texture) : IRenderable
{
    public RENDER_PHASE_TYPE RenderPhase => RENDER_PHASE_TYPE.D2;
    public Vector2 Position { get; set; } = Vector2.Zero;
    public Vector2 Size { get; set; } = Vector2.One;
    public Vector4 Color { get; set; } = Vector4.One; // RGBA
    public Texture Texture { get; set; } = texture;

    public void OnRender(IRenderContext context)
    {
        if (context is not ID2RenderContext d2Context)
        {
            throw new InvalidOperationException("Context must be ID2RenderContext");
        }

        d2Context.DrawSprite(this);
    }
}