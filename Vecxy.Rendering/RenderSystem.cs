using System.Numerics;
using Vecxy.Kernel;

namespace Vecxy.Rendering;

public class RenderSystem(IRenderWindow window) : IVecxySystem
{
    private RenderPipelineBase? _pipeline;
    private D2RenderContext? _d2Context;

    private Camera? _camera;

    public void OnLoad()
    {
        _camera = new Camera();
    }

    public void OnInitialize()
    {
        _d2Context = new D2RenderContext(window);
        
        _pipeline = new DefaultRenderPipeline(_d2Context);
        
        _pipeline.Initialize();
        
        var texture = new Texture("Sprites.test.jpg");
        var sprite = new Sprite(texture)
        {
            Position = new Vector2(0 - 200f / 2, 0 - 200f / 2),
            Size = new Vector2(200, 200),
            Color = new Vector4(1f, 1f, 1f, 1f)
        };
        
        _pipeline.RegisterRenderable(sprite);
    }

    public void OnTick()
    {
        _pipeline?.Render();
    }

    public void OnUnload()
    {
        
    }

    public void Dispose()
    {
        _pipeline?.Dispose();
    }
}