namespace Vecxy.Rendering;

public interface IRenderWindow
{
    public int Width { get; }
    public int Height { get; }

    public void Clear();
    public void SwapBuffers();
}