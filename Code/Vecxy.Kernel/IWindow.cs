namespace Vecxy.Kernel;


public interface IWindow : IDisposable
{
    public readonly record struct Options(string Title, int Width, int Height);
    
    int Width { get; }
    int Height { get; }

    bool IsRunning { get; }
    
    event Action<int, int>? Resized;
    
    void Initialize();
    void PollEvents();
    void MakeCurrent();
    void SwapBuffers();
    nint GetProcAddress(string name);
}