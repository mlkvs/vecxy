using Vecxy.Rendering;

namespace Vecxy.Engine;

public class Engine : IDisposable
{
    public const string VERSION = "0.0.1";
    
    private readonly RenderWindow _window;
    private readonly Application _application;

    public Engine(Application application)
    {
        _application = application;
        
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
    }

    public void Run()
    {
        _window.Run();
    }

    private void OnLoad()
    {
        _application.OnInitialize();
    }

    private void OnUpdate(OpenTK.Windowing.Common.FrameEventArgs e)
    {
        var deltaTime = (float)e.Time;
        
        _application.OnTick();
    }

    private void OnFrame(OpenTK.Windowing.Common.FrameEventArgs e)
    {
        var deltaTime = (float)e.Time;
    }

    private void OnUnload()
    {
    }

    public void Dispose()
    {
        _window.Dispose();
    }
}