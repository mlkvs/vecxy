using System.Diagnostics;
using Vecxy.Native;

namespace Vecxy.Engine;

public class Engine : IDisposable
{
    private readonly Stack<AppLayer> _appLayers;
    private readonly Window _window;

    public Engine(AppLayer[] layers)
    {
        _appLayers = new Stack<AppLayer>(layers);

        _window = new Window(new WindowConfig
        {
            Title = $"{App.Info.Name} v{App.Info.Version}",
            Width = 800,
            Height = 600
        });
    }

    public void Run()
    {
        foreach (var appLayer in _appLayers)
        {
            appLayer.OnInitialize();
        }

        var targetTicksPerFrame = Stopwatch.Frequency / App.TargetFrameRate;

        var sw = Stopwatch.StartNew();

        var lastFrameTicks = sw.ElapsedTicks;

        while (_window.IsRunning)
        {
            var currentFrameTicks = sw.ElapsedTicks;

            var dt = (double)(currentFrameTicks - lastFrameTicks) / Stopwatch.Frequency;
            lastFrameTicks = currentFrameTicks;

            if (dt > 0.1)
            {
                dt = 0.1;
            }

            _window.ProcessEvents();

            Tick((float)dt);
            Frame();

            var frameEndTime = sw.ElapsedTicks;
            var elapsedTicks = frameEndTime - currentFrameTicks;

            if (elapsedTicks < targetTicksPerFrame)
            {
                var waitMs = (int)((targetTicksPerFrame - elapsedTicks) * 1000 / Stopwatch.Frequency);

                if (waitMs > 0)
                {
                    Thread.Sleep(waitMs);
                }
            }
        }
    }

    public void Tick(float dt)
    {
        foreach (var appLayer in _appLayers)
        {
            appLayer.OnTick(dt);
        }
    }

    public void Frame()
    {

    }

    public void Dispose()
    {
        // TODO release managed resources here
    }
}