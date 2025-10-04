namespace Vecxy.Rendering;

public interface IRenderContext
{
    public IRenderWindow Window { get; }
    public void Clear();
    public void SwapBuffers();
}