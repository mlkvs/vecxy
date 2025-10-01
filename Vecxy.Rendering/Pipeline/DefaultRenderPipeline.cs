namespace Vecxy.Rendering;

public class DefaultRenderPipeline(IRenderWindow window) : RenderPipeline(window)
{
    public override void Initialize()
    {
        RegisterRenderPhase(new D2RenderPhase());
        RegisterRenderPhase(new UIRenderPhase());
        
        base.Initialize();
    }
}