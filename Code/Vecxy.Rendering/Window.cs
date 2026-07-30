using Silk.NET.Maths;
using Silk.NET.Input;
using Silk.NET.Windowing;
using IWindow = Vecxy.Kernel.IWindow;

namespace Vecxy.Rendering;

public sealed class Window : IWindow
{
    public Silk.NET.Windowing.IWindow NativeWindow => _instance;
    public IInputContext InputContext =>
        _input ?? throw new InvalidOperationException(
            "Window input is not initialized.");

    public int Width => _instance.FramebufferSize.X;
    public int Height => _instance.FramebufferSize.Y;

    public bool IsRunning => _initialized && !_instance.IsClosing;
    public bool IsFullscreen => _isFullscreen;
    public bool IsCursorCaptured { get; private set; }

    public event Action<int, int>? Resized;
    public event Action<IWindow.KeyEvent>? KeyChanged;
    public event Action<IWindow.MouseButtonEvent>? MouseButtonChanged;
    public event Action<IWindow.MouseMoveEvent>? MouseMoved;
    public event Action<IWindow.MouseWheelEvent>? MouseWheelChanged;

    private readonly Silk.NET.Windowing.IWindow _instance;
    private IInputContext? _input;
    private IMouse? _primaryMouse;
    private Vector2D<int> _windowedSize;
    private Vector2D<int> _windowedPosition;
    private WindowState _windowedState = WindowState.Normal;

    private bool _initialized;
    private bool _isFullscreen;

    public Window(IWindow.Options options)
    {
        var wOptions = WindowOptions.Default;

        wOptions.Title = options.Title;
        wOptions.Size = new Vector2D<int>(options.Width, options.Height);
        wOptions.API = new GraphicsAPI
        (
            ContextAPI.OpenGL,
            ContextProfile.Core,
            ContextFlags.ForwardCompatible,
            new APIVersion(3, 3)
        );
        wOptions.ShouldSwapAutomatically = false;

        _instance = CreateWindow(wOptions, options.MonitorIndex);
        _windowedSize = wOptions.Size;
    }

    private static Silk.NET.Windowing.IWindow CreateWindow(
        WindowOptions options,
        int? monitorIndex)
    {
        if (monitorIndex is null)
            return Silk.NET.Windowing.Window.Create(options);

        var platform =
            Silk.NET.Windowing.Window.GetWindowPlatform(false) ??
            throw new InvalidOperationException(
                "No desktop window platform is available.");
        var monitors = platform.GetMonitors();
        var monitor = monitors.FirstOrDefault(
            candidate => candidate.Index == monitorIndex.Value);

        if (monitor is not null)
            return monitor.CreateWindow(options);

        var availableIndices = string.Join(
            ", ",
            monitors.Select(candidate => candidate.Index));
        throw new ArgumentOutOfRangeException(
            nameof(monitorIndex),
            monitorIndex,
            $"Monitor index {monitorIndex} was not found. Available monitor indices: {availableIndices}.");
    }

    public void Initialize()
    {
        _instance.Initialize();

        _instance.Resize += size => Resized?.Invoke(size.X, size.Y);
        _input = _instance.CreateInput();

        foreach (var keyboard in _input.Keyboards)
        {
            keyboard.KeyDown += OnKeyDown;
            keyboard.KeyUp += OnKeyUp;
        }

        foreach (var mouse in _input.Mice)
        {
            _primaryMouse ??= mouse;
            mouse.MouseDown += OnMouseDown;
            mouse.MouseUp += OnMouseUp;
            mouse.MouseMove += OnMouseMove;
            mouse.Scroll += OnMouseScroll;
        }

        _windowedSize = _instance.Size;
        _windowedPosition = _instance.Position;
        _initialized = true;
    }

    public void PollEvents() => _instance.DoEvents();

    public void SwapBuffers() => _instance.SwapBuffers();

    public void MakeCurrent() => _instance.GLContext?.MakeCurrent();

    public void Close() => _instance.Close();

    public void ToggleFullscreen()
    {
        if (_isFullscreen)
        {
            _instance.WindowState = WindowState.Normal;
            if (_windowedState == WindowState.Maximized)
                _instance.WindowState = WindowState.Maximized;

            _instance.Size = _windowedSize;
            _instance.Position = _windowedPosition;
            _isFullscreen = false;
        }
        else
        {
            _windowedSize = _instance.Size;
            _windowedPosition = _instance.Position;
            _windowedState = _instance.WindowState;
            _instance.WindowState = WindowState.Fullscreen;
            _isFullscreen = true;
        }

        var framebufferSize = _instance.FramebufferSize;
        Resized?.Invoke(framebufferSize.X, framebufferSize.Y);
    }

    public void SetCursorCaptured(bool captured)
    {
        if (!_initialized)
        {
            IsCursorCaptured = captured;
            return;
        }

        var cursor = _primaryMouse?.Cursor;
        if (cursor is null)
        {
            IsCursorCaptured = false;
            return;
        }

        var targetMode = captured
            ? cursor.IsSupported(CursorMode.Disabled)
                ? CursorMode.Disabled
                : CursorMode.Hidden
            : CursorMode.Normal;

        if (!cursor.IsSupported(targetMode))
            return;

        cursor.CursorMode = targetMode;
        IsCursorCaptured = captured;
    }

    public nint GetProcAddress(string name) =>
        _instance.GLContext?.GetProcAddress(name) ?? 0;

    public void Dispose()
    {
        try
        {
            SetCursorCaptured(false);
            _input?.Dispose();
            _instance.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void OnKeyDown(IKeyboard _, Key key, int _2)
    {
        KeyChanged?.Invoke(
            new IWindow.KeyEvent(
                (int)key,
                true));
    }

    private void OnKeyUp(IKeyboard _, Key key, int _2)
    {
        KeyChanged?.Invoke(
            new IWindow.KeyEvent(
                (int)key,
                false));
    }

    private void OnMouseDown(IMouse _, MouseButton button)
    {
        MouseButtonChanged?.Invoke(
            new IWindow.MouseButtonEvent(
                (int)button,
                true));
    }

    private void OnMouseUp(IMouse _, MouseButton button)
    {
        MouseButtonChanged?.Invoke(
            new IWindow.MouseButtonEvent(
                (int)button,
                false));
    }

    private void OnMouseMove(IMouse _, System.Numerics.Vector2 position)
    {
        MouseMoved?.Invoke(
            new IWindow.MouseMoveEvent(
                position.X,
                position.Y));
    }

    private void OnMouseScroll(IMouse _, ScrollWheel wheel)
    {
        MouseWheelChanged?.Invoke(
            new IWindow.MouseWheelEvent(
                wheel.X,
                wheel.Y));
    }
}
