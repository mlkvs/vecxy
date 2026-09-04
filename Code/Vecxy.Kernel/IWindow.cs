using System.Numerics;

namespace Vecxy.Kernel;


public interface IWindow : IDisposable
{
    public readonly record struct Options(
        string Title,
        int Width,
        int Height,
        int? MonitorIndex = null);
    public readonly record struct KeyEvent(int Key, bool IsPressed, KeyModifiers Modifiers = KeyModifiers.None);
    public readonly record struct MouseButtonEvent(int Button, bool IsPressed);
    public readonly record struct MouseMoveEvent(float X, float Y);
    public readonly record struct MouseWheelEvent(float X, float Y);
    public readonly record struct TouchEvent(
        int Id,
        float X,
        float Y,
        ETouchPhase Phase,
        float Pressure = 1.0f,
        bool IsPrimary = false);
    
    int Width { get; }
    int Height { get; }
    int ClientWidth { get; }
    int ClientHeight { get; }

    bool IsRunning { get; }
    bool IsFullscreen { get; }
    bool IsCursorCaptured { get; }
    
    event Action<int, int>? Resized;
    event Action<KeyEvent>? KeyChanged;
    event Action<MouseButtonEvent>? MouseButtonChanged;
    event Action<MouseMoveEvent>? MouseMoved;
    event Action<MouseWheelEvent>? MouseWheelChanged;
    event Action<TouchEvent>? TouchChanged;
    
    void Initialize();
    void PollEvents();
    void MakeCurrent();
    void SuppressNextSwap();
    void SwapBuffers();
    void Close();
    void ToggleFullscreen();
    void SetCursorCaptured(bool captured);
    Vector2 ClientToFramebuffer(Vector2 position);
    Vector2 FramebufferToClient(Vector2 position);
    nint GetProcAddress(string name);
}

public readonly record struct TextInputEvent(string Text);
public readonly record struct TextCompositionEvent(string Text, int SelectionStart, int SelectionLength);

/// <summary>Committed text and IME composition stream supplied by a platform backend.</summary>
public interface ITextInputSource
{
    event Action<TextInputEvent>? TextInput;
    event Action? CompositionStarted;
    event Action<TextCompositionEvent>? CompositionUpdated;
    event Action<TextInputEvent>? CompositionCommitted;
    event Action? CompositionEnded;
}

[Flags]
public enum KeyModifiers : byte
{
    None = 0,
    Shift = 1,
    Control = 2,
    Alt = 4,
    Super = 8,
    Primary = Control | Super
}

public enum ETouchPhase : byte
{
    Began,
    Moved,
    Stationary,
    Ended,
    Cancelled
}

/// <summary>
/// Thread-safe hand-off used by platform hosts whose native touch callbacks live
/// outside the engine's window event loop.
/// </summary>
public static class PlatformTouchSource
{
    public static event Action<IWindow.TouchEvent>? Changed;

    public static void Publish(IWindow.TouchEvent eventData) =>
        Changed?.Invoke(eventData);
}

public static class PlatformApplicationLifecycle
{
    public static event Action<bool>? ActiveChanged;

    public static void PublishActiveChanged(bool isActive) => ActiveChanged?.Invoke(isActive);
}
