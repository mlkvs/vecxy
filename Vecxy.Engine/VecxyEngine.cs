using Vecxy.Rendering;

namespace Vecxy.Engine;

public class VecxyEngine : IDisposable
{
    private readonly RenderSystem _render;
    private readonly RenderWindow _window;

    public VecxyEngine()
    {
        _window =  new RenderWindow(new RenderWindowOptions
        {
            Width = 800,
            Height = 600,
            Title = "Vecxy.Rendering",
        });
        
        _window.Load += OnLoad;
        _window.UpdateFrame += OnUpdate;
        _window.RenderFrame += OnFrame;
        _window.Unload += OnUnload;
        
        _render = new RenderSystem(_window);
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
        _render.OnTick();
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