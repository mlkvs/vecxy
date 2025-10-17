using Vecxy.Kernel;
using Vecxy.Rendering;

namespace Vecxy.Engine;

public class VecxyEngine : IDisposable
{
    private readonly IModule _render;
    private readonly RenderWindow _window;

    public VecxyEngine()
    {
        _window = RenderWindow.Create(new RenderWindowOptions
        {
            Width = 800,
            Height = 600,
            Title = "Vecxy.Rendering",
            IsDebug = true
        });
        
        _window.Load += OnLoad;
        _window.UpdateFrame += OnUpdate;
        _window.RenderFrame += OnFrame;
        _window.Unload += OnUnload;
        
        _render = new RenderingModule(_window);
    }

    public void Run()
    {
        _window.Run();
    }

    private void OnLoad()
    {
        _render.OnLoad();
        _render.OnInitialize();
    }

    private void OnUpdate(OpenTK.Windowing.Common.FrameEventArgs e)
    {
        var deltaTime = (float)e.Time;
    }

    private void OnFrame(OpenTK.Windowing.Common.FrameEventArgs e)
    {
        var deltaTime = (float)e.Time;
        
        _render.OnTick(deltaTime);
    }

    private void OnUnload()
    {
        _render.OnUnload();
    }

    public void Dispose()
    {
        _render.Dispose();
        _window.Dispose();
    }
}