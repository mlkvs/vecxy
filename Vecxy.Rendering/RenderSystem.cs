using Vecxy.Kernel;

namespace Vecxy.Rendering;

public class RenderSystem(IRenderWindow window) : IVecxySystem
{
    private RenderPipelineBase _pipelineBase;
    private D2RenderContext _d2Context;

    private Camera _camera;

    public void OnLoad()
    {
        _camera = new Camera();
    }

    public void OnInitialize()
    {
        _d2Context = new D2RenderContext(window);
        
        _pipelineBase = new DefaultRenderPipelineBase(_d2Context);
        
        _pipelineBase.Initialize();
    }

    public void OnTick()
    {
        _pipelineBase.Render();
    }

    public void OnUnload()
    {
        
    }

    public void Dispose()
    {
        _pipelineBase.Dispose();
    }
}