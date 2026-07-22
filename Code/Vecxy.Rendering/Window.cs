using Silk.NET.Core.Contexts;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace Vecxy.Rendering;

public readonly record struct WindowConfig(string Title, int Width, int Height);

public sealed class Window : INativeContext, IDisposable
{
    private readonly IWindow _window;
    private bool _initialized;

    public Window(WindowConfig config)
    {
        var options = WindowOptions.Default;
        options.Title = config.Title;
        options.Size = new Vector2D<int>(config.Width, config.Height);
        options.API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core,
            ContextFlags.ForwardCompatible, new APIVersion(3, 3));
        options.ShouldSwapAutomatically = false;
        _window = Silk.NET.Windowing.Window.Create(options);
    }

    public bool IsRunning => _initialized && !_window.IsClosing;
    public Vector2D<int> Size => _window.Size;
    public event Action<Vector2D<int>>? Resized;

    public void Initialize()
    {
        if (_initialized) return;
        _window.Initialize();
        _window.Resize += size => Resized?.Invoke(size);
        _initialized = true;
    }

    public void ProcessEvents() => _window.DoEvents();
    public void SwapBuffers() => _window.SwapBuffers();

    public nint GetProcAddress(string proc, int? slot = null) =>
        _window.GLContext?.GetProcAddress(proc, slot) ?? nint.Zero;

    public bool TryGetProcAddress(string proc, out nint addr, int? slot = null)
    {
        addr = GetProcAddress(proc, slot);
        return addr != nint.Zero;
    }

    public void Dispose() => _window.Dispose();
}
