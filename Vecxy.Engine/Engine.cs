using Vecxy.Rendering;

namespace Vecxy.Engine;

public class Engine : IDisposable
{
    private readonly RenderingWindow _renderingWindow;

    public Engine()
    {
        var options = new RenderingWindowOptions
        {
            Width = 800,
            Height = 600,
            Title = "Vecxy.Rendering",
        };
        
        _renderingWindow =  new RenderingWindow(options);
        
        _renderingWindow.Load += OnLoad;
        _renderingWindow.UpdateFrame += OnUpdate;
        _renderingWindow.RenderFrame += OnRenderingWindowRenderFrame;
        _renderingWindow.Unload += OnUnload;
    }

    public void Run()
    {
        _renderingWindow.Run();
    }

    private void OnLoad()
    {
    }

    private void OnUpdate(OpenTK.Windowing.Common.FrameEventArgs e)
    {
        var deltaTime = (float)e.Time;
    }

    private void OnRenderingWindowRenderFrame(OpenTK.Windowing.Common.FrameEventArgs e)
    {
        
    }

    private void OnUnload()
    {

    }

    public void Dispose()
    {
        _renderingWindow.Dispose();
    }
}