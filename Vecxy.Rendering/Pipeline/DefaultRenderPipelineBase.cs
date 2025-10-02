namespace Vecxy.Rendering;

public class DefaultRenderPipelineBase(ID2RenderContext context) : RenderPipelineBase(context)
{
    public override void Initialize()
    {
        RegisterPhase(new D2RenderPhase(context));
        
        base.Initialize();
    }
}