using Vecxy.Kernel;

namespace Vecxy.Rendering;

public class RenderSystem(IRenderWindow window) : IVecxySystem
{
    private RenderPipeline _pipeline;
    private readonly IRenderWindow _window = window;

    public void OnLoad()
    {
        
    }

    public void OnInitialize()
    {
        _pipeline = new DefaultRenderPipeline(_window);
        
        _pipeline.Initialize();
    }

    public void OnTick()
    {
        _pipeline.Render();
    }

    public void OnUnload()
    {
        
    }

    public void Dispose()
    {
        _pipeline.Dispose();
    }
}