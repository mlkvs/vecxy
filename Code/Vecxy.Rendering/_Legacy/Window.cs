using System.Numerics;
using System.Text;
using Silk.NET.Core.Contexts;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace Vecxy.Rendering._Legacy;

public readonly record struct WindowConfig(string Title, int Width, int Height);

public sealed class Window : INativeContext, IInput, IDisposable
{
    private readonly IWindow _window;
    private IInputContext? _input;
    private IKeyboard? _keyboard;
    private IMouse? _mouse;
    private Vector2 _mouseDelta;
    private Vector2 _lastMousePosition;
    private bool _hasMousePosition;
    private bool _leftMousePressed;
    private bool _leftMouseReleased;
    private Vector2 _scrollDelta;
    private readonly StringBuilder _textInput = new();
    private readonly List<TextEditCommand> _textEditCommands = [];
    private bool _f12Pressed;
    private bool _disposed;
    private bool _initialized;

    public Window(WindowConfig config, IGLContext? sharedContext = null)
    {
        var options = Silk.NET.Windowing.WindowOptions.Default;
        options.Title = config.Title;
        options.Size = new Vector2D<int>(config.Width, config.Height);
        options.API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core,
            ContextFlags.ForwardCompatible, new APIVersion(3, 3));
        options.ShouldSwapAutomatically = false;
        options.SharedContext = sharedContext;
        _window = Silk.NET.Windowing.Window.Create(options);
    }

    public bool IsRunning => _initialized && !_window.IsClosing;
    public Vector2D<int> Size => _window.Size;
    public event Action<Vector2D<int>>? Resized;
    internal IGLContext? GLContext => _window.GLContext;
    public void MakeCurrent() => _window.GLContext?.MakeCurrent();

    public void Initialize()
    {
        if (_initialized) return;
        _window.Initialize();
        _input = _window.CreateInput();
        _keyboard = _input.Keyboards.FirstOrDefault();
        if (_keyboard is not null)
        {
            _keyboard.KeyChar += OnKeyChar;
            _keyboard.KeyDown += OnKeyDown;
        }
        _mouse = _input.Mice.FirstOrDefault();
        if (_mouse is not null)
        {
            _mouse.MouseMove += OnMouseMove;
            _mouse.MouseDown += OnMouseDown;
            _mouse.MouseUp += OnMouseUp;
            _mouse.Scroll += OnScroll;
        }
        _window.Resize += size => Resized?.Invoke(size);
        _initialized = true;
    }

    public void ProcessEvents() => _window.DoEvents();
    public void SwapBuffers() => _window.SwapBuffers();

    public bool IsKeyDown(InputKey key) => _keyboard?.IsKeyPressed(key switch
    {
        InputKey.W => Key.W,
        InputKey.A => Key.A,
        InputKey.S => Key.S,
        InputKey.D => Key.D,
        InputKey.Q => Key.Q,
        InputKey.E => Key.E,
        InputKey.LeftShift => Key.ShiftLeft,
        InputKey.Escape => Key.Escape,
        _ => Key.Unknown
    }) ?? false;

    public bool IsRightMouseDown => _mouse?.IsButtonPressed(MouseButton.Right) ?? false;
    public bool IsLeftMouseDown => _mouse?.IsButtonPressed(MouseButton.Left) ?? false;
    public bool IsLeftMousePressed => _leftMousePressed;
    public bool IsLeftMouseReleased => _leftMouseReleased;
    public Vector2 MousePosition => _mouse?.Position ?? Vector2.Zero;
    public string ClipboardText
    {
        get => _keyboard?.ClipboardText ?? string.Empty;
        set { if (_keyboard is not null) _keyboard.ClipboardText = value; }
    }

    public bool ConsumeLeftMousePressed()
    {
        var value = _leftMousePressed;
        _leftMousePressed = false;
        return value;
    }
    public bool ConsumeF12Pressed() { var value = _f12Pressed; _f12Pressed = false; return value; }

    public bool ConsumeLeftMouseReleased()
    {
        var value = _leftMouseReleased;
        _leftMouseReleased = false;
        return value;
    }

    public Vector2 ConsumeScrollDelta()
    {
        var value = _scrollDelta;
        _scrollDelta = Vector2.Zero;
        return value;
    }

    public string ConsumeTextInput()
    {
        var value = _textInput.ToString();
        _textInput.Clear();
        return value;
    }

    public IReadOnlyList<TextEditCommand> ConsumeTextEditCommands()
    {
        var value = _textEditCommands.ToArray();
        _textEditCommands.Clear();
        return value;
    }

    public Vector2 ConsumeMouseDelta()
    {
        var value = _mouseDelta;
        _mouseDelta = Vector2.Zero;
        return value;
    }

    public void SetMouseCaptured(bool captured)
    {
        if (_mouse is not null)
            _mouse.Cursor.CursorMode = captured ? CursorMode.Raw : CursorMode.Normal;
    }

    public nint GetProcAddress(string proc, int? slot = null) =>
        _window.GLContext?.GetProcAddress(proc, slot) ?? nint.Zero;

    public bool TryGetProcAddress(string proc, out nint addr, int? slot = null)
    {
        addr = GetProcAddress(proc, slot);
        return addr != nint.Zero;
    }

    private void OnMouseMove(IMouse mouse, Vector2 position)
    {
        if (_hasMousePosition) _mouseDelta += position - _lastMousePosition;
        _lastMousePosition = position;
        _hasMousePosition = true;
    }

    private void OnMouseDown(IMouse mouse, MouseButton button)
    {
        if (button == MouseButton.Left) _leftMousePressed = true;
    }

    private void OnMouseUp(IMouse mouse, MouseButton button)
    {
        if (button == MouseButton.Left) _leftMouseReleased = true;
    }

    private void OnScroll(IMouse mouse, ScrollWheel wheel) => _scrollDelta += new Vector2(wheel.X, wheel.Y);
    private void OnKeyChar(IKeyboard keyboard, char character)
    {
        if (!char.IsControl(character)) _textInput.Append(character);
    }
    private void OnKeyDown(IKeyboard keyboard, Key key, int scanCode)
    {
        if (key == Key.F12) _f12Pressed = true;
        var control = keyboard.IsKeyPressed(Key.ControlLeft) || keyboard.IsKeyPressed(Key.ControlRight);
        var shift = keyboard.IsKeyPressed(Key.ShiftLeft) || keyboard.IsKeyPressed(Key.ShiftRight);
        var command = key switch
        {
            Key.Left when shift => TextEditCommand.SelectLeft, Key.Right when shift => TextEditCommand.SelectRight,
            Key.Home when shift => TextEditCommand.SelectHome, Key.End when shift => TextEditCommand.SelectEnd,
            Key.Left => TextEditCommand.Left, Key.Right => TextEditCommand.Right, Key.Home => TextEditCommand.Home, Key.End => TextEditCommand.End,
            Key.Backspace => TextEditCommand.Backspace, Key.Delete => TextEditCommand.Delete,
            Key.A when control => TextEditCommand.SelectAll, Key.C when control => TextEditCommand.Copy,
            Key.X when control => TextEditCommand.Cut, Key.V when control => TextEditCommand.Paste,
            _ => (TextEditCommand?)null
        };
        if (command.HasValue) _textEditCommands.Add(command.Value);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_mouse is not null)
        {
            _mouse.MouseMove -= OnMouseMove;
            _mouse.MouseDown -= OnMouseDown;
            _mouse.MouseUp -= OnMouseUp;
            _mouse.Scroll -= OnScroll;
        }
        if (_keyboard is not null)
        {
            _keyboard.KeyChar -= OnKeyChar;
            _keyboard.KeyDown -= OnKeyDown;
        }
        try { _input?.Dispose(); }
        catch (ObjectDisposedException) { }
        _input = null;
        try { _window.Dispose(); }
        catch (ObjectDisposedException) { }
    }
}
