namespace Vecxy.Rendering;

public abstract class RenderPhaseLayerBase : IRenderPhaseLayer
{
    public abstract RENDER_PHASE_LAYER_TYPE Type { get; }
    
    public virtual void Initialize(IRenderContext ctx)
    {
    }

    public virtual void OnBegin(IRenderContext ctx)
    {
    }

    public virtual void OnRender(IRenderContext ctx)
    {
    }

    public virtual void OnEnd(IRenderContext ctx)
    {
    }
    
    public virtual void Dispose()
    {
    }
}