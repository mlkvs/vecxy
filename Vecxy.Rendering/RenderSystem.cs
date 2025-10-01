using Vecxy.Kernel;

namespace Vecxy.Rendering;

public class RenderSystem(IRenderWindow window) : IVecxySystem
{
    private RenderPipeline _pipeline;

    private Camera _camera;

    public void OnLoad()
    {
        _camera = new Camera();
    }

    public void OnInitialize()
    {
        _pipeline = new DefaultRenderPipeline(window);
        
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