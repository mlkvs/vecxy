namespace Vecxy.Kernel;

public readonly record struct WindowOptions(string Title, int Width, int Height);

public interface IWindow : 
    IRunContext,
    IGraphicsContext
{
    int Width { get; }
    int Height { get; }

    event Action<int, int>? Resized;
}