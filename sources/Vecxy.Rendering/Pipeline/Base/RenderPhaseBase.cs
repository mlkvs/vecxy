namespace Vecxy.Rendering;

public abstract class RenderPhaseBase(IRenderContext context) : IRenderPhase
{
    public abstract RENDER_PHASE_TYPE Type { get; }
    
    private readonly List<IRenderPhaseLayer> _phaseLayers = [];
    private readonly List<IRenderable> _renderables = [];
    
    public virtual void Initialize()
    {
        for (int index = 0, count = _phaseLayers.Count; index < count; index++)
        {
            var layer = _phaseLayers[index];

            layer.Initialize();
        }
    }

    public virtual void OnBegin()
    {
        for (int index = 0, count = _phaseLayers.Count; index < count; index++)
        {
            var layer = _phaseLayers[index];

            layer.OnBegin();
        }
    }

    public virtual void OnRender()
    {
        for (int index = 0, count = _phaseLayers.Count; index < count; index++)
        {
            var layer = _phaseLayers[index];

            layer.OnRender(_renderables);
        }
    }

    public virtual void OnEnd()
    {
        for (int index = 0, count = _phaseLayers.Count; index < count; index++)
        {
            var layer = _phaseLayers[index];

            layer.OnEnd();
        }
    }
    
    public void RegisterRenderable(IRenderable renderable)
    {
        _renderables.Add(renderable);
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