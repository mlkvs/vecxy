namespace Vecxy.Rendering;

public class DefaultRenderPipeline(ID2RenderContext context) : RenderPipelineBase(context)
{
    public override void Initialize()
    {
        RegisterPhase(new D2RenderPhase(context));
        
        base.Initialize();
    }
}