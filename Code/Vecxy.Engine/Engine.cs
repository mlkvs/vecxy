using System.Diagnostics;
using Autofac;
using Vecxy.Assets;
using Vecxy.Kernel;
using Vecxy.Rendering;

namespace Vecxy.Engine;

public class Engine : IDisposable
{
    private readonly Window _window;
    private readonly IContainer _rootContainer;
    private readonly ILifetimeScope _modulesScope;
    private readonly List<ILifetimeScope> _layerScopes = []; 
    
    private readonly List<IModule> _moduleInstances = [];
    private readonly AppLayer[] _layerInstances;
    private readonly EngineOptions _options;

    public Engine(AppLayer[] layers) : this(new EngineOptions(), layers)
    {
    }

    public Engine(EngineOptions options, AppLayer[] layers)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _rootContainer = new ContainerBuilder().Build();

        _window = new Window(new WindowConfig(options.WindowTitle, options.WindowWidth, options.WindowHeight));
        _moduleInstances.Add(new AssetsModule
        {
            LoadMode = options.UsePackedAssets ? AssetLoadMode.Packed : AssetLoadMode.LooseFiles,
            SourcePath = options.AssetsPath
        });
        _moduleInstances.Add(new RenderingModule(_window));

        _modulesScope = _rootContainer.BeginLifetimeScope(builder =>
        {
            builder.RegisterInstance(_window).AsSelf();

            foreach (var module in _moduleInstances)
            {
                builder.RegisterInstance(module).AsSelf().As<IModule>();
                var installers = module.GetType()
                    .GetNestedTypes()
                    .Where(t => t.IsSubclassOf(typeof(Autofac.Module)) && t.IsNested);

                foreach (var installerType in installers)
                {
                    var installer = (Autofac.Module)Activator.CreateInstance(installerType)!;
                    
                    builder.RegisterModule(installer);
                }
            }
            
            foreach (var layer in layers)
            {
                layer.OnGlobalBindings(builder);
            }
        });
        
        foreach (var module in _moduleInstances)
        {
            _modulesScope.InjectProperties(module);
        }
        
        foreach (var layer in layers)
        {
            var layerScope = _modulesScope.BeginLifetimeScope(builder =>
            {
                layer.OnLocalBindings(builder);

                builder.RegisterInstance(layer)
                    .AsSelf()
                    .PropertiesAutowired();
            });

            layerScope.InjectProperties(layer);

            _layerScopes.Add(layerScope);
        }
        
        _layerInstances = layers;
    }

    public void Run()
    {
        _window.Initialize();

        foreach (var module in _moduleInstances) module.OnLoad(_modulesScope);
        foreach (var module in _moduleInstances) module.OnInitialize();
        foreach (var appLayer in _layerInstances) appLayer.OnInitialize();

        var targetTicksPerFrame = Stopwatch.Frequency / Math.Max(1, _options.TargetFrameRate);

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
        foreach (var module in _moduleInstances) module.OnTick(dt);
        foreach (var appLayer in _layerInstances) appLayer.OnTick(dt);
    }

    public void Frame()
    {
        foreach (var module in _moduleInstances) module.OnFrame();
        foreach (var appLayer in _layerInstances) appLayer.OnFrame();
    }

    public void Dispose()
    {
        for (var index = _moduleInstances.Count - 1; index >= 0; index--)
        {
            _moduleInstances[index].OnUnload();
        }

        for (var index = _layerScopes.Count - 1; index >= 0; index--) _layerScopes[index].Dispose();
        _modulesScope.Dispose();
        _rootContainer.Dispose();
        _window.Dispose();
    }
}
