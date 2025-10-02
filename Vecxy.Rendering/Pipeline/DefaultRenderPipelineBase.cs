namespace Vecxy.Rendering;

public class DefaultRenderPipelineBase(IRenderContext context) : RenderPipelineBase(context)
{
    public override void Initialize()
    {
        RegisterPhase(new D2RenderPhase());
        
        base.Initialize();
    }
}