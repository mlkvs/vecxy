using System.Numerics;
using Vecxy.Kernel;

namespace Vecxy.Rendering;

public class RenderSystem(IRenderWindow window) : IVecxySystem
{
    private RenderPipeline? _pipeline;

    public void OnInitialize()
    {
        _pipeline = new RenderPipeline(window);

        // D2
        var d2Context = new D2RenderContext(window);
        var d2Phase = new D2RenderPhase(d2Context);
        _pipeline.RegisterPhase(d2Phase);
        _pipeline.RegisterPhase(new TestRenderPhase());
        
        // TODO: UI
        
        // TODO: D3
        
        /*var texture = new Texture("Sprites.test.jpg");
        var sprite1 = new Sprite(texture)
        {
            Position = new Vector2(0 - texture.Width / 2f, 0 - texture.Height / 2f),
            Size = new Vector2(texture.Width, texture.Height),
            Color = new Vector4(1f, 1f, 1f, 1f)
        };
        
        var sprite2 = new Sprite(texture)
        {
            Position = sprite1.Position with { X = sprite1.Position.X + texture.Width + 10f },
            Size = new Vector2(texture.Width, texture.Height),
            Color = new Vector4(1f, 1f, 1f, 1f)
        };
        
        _pipeline.RegisterRenderable(sprite1);
        _pipeline.RegisterRenderable(sprite2);*/
    }

    public void OnTick(float deltaTime)
    {
        _pipeline?.Render();
    }

    public void Dispose()
    {
        _pipeline?.Dispose();
    }
}