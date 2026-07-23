using Silk.NET.Maths;
using Silk.NET.Windowing;
using IWindow = Vecxy.Kernel.IWindow;
using WindowOptions = Vecxy.Kernel.WindowOptions;

namespace Vecxy.Rendering;

public class Window : IWindow
{
    public int Width => _instance.FramebufferSize.X;
    public int Height => _instance.FramebufferSize.Y;

    public bool IsRunning => _initialized && !_instance.IsClosing;

    public event Action<int, int>? Resized;

    private readonly Silk.NET.Windowing.IWindow _instance;

    private bool _initialized;

    public Window(WindowOptions windowOptions)
    {
        var options = Silk.NET.Windowing.WindowOptions.Default;

        options.Title = windowOptions.Title;
        options.Size = new Vector2D<int>(windowOptions.Width, windowOptions.Height);
        options.API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible,
            new APIVersion(3, 3));
        options.ShouldSwapAutomatically = false;

        _instance = Silk.NET.Windowing.Window.Create(options);
    }

    public void Initialize()
    {
        _instance.Initialize();

        _instance.Resize += size => Resized?.Invoke(size.X, size.Y);

        _initialized = true;
    }

    public void PollEvents() => _instance.DoEvents();

    public void SwapBuffers() => _instance.SwapBuffers();

    public void MakeCurrent() => _instance.GLContext?.MakeCurrent();

    public nint GetProcAddress(string name) =>
        _instance.GLContext?.GetProcAddress(name) ?? 0;

    public void Dispose()
    {
        try
        {
            _instance.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
