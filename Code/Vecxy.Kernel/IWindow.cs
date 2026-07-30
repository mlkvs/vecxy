namespace Vecxy.Kernel;


public interface IWindow : IDisposable
{
    public readonly record struct Options(
        string Title,
        int Width,
        int Height,
        int? MonitorIndex = null);
    public readonly record struct KeyEvent(int Key, bool IsPressed);
    public readonly record struct MouseButtonEvent(int Button, bool IsPressed);
    public readonly record struct MouseMoveEvent(float X, float Y);
    public readonly record struct MouseWheelEvent(float X, float Y);
    
    int Width { get; }
    int Height { get; }

    bool IsRunning { get; }
    bool IsFullscreen { get; }
    bool IsCursorCaptured { get; }
    
    event Action<int, int>? Resized;
    event Action<KeyEvent>? KeyChanged;
    event Action<MouseButtonEvent>? MouseButtonChanged;
    event Action<MouseMoveEvent>? MouseMoved;
    event Action<MouseWheelEvent>? MouseWheelChanged;
    
    void Initialize();
    void PollEvents();
    void MakeCurrent();
    void SwapBuffers();
    void Close();
    void ToggleFullscreen();
    void SetCursorCaptured(bool captured);
    nint GetProcAddress(string name);
}
