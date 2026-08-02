using System.Numerics;
using Vecxy.Input;
using Vecxy.Kernel;
using Xunit;

namespace Vecxy.Engine.Tests;

public sealed class TouchInputTests
{
    [Fact]
    public void PrimaryTouchDrivesUnifiedPointerUntilRelease()
    {
        var window = new FakeWindow();
        var module = new InputModule(window, new FakeCapture());
        module.OnInitialize();

        window.Touch(new IWindow.TouchEvent(7, 20, 30, ETouchPhase.Began, 0.7f, true));
        module.OnUpdate(0.016f);
        Assert.Equal(EPointerKind.Touch, module.PointerKind);
        Assert.True(module.IsPrimaryPointerPressed);
        Assert.Equal(new Vector2(20, 30), module.PointerPosition);
        Assert.Single(module.Touches);

        window.Touch(new IWindow.TouchEvent(7, 25, 42, ETouchPhase.Moved, 0.8f, true));
        module.OnUpdate(0.016f);
        Assert.Equal(new Vector2(5, 12), module.PointerDelta);

        window.Touch(new IWindow.TouchEvent(7, 25, 42, ETouchPhase.Ended, 0, true));
        module.OnUpdate(0.016f);
        Assert.False(module.IsPrimaryPointerPressed);
        Assert.Equal(ETouchPhase.Ended, Assert.Single(module.Touches).Phase);

        module.OnUpdate(0.016f);
        Assert.Empty(module.Touches);
        module.Dispose();
    }

    private sealed class FakeCapture : IInputCaptureState
    {
        public bool SuppressKeyboard { get; set; }
        public bool SuppressMouse { get; set; }
    }

    private sealed class FakeWindow : IWindow
    {
        public int Width => 450;
        public int Height => 900;
        public int ClientWidth => Width;
        public int ClientHeight => Height;
        public bool IsRunning => true;
        public bool IsFullscreen => false;
        public bool IsCursorCaptured => false;
        public event Action<int, int>? Resized { add { } remove { } }
        public event Action<IWindow.KeyEvent>? KeyChanged { add { } remove { } }
        public event Action<IWindow.MouseButtonEvent>? MouseButtonChanged { add { } remove { } }
        public event Action<IWindow.MouseMoveEvent>? MouseMoved { add { } remove { } }
        public event Action<IWindow.MouseWheelEvent>? MouseWheelChanged { add { } remove { } }
        public event Action<IWindow.TouchEvent>? TouchChanged;
        public void Touch(IWindow.TouchEvent eventData) => TouchChanged?.Invoke(eventData);
        public void Initialize() { }
        public void PollEvents() { }
        public void MakeCurrent() { }
        public void SwapBuffers() { }
        public void Close() { }
        public void ToggleFullscreen() { }
        public void SetCursorCaptured(bool captured) { }
        public Vector2 ClientToFramebuffer(Vector2 position) => position;
        public Vector2 FramebufferToClient(Vector2 position) => position;
        public nint GetProcAddress(string name) => 0;
        public void Dispose() { }
    }
}
