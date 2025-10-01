namespace Vecxy.Rendering;

public interface IRenderable
{
    public int Id { get; }
    
    public void OnRender(IRenderContext context);
}