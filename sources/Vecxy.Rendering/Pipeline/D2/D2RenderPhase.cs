namespace Vecxy.Rendering;

public class D2RenderPhase(ID2RenderContext context) : IRenderPhase
{
    public RENDER_PHASE_TYPE Type => RENDER_PHASE_TYPE.D2;

    private readonly List<IRenderable> _renderables = [];
    
    public static D2RenderPhase Create(IRenderWindow window)
    {
        var d2Context = new D2RenderContext(window);
        
        var d2Phase = new D2RenderPhase(d2Context);

        return d2Phase;
    }

    public void OnInitialize()
    {
        
    }

    public void OnRender()
    {
        context.BeginBatch();
        
        foreach (var renderable in _renderables)
        {
            renderable.OnRender(context);
        }
        
        context.EndBatch();
    }

    public void RegisterRenderable(IRenderable renderable)
    {
        _renderables.Add(renderable);
    }

    void IDisposable.Dispose()
    {
        _renderables.Clear();
        
        GC.SuppressFinalize(this);
    }
}