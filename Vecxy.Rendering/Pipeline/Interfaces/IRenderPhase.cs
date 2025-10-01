namespace Vecxy.Rendering;

public interface IRenderPhase : IDisposable
{
    public RenderPhase Type { get; }
    public void Initialize();
    public void OnBegin();
    public void OnRender();
    public void OnEnd();
}

public interface IRenderPhase<TRenderContext> : IRenderPhase where TRenderContext : IRenderContext
{
    public TRenderContext Context { get; }
}