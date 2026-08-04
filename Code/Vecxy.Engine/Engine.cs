using System.Diagnostics;
using Autofac;
using Vecxy.Kernel;
using Vecxy.Rendering;

namespace Vecxy.Engine;

public sealed class Engine : IDisposable
{
    public sealed class Options
    {
        public IWindow.Options Window = new();
        public int TargetFrameRate { get; init; } = 60;
        public bool Headless { get; init; }
        public bool ShowSplashScreen { get; init; } = true;
        public string SplashScreenLogoPath { get; init; } = "Textures/Logo.jpeg";
        public Action<ContainerBuilder>? ConfigureServices { get; init; }
    }

    private readonly Options _options;

    private readonly IWindow _window;
    private readonly IContainer _container;
    private readonly string _assetsDirectory;
    private IEngineSplashScreen? _splashScreen;

    private readonly IReadOnlyList<AAppLayer.IDefinition> _layerDefinitions;
    private readonly IReadOnlyList<IReadOnlyList<Vecxy.Kernel.IDefinition>> _layerDefinitionTrees;
    private readonly List<AAppLayer> _appLayers = [];
    private readonly List<ILifetimeScope> _layerScopes = [];

    private int _initializedLayerCount;

    private bool _isRunning;
    private bool _disposed;

    public Engine(
        Options options,
        IReadOnlyList<AAppLayer.IDefinition> layerDefinitions,
        string assetsDirectory,
        IEngineSplashScreen? platformSplashScreen = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(layerDefinitions);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetsDirectory);

        _options = options;
        _assetsDirectory = assetsDirectory;
        _splashScreen = platformSplashScreen;
        _layerDefinitions = layerDefinitions;
        _layerDefinitionTrees = FlattenLayerDefinitions(layerDefinitions);

        _window = options.Headless
            ? new HeadlessWindow(options.Window)
            : new Window(options.Window);

        var builder = new ContainerBuilder();

        builder.RegisterInstance(_window)
            .As<IWindow>()
            .SingleInstance();

        builder.RegisterInstance(_options)
            .AsSelf()
            .SingleInstance();

        _options.ConfigureServices?.Invoke(builder);

        foreach (var definitions in _layerDefinitionTrees)
        {
            foreach (var definition in definitions)
            {
                definition.RegisterGlobal(builder);
            }
        }

        _container = builder.Build();
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
            InitializeSplashScreen();
            _splashScreen?.ReportProgress(0.12f);
            CreateLayerScopes();
            _splashScreen?.ReportProgress(0.28f);
            InitializeLayers();
            _splashScreen?.ReportProgress(0.94f);
            RunLoop();
        }
        finally
        {
            _isRunning = false;
        }
    }

    public void Stop()
    {
        if (_disposed)
            return;

        _window.Close();
    }

    private void CreateLayerScopes()
    {
        ILifetimeScope parentScope = _container;

        for (var index = 0; index < _layerDefinitions.Count; index++)
        {
            var definition = _layerDefinitions[index];
            var definitions = _layerDefinitionTrees[index];
            var layerType = definition.LayerType
                ?? throw new InvalidOperationException(
                    $"Top-level definition '{definition.GetType().FullName}' does not define a layer type.");

            var scope = parentScope.BeginLifetimeScope(builder =>
            {
                foreach (var current in definitions)
                {
                    current.RegisterLocal(builder);
                }

                builder.RegisterType(layerType);
            });

            try
            {
                var layer = (AAppLayer)scope.Resolve(layerType);
                _layerScopes.Add(scope);
                _appLayers.Add(layer);
                parentScope = scope;
            }
            catch
            {
                scope.Dispose();
                throw;
            }
        }
    }

    private static IReadOnlyList<IReadOnlyList<Vecxy.Kernel.IDefinition>> FlattenLayerDefinitions(
        IReadOnlyList<AAppLayer.IDefinition> roots)
    {
        var result = new List<IReadOnlyList<Vecxy.Kernel.IDefinition>>(roots.Count);
        var visited = new HashSet<Vecxy.Kernel.IDefinition>(ReferenceEqualityComparer.Instance);

        foreach (var root in roots)
        {
            if (root.LayerType is null)
            {
                throw new InvalidOperationException(
                    $"Top-level definition '{root.GetType().FullName}' must define a layer type.");
            }

            var flattened = new List<Vecxy.Kernel.IDefinition>();
            var active = new HashSet<Vecxy.Kernel.IDefinition>(ReferenceEqualityComparer.Instance);
            Visit(root, flattened, active, visited);
            result.Add(flattened);
        }

        return result;

        static void Visit(
            Vecxy.Kernel.IDefinition definition,
            ICollection<Vecxy.Kernel.IDefinition> flattened,
            ISet<Vecxy.Kernel.IDefinition> active,
            ISet<Vecxy.Kernel.IDefinition> visited)
        {
            if (!active.Add(definition))
            {
                throw new InvalidOperationException(
                    $"Definition cycle detected at '{definition.GetType().FullName}'.");
            }

            if (!visited.Add(definition))
            {
                throw new InvalidOperationException(
                    $"Definition instance '{definition.GetType().FullName}' is used more than once.");
            }

            flattened.Add(definition);

            foreach (var child in definition.Children)
            {
                if (child is null)
                {
                    throw new InvalidOperationException(
                        $"Definition '{definition.GetType().FullName}' contains a null child.");
                }

                Visit(child, flattened, active, visited);
            }

            active.Remove(definition);
        }
    }

    private void InitializeLayers()
    {
        for (var index = 0; index < _appLayers.Count; index++)
        {
            var layer = _appLayers[index];
            layer.OnInitialize();
            _initializedLayerCount++;

            var layerProgress = (index + 1.0f) / Math.Max(1, _appLayers.Count);
            _splashScreen?.ReportProgress(0.28f + 0.62f * layerProgress);
        }
    }

    private void InitializeSplashScreen()
    {
        if (_options.Headless || !_options.ShowSplashScreen)
        {
            _splashScreen?.Dismiss();
            _splashScreen?.Dispose();
            _splashScreen = null;
            return;
        }

#if !ANDROID
        _splashScreen ??= new DesktopSplashScreen(
            _window,
            Path.Combine(_assetsDirectory, _options.SplashScreenLogoPath));
#endif
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

            _window.PollEvents();

            Update((float)deltaTime);
            _splashScreen?.PrepareForFirstFrame();
            Render();
            DismissSplashScreen();

            WaitForNextFrame(stopwatch, frameStartTicks, targetTicksPerFrame);
        }
    }

    private void DismissSplashScreen()
    {
        if (_splashScreen is null)
            return;

        var splashScreen = _splashScreen;
        _splashScreen = null;
        splashScreen.Dismiss();
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
        DismissSplashScreen();

        UnloadLayers();
        DisposeLayerScopes();

        _container.Dispose();
        _window.Dispose();
    }
}
