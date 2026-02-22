using System.Diagnostics;
using System.Reflection;
using Autofac;
using Vecxy.Assets;
using Vecxy.Assets.V2;
using Vecxy.Kernel;
using Vecxy.Native;
using Vecxy.Rendering;
using Vecxy.Scripting;

namespace Vecxy.Engine;

public class Engine : IDisposable
{
    private readonly Window _window;

    private readonly IContainer _rootContainer;
    private readonly ILifetimeScope _modulesScope;
    private readonly List<ILifetimeScope> _layerScopes = []; 
    
    private readonly List<IModule> _moduleInstances = [];
    private readonly AppLayer[] _layerInstances;

    public Engine(AppLayer[] layers)
    {
        var modulesTypes = new[]
        {
            typeof(AssetsModuleV2),
        };
        
        _rootContainer = new ContainerBuilder().Build();
        
        _window = new Window(new WindowConfig
        {
            Title = $"{App.Info.Name} v{App.Info.Version}",
            Width = 800,
            Height = 600
        });

        _modulesScope = _rootContainer.BeginLifetimeScope(builder =>
        {
            for (int index = 0, count = modulesTypes.Length; index < count; ++index)
            {
                var moduleType = modulesTypes[index];

                if (!(typeof(IModule).IsAssignableFrom(moduleType) && moduleType is { IsInterface: false, IsAbstract: false }))
                {
                    continue;
                }
                
                var module = (IModule)Activator.CreateInstance(moduleType)!;
                
                _moduleInstances.Add(module);
                
                var installers = moduleType
                    .GetNestedTypes()
                    .Where(t => t.IsSubclassOf(typeof(Autofac.Module)) && t.IsNested);

                foreach (var installerType in installers)
                {
                    if (!typeof(IModule).IsAssignableFrom(installerType.DeclaringType))
                    {
                        continue;
                    }
                
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
        foreach (var module in _moduleInstances) module.OnTick(dt);
        foreach (var appLayer in _moduleInstances) appLayer.OnTick(dt);
    }

    public void Frame()
    {
        foreach (var module in _moduleInstances) module.OnFrame();
        foreach (var appLayer in _moduleInstances) appLayer.OnFrame();
    }

    public void Dispose()
    {
        foreach (var module in _moduleInstances) module.OnUnload();
    }
}