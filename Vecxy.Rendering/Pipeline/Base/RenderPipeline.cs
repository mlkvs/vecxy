using OpenTK.Graphics.OpenGL;

namespace Vecxy.Rendering;

public class RenderPipeline(IRenderWindow window) : IDisposable
{
    private List<IRenderable> _renderables = new();
    private List<IRenderPhase> _phases = new();

    public virtual void Initialize() { }
    
    public void AddRenderable(IRenderable renderable)
    {
        _renderables.Add(renderable);
    }

    public void Render()
    {
        for (int index = 0, count = _phases.Count; index < count; index++)
        {
            var phase = _phases[index];

            OnRenderFrameBegin(phase);
            OnRenderFrame(phase);
            OnRenderFrameEnd(phase);
        }
        
        window.SwapBuffers();
    }

    protected virtual void OnRenderFrameBegin(IRenderPhase phase)
    {
        phase.OnBegin();
    }
    
    protected virtual void OnRenderFrame(IRenderPhase phase)
    {
        phase.OnRender();
    }
    
    protected virtual void OnRenderFrameEnd(IRenderPhase phase)
    {
        phase.OnEnd();
    }

    protected void RegisterRenderPhase(IRenderPhase phase)
    {
        _phases.Add(phase);
    }

    public void Dispose()
    {
        for (int index = 0, count = _phases.Count; index < count; index++)
        {
            var phase = _phases[index];
            
            phase.Dispose();
        }
        
        _phases.Clear();
    }
}