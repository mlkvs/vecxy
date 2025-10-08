namespace Vecxy.Rendering;

public abstract class RenderPhaseBase(IRenderContext context) : IRenderPhase
{
    public abstract RENDER_PHASE_TYPE Type { get; }
    
    protected readonly List<IRenderable> Renderables = [];
    
    public virtual void OnInitialize()
    {
    }

    public virtual void OnRender()
    {
        
    }
    
    public void RegisterRenderable(IRenderable renderable)
    {
        Renderables.Add(renderable);
    }
    
    public virtual void Dispose()
    {
       
    }
}