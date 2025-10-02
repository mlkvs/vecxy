namespace Vecxy.Rendering;

public abstract class RenderPhaseLayerBase(IRenderContext context) : IRenderPhaseLayer
{
    public abstract RENDER_PHASE_LAYER_TYPE Type { get; }
    
    public virtual void Initialize()
    {
    }

    public virtual void OnBegin()
    {
    }

    public virtual void OnRender(List<IRenderable> renderables)
    {
        for (int index = 0, count = renderables.Count; index < count; index++)
        {
            var renderable = renderables[index];
            
            renderable.OnRender(context);
        }
    }

    public virtual void OnEnd()
    {
    }

    public virtual void Dispose()
    {
    }
}