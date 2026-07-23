using System.Diagnostics;
using Autofac;
using Vecxy.Kernel;
using Vecxy.Rendering;

namespace Vecxy.Engine;

public sealed class Engine : IDisposable
{
    public sealed class Options
    {
        public WindowOptions Window = new();
        public int TargetFrameRate { get; init; } = 60;
        public string AssetsPath { get; init; } = Path.Combine(AppContext.BaseDirectory, "Assets");
        public bool UsePackedAssets { get; init; }
        public Vecxy.Rendering._Legacy.Color ClearColor { get; init; } = Vecxy.Rendering._Legacy.Color.CornflowerBlue;
    }
    
    private readonly Options _options;

    private readonly IRunContext _runContext;
    private readonly IContainer _container;
    
    private readonly IReadOnlyList<AppLayer.IDefinition> _layerDefinitions;
    private readonly List<AppLayer> _appLayers = [];
    private readonly List<ILifetimeScope> _layerScopes = [];

    private int _initializedLayerCount;

    private bool _isRunning;
    private bool _disposed;

    public Engine(Options options, IReadOnlyList<AppLayer.IDefinition> layerDefinitions)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(layerDefinitions);

        _options = options;
        _layerDefinitions = layerDefinitions;

        var window = new Window(options.Window);

        _runContext = window;

        var builder = new ContainerBuilder();

        builder.RegisterInstance(window)
            .As<IWindow>()
            .As<IGraphicsContext>()
            .As<IRunContext>()
            .SingleInstance();

        builder.RegisterInstance(_options)
            .AsSelf()
            .SingleInstance();

        foreach (var definition in _layerDefinitions)
        {
            definition.RegisterGlobal(builder);
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
            _runContext.Dispose();

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
            _runContext.Initialize();
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
        foreach (var definition in _layerDefinitions)
        {
            var scope = _container.BeginLifetimeScope(builder =>
            {
                definition.RegisterLocal(builder);
                builder.RegisterType(definition.LayerType);
            });

            try
            {
                var layer = (AppLayer)scope.Resolve(definition.LayerType);
                _layerScopes.Add(scope);
                _appLayers.Add(layer);
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

        while (_runContext.IsRunning)
        {
            var frameStartTicks = stopwatch.ElapsedTicks;

            var deltaTime =
                (double)(frameStartTicks - lastFrameTicks) /
                Stopwatch.Frequency;

            lastFrameTicks = frameStartTicks;

            deltaTime = Math.Min(deltaTime, 0.1);

            _runContext.PollEvents();

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
                // Ignore
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
        _appLayers.Clear();
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
        _runContext.Dispose();
    }
}
