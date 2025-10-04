namespace Vecxy.Rendering;

public interface IRenderPhaseLayer : IDisposable
{
    public RENDER_PHASE_LAYER_TYPE Type { get; }
    public void Initialize();
    public void OnBegin();
    public void OnRender(List<IRenderable> renderables);
    public void OnEnd();
}