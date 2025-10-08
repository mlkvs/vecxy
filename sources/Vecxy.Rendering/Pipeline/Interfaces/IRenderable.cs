namespace Vecxy.Rendering;

public interface IRenderable
{
    public RENDER_PHASE_TYPE RenderPhase { get; }
    public void OnRender(IRenderContext context);
}