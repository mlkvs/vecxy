namespace Vecxy.Rendering;

public class D2RenderPhase(ID2RenderContext context) : RenderPhaseBase(context)
{
    public override RENDER_PHASE_TYPE Type => RENDER_PHASE_TYPE.D2;

    public override void Initialize()
    {
        RegisterLayer(new D2MainRenderPhaseLayer(context));
        
        base.Initialize();
    }

    public override void OnBegin()
    {
        base.OnBegin();
        
        context.BeginBatch();
    }

    public override void OnEnd()
    {
        base.OnEnd();
        
        context.EndBatch();
        
        context.Flush();
    }
}