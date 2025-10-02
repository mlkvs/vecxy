namespace Vecxy.Rendering;

public class D2RenderPhase : RenderPhaseBase
{
    public override RENDER_PHASE_TYPE Type => RENDER_PHASE_TYPE.D2;

    public override void Initialize(IRenderContext ctx)
    {
        RegisterLayer(new D2MainRenderPhaseLayer());
        
        base.Initialize(ctx);
    }
}