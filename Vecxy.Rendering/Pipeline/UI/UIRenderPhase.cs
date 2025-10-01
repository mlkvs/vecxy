namespace Vecxy.Rendering;

public class UIRenderPhase : IRenderPhase<IUIRenderContext>
{
    public RenderPhase Type => RenderPhase.UI;
    public IUIRenderContext Context { get; }
    
    public void OnBegin()
    {
    }

    public void OnRender()
    {
    }

    public void OnEnd()
    {
    }

    public void Dispose()
    {
    }
}