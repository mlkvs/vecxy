using OpenTK.Graphics.OpenGL;

namespace Vecxy.Rendering;

public abstract class RenderPipelineBase(IRenderContext context) : IDisposable
{
    private List<IRenderable> _renderables = new();
    private List<IRenderPhase> _phases = new();

    public virtual void Initialize()
    {
        for (int index = 0, count = _phases.Count; index < count; index++)
        {
            var phase = _phases[index];

            phase.Initialize(context);
        }
    }
    
    public void AddRenderable(IRenderable renderable)
    {
        _renderables.Add(renderable);
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
        phase.OnBegin(context);
    }
    
    protected virtual void OnRenderFrame(IRenderPhase phase)
    {
        phase.OnRender(context);
    }
    
    protected virtual void OnRenderFrameEnd(IRenderPhase phase)
    {
        phase.OnEnd(context);
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