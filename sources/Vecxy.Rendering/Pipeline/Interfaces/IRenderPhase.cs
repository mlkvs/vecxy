namespace Vecxy.Rendering;

public interface IRenderPhase : IDisposable
{
    public RENDER_PHASE_TYPE Type { get; }
    
    public void OnInitialize();
    public void OnRender();
    public void RegisterRenderable(IRenderable renderable);
}