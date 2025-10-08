namespace Vecxy.Rendering;

public class D2RenderPhase(ID2RenderContext context) : RenderPhaseBase(context)
{
    public override RENDER_PHASE_TYPE Type => RENDER_PHASE_TYPE.D2;
    
    public override void OnRender()
    {
        context.BeginBatch();
        
        foreach (var renderable in Renderables)
        {
            renderable.OnRender(context);
        }
        
        context.EndBatch();
    }
}