<<<<<<< Updated upstream
﻿using System.Diagnostics;
using Autofac;
using Autofac.Core;
using Vecxy.Kernel;
using Vecxy.Rendering;

namespace Vecxy.Engine;

public sealed class Engine : IDisposable
{
    private readonly EngineOptions _options;
    private readonly Window _window;

    private readonly IContainer _container;
    private readonly List<AppLayer> _appLayers;
    private readonly List<ILifetimeScope> _layerScopes = [];

    private int _initializedLayerCount;

    private bool _isRunning;
    private bool _disposed;

    public Engine(
        EngineOptions options,
        params AppLayer[] layers)
        : this(options, [], layers)
    {
    }

    public Engine(
        EngineOptions options,
        IEnumerable<Kernel.IModule> modules,
        params AppLayer[] layers)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(layers);

        _options = options;

        _window = new Window(
            new WindowConfig(
                options.WindowTitle,
                options.WindowWidth,
                options.WindowHeight
            )
        );

        _appLayers =
        [
            new EngineLayer(modules),
            ..layers
        ];

        var builder = new ContainerBuilder();

        builder.RegisterInstance(_window)
            .AsSelf()
            .SingleInstance();

        builder.RegisterInstance(_options)
            .AsSelf()
            .SingleInstance();

        foreach (var layer in _appLayers)
        {
            layer.OnGlobalBindings(builder);
        }

        _container = builder.Build();

