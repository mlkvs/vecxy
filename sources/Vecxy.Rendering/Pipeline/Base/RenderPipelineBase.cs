using OpenTK.Graphics.OpenGL;

namespace Vecxy.Rendering;

public abstract class RenderPipelineBase(IRenderContext context) : IDisposable
{
    private List<IRenderPhase> _phases = new();

    public virtual void Initialize()
    {
        for (int index = 0, count = _phases.Count; index < count; index++)
        {
            var phase = _phases[index];

            phase.Initialize();
        }
    }
    
    public void RegisterRenderable(IRenderable renderable)
    {
        for (int index = 0, count = _phases.Count; index < count; index++)
        {
            var phase = _phases[index];
            
            phase.RegisterRenderable(renderable);
        }
    }

    public void Render()
    {
        context.Clear();
        
        for (int index = 0, count = _phases.Count; index < count; index++)
        {
            var phase = _phases[index];

            OnRenderFrameBegin(phase);
            OnRenderFrame(phase);
            OnRenderFrameEnd(phase);
        }
        
        context.SwapBuffers();
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

    protected void RegisterPhase(IRenderPhase phase)
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