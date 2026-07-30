namespace Vecxy.Rendering;

public interface IRenderTarget
{
    int Width { get; }
    int Height { get; }

    void Bind(GraphicsDevice device);
    void Present();
}