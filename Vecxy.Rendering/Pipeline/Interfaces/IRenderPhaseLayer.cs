namespace Vecxy.Rendering;

public interface IRenderPhaseLayer : IDisposable
{
    public RENDER_PHASE_LAYER_TYPE Type { get; }
    public void Initialize(IRenderContext ctx);
    public void OnBegin(IRenderContext ctx);
    public void OnRender(IRenderContext ctx);
    public void OnEnd(IRenderContext ctx);
}