        try
        {
            CreateLayerScopes();
        }
        catch
        {
            DisposeLayerScopes();
            _container.Dispose();
            _window.Dispose();

            throw;
        }
    }

    public void Run()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_isRunning)
        {
            throw new InvalidOperationException("Engine is already running.");
        }

        _isRunning = true;

        try
        {
            _window.Initialize();
            InitializeLayers();
            RunLoop();
        }
        finally
        {
            _isRunning = false;
        }
    }

    private void CreateLayerScopes()
    {
        foreach (var layer in _appLayers)
        {
            var scope = _container.BeginLifetimeScope(builder =>
            {
                layer.OnLocalBindings(builder);
            });

            try
            {
                scope.InjectProperties(layer);
                layer.OnScopeCreated(scope);

                _layerScopes.Add(scope);
            }
            catch
            {
                scope.Dispose();
                throw;
            }
        }
    }

    private void InitializeLayers()
    {
        foreach (var layer in _appLayers)
        {
            layer.OnInitialize();
            _initializedLayerCount++;
        }
    }

    private void RunLoop()
    {
        var targetFrameRate = Math.Max(1, _options.TargetFrameRate);
        var targetTicksPerFrame = Stopwatch.Frequency / targetFrameRate;

        var stopwatch = Stopwatch.StartNew();
        var lastFrameTicks = stopwatch.ElapsedTicks;

        while (_window.IsRunning)
        {
            var frameStartTicks = stopwatch.ElapsedTicks;

            var deltaTime =
                (double)(frameStartTicks - lastFrameTicks) /
                Stopwatch.Frequency;

            lastFrameTicks = frameStartTicks;

            deltaTime = Math.Min(deltaTime, 0.1);

            _window.ProcessEvents();

            Update((float)deltaTime);
            Render();

            WaitForNextFrame(stopwatch, frameStartTicks, targetTicksPerFrame);
        }
    }

    private void Update(float deltaTime)
    {
        foreach (var layer in _appLayers)
        {
            layer.OnUpdate(deltaTime);
        }
    }

    private void Render()
    {
        foreach (var layer in _appLayers)
        {
            layer.OnRender();
        }
    }

    private static void WaitForNextFrame(Stopwatch stopwatch, long frameStartTicks, long targetTicksPerFrame)
    {
        var targetEndTicks = frameStartTicks + targetTicksPerFrame;
        var remainingTicks = targetEndTicks - stopwatch.ElapsedTicks;

        if (remainingTicks <= 0)
        {
            return;
        }

        var remainingMilliseconds = remainingTicks * 1000.0 / Stopwatch.Frequency;

        if (remainingMilliseconds > 1.5)
        {
            Thread.Sleep(Math.Max(0, (int)remainingMilliseconds - 1));
        }

        while (stopwatch.ElapsedTicks < targetEndTicks)
        {
            Thread.SpinWait(16);
        }
    }

    private void UnloadLayers()
    {
        for (var index = _initializedLayerCount - 1; index >= 0; index--)
        {
            try
            {
                _appLayers[index].OnUnload();
            }
            catch
            {
            }
        }

        _initializedLayerCount = 0;
    }

    private void DisposeLayerScopes()
    {
        for (var index = _layerScopes.Count - 1; index >= 0; index--)
        {
            _layerScopes[index].Dispose();
        }

        _layerScopes.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        UnloadLayers();
        DisposeLayerScopes();

        _container.Dispose();
        _window.Dispose();

        GC.SuppressFinalize(this);
    }
}
=======
﻿using System.Diagnostics;
using Autofac;
using Vecxy.Assets;
using Vecxy.Kernel;
using Vecxy.Rendering;
using Vecxy.Engine.Scenes;
using Vecxy.UI;

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
    private readonly RenderingModule _renderingModule;
    private readonly AppLayerRenderAdapter[] _renderLayers;
    private readonly SceneManager _sceneManager;
    private readonly AssetsModule _assetsModule;
    private bool _disposed;

    public Engine(AppLayer[] layers) : this(new EngineOptions(), layers)
    {
    }

    public Engine(EngineOptions options, AppLayer[] layers)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _rootContainer = new ContainerBuilder().Build();

        _window = new Window(new WindowConfig(options.WindowTitle, options.WindowWidth, options.WindowHeight));
        _assetsModule = new AssetsModule
        {
            LoadMode = options.UsePackedAssets ? AssetLoadMode.Packed : AssetLoadMode.LooseFiles,
            SourcePath = options.AssetsPath
        };
        _moduleInstances.Add(_assetsModule);
        _renderingModule = new RenderingModule(_window);
        _sceneManager = new SceneManager(_renderingModule.Renderer, _window);
        AppLayer[] allLayers = [.. layers, new EditorLayer()];
        _moduleInstances.Add(_renderingModule);

        _modulesScope = _rootContainer.BeginLifetimeScope(builder =>
        {
            builder.RegisterInstance(_window).AsSelf().As<IInput>().ExternallyOwned();
            builder.RegisterInstance(_renderingModule.Renderer).As<IRenderer>().ExternallyOwned();
            builder.RegisterInstance(_renderingModule.UI).AsSelf().ExternallyOwned();
            builder.RegisterInstance(_renderingModule.GameScreen).AsSelf().ExternallyOwned();
            builder.RegisterInstance(_sceneManager).AsSelf().ExternallyOwned();
            builder.RegisterInstance(_assetsModule.Manager).AsSelf().ExternallyOwned();

            foreach (var module in _moduleInstances)
            {
                builder.RegisterInstance(module).AsSelf().As<IModule>().ExternallyOwned();
                var installers = module.GetType()
                    .GetNestedTypes()
                    .Where(t => t.IsSubclassOf(typeof(Autofac.Module)) && t.IsNested);

                foreach (var installerType in installers)
                {
                    var installer = (Autofac.Module)Activator.CreateInstance(installerType)!;
                    
                    builder.RegisterModule(installer);
                }
            }
            
            foreach (var layer in allLayers)
            {
                layer.OnGlobalBindings(builder);
            }
        });
        
        foreach (var module in _moduleInstances)
        {
            _modulesScope.InjectProperties(module);
        }
        
        foreach (var layer in allLayers)
        {
            var layerScope = _modulesScope.BeginLifetimeScope(builder =>
            {
                layer.OnLocalBindings(builder);

                builder.RegisterInstance(layer)
                    .AsSelf()
                    .ExternallyOwned()
                    .PropertiesAutowired();
            });

            layerScope.InjectProperties(layer);

            _layerScopes.Add(layerScope);
        }
        
        _layerInstances = allLayers;
        _renderLayers = [new AppLayerRenderAdapter(_sceneManager.Render),
            .. allLayers.Select(layer => new AppLayerRenderAdapter(layer.OnRender))];
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
            RenderFrame();

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
        _sceneManager.Update(dt);
        foreach (var appLayer in _layerInstances) appLayer.OnTick(dt);
    }

    private void RenderFrame()
    {
        _renderingModule.Render(_renderLayers, _options.ClearColor);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        for (var index = _layerInstances.Length - 1; index >= 0; index--)
        {
            _layerInstances[index].OnUnload();
        }

        _sceneManager.Dispose();

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
>>>>>>> Stashed changes
