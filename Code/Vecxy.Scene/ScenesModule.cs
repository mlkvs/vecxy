using Autofac;
using Vecxy.Kernel;

namespace Vecxy.Scene;

public interface ISceneManager
{
    Scene? ActiveScene { get; }
    IReadOnlyList<Scene> LoadedScenes { get; }

    void SetActiveScene(Scene scene);
    void UnloadActiveScene();
}

public interface ISceneFactory
{
    Scene Create();
}

public sealed class ScenesModule(IEnumerable<ISceneSystem> systems) : 
    IModule, 
    IModule.IUpdatable,
    ISceneManager,
    ISceneFactory
{
    public sealed class Definition : AModuleDefinition<ScenesModule>
    {
        protected override IReadOnlyList<Type> Exports => [typeof(ISceneManager), typeof(ISceneFactory)];

        protected override void RegisterModule(ContainerBuilder builder)
        {
            builder
                .RegisterType<ScenesModule>()
                .AsSelf()
                .SingleInstance();
        }
    }

    private readonly List<Scene> _loadedScenes = [];
    private Scene? _activeScene;
    private bool _initialized;
    private bool _disposed;

    public Scene? ActiveScene => _activeScene;
    public IReadOnlyList<Scene> LoadedScenes => _loadedScenes;

    public void OnInitialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_initialized)
            return;

        _initialized = true;
        _activeScene?.Activate();
    }
    
    public Scene Create()
    {
        var scene = new Scene();

        foreach (var system in systems)
        {
            scene.RegisterSystem(system);
        }

        return scene; 
    }

    public void SetActiveScene(Scene scene)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(scene);

        if (!_loadedScenes.Contains(scene))
            _loadedScenes.Add(scene);

        if (ReferenceEquals(_activeScene, scene))
            return;

        _activeScene?.Deactivate();

        _activeScene = scene;

        if (_initialized)
            _activeScene.Activate();
    }

    public void UnloadActiveScene()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_activeScene is not null)
        {
            _activeScene.Deactivate();
            _loadedScenes.Remove(_activeScene);
        }

        _activeScene = null;
    }

    public void OnUpdate(float deltaTime)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_initialized)
            return;

        _activeScene?.Update(deltaTime);
    }

    public void OnShutdown()
    {
        if (!_initialized)
            return;

        foreach (var scene in _loadedScenes.ToArray().Reverse())
        {
            if (ReferenceEquals(scene, _activeScene))
                scene.Deactivate();
        }

        _activeScene = null;
        _loadedScenes.Clear();

        _initialized = false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        OnShutdown();

        _disposed = true;
    }
}
