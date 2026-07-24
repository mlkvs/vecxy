using Silk.NET.Maths;
using Silk.NET.Windowing;
using IWindow = Vecxy.Kernel.IWindow;

namespace Vecxy.Rendering;

public sealed class Window : IWindow
{
    public int Width => _instance.FramebufferSize.X;
    public int Height => _instance.FramebufferSize.Y;

    public bool IsRunning => _initialized && !_instance.IsClosing;

    public event Action<int, int>? Resized;

    private readonly Silk.NET.Windowing.IWindow _instance;

    private bool _initialized;

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

        _instance = Silk.NET.Windowing.Window.Create(wOptions);
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