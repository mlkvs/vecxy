using System.Diagnostics;
using Vecxy.Kernel;
using Vecxy.Native;
using Vecxy.Rendering;

namespace Vecxy.Engine;

public class Engine : IDisposable
{
    private readonly Stack<AppLayer> _appLayers;
    private readonly Stack<IModule> _modules;
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

        _modules = new Stack<IModule>([
            new RenderingModule(_window)
        ]);
    }

    public void Run()
    {
        _window.Initialize();

        foreach (var module in _modules)
        {
            module.OnLoad();
        }

        foreach (var module in _modules)
        {
            module.OnInitialize();
        }

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
        foreach (var module in _modules)
        {
            module.OnTick(dt);
        }

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
        foreach (var module in _modules)
        {
            module.OnUnload();
        }
        // TODO release managed resources here
    }
}