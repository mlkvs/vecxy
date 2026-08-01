using System.Numerics;
using Vecxy.Kernel;

namespace Vecxy.Engine;

internal sealed class HeadlessWindow(IWindow.Options options) : IWindow
{
    public int Width { get; } = Math.Max(1, options.Width);
    public int Height { get; } = Math.Max(1, options.Height);
    public int ClientWidth => Width;
    public int ClientHeight => Height;

    public bool IsRunning { get; private set; }
    public bool IsFullscreen { get; private set; }
    public bool IsCursorCaptured { get; private set; }

    public event Action<int, int>? Resized;
    public event Action<IWindow.KeyEvent>? KeyChanged;
    public event Action<IWindow.MouseButtonEvent>? MouseButtonChanged;
    public event Action<IWindow.MouseMoveEvent>? MouseMoved;
    public event Action<IWindow.MouseWheelEvent>? MouseWheelChanged;

    public void Initialize()
    {
        IsRunning = true;
        Resized?.Invoke(Width, Height);
    }

    public void PollEvents()
    {
    }

    public void MakeCurrent()
    {
    }

    public void SwapBuffers()
    {
    }

    public void Close()
    {
        IsRunning = false;
    }

    public void ToggleFullscreen()
    {
        IsFullscreen = !IsFullscreen;
    }

    public void SetCursorCaptured(bool captured)
    {
        IsCursorCaptured = captured;
    }

    public Vector2 ClientToFramebuffer(Vector2 position) => position;

    public Vector2 FramebufferToClient(Vector2 position) => position;

    public nint GetProcAddress(string name) => 0;

    public void Dispose()
    {
        IsRunning = false;
    }
}
