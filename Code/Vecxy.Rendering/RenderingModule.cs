using Vecxy.Kernel;

namespace Vecxy.Rendering;

public class RenderingModule(IRenderWindow window) : IModule
{
    private RenderPipeline? _pipeline;

    public void OnInitialize()
    {
        _pipeline = new RenderPipeline(window);
        
        _pipeline.RegisterPhase(D2RenderPhase.Create(window));
        _pipeline.RegisterPhase(new TestRenderPhase());
        
    }

    public void OnTick(float deltaTime)
    {
        _pipeline?.Render();
    }

    public void Dispose()
    {
        _pipeline?.Dispose();
        
        GC.SuppressFinalize(this);
    }
}