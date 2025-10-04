namespace Vecxy.Rendering;

public interface IRenderPhase : IDisposable
{
    public RENDER_PHASE_TYPE Type { get; }
    
    public void Initialize();
    public void OnBegin();
    public void OnRender();
    public void OnEnd();

    public void RegisterRenderable(IRenderable renderable);
}