namespace Vecxy.Rendering;

public abstract class RenderPhaseBase : IRenderPhase
{
    public abstract RENDER_PHASE_TYPE Type { get; }
    
    private readonly List<IRenderPhaseLayer> _phaseLayers = [];
    
    public virtual void Initialize(IRenderContext ctx)
    {
        for (int index = 0, count = _phaseLayers.Count; index < count; index++)
        {
            var layer = _phaseLayers[index];

            layer.Initialize(ctx);
        }
    }

    public virtual void OnBegin(IRenderContext ctx)
    {
        for (int index = 0, count = _phaseLayers.Count; index < count; index++)
        {
            var layer = _phaseLayers[index];

            layer.OnBegin(ctx);
        }
    }

    public virtual void OnRender(IRenderContext ctx)
    {
        for (int index = 0, count = _phaseLayers.Count; index < count; index++)
        {
            var layer = _phaseLayers[index];

            layer.OnRender(ctx);
        }
    }

    public virtual void OnEnd(IRenderContext ctx)
    {
        for (int index = 0, count = _phaseLayers.Count; index < count; index++)
        {
            var layer = _phaseLayers[index];

            layer.OnEnd(ctx);
        }
    }
    
    public virtual void Dispose()
    {
        for (int index = 0, count = _phaseLayers.Count; index < count; index++)
        {
            var layer = _phaseLayers[index];

            layer.Dispose();
        }
        
        _phaseLayers.Clear();
    }

    public void RegisterLayer(IRenderPhaseLayer layer)
    {
        _phaseLayers.Add(layer);
    }
}