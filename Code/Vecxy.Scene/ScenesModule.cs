using Autofac;
using Vecxy.Assets;
using Vecxy.Kernel;

namespace Vecxy.Scene;

public interface ISceneManager
{
    SceneInstance? ActiveScene { get; }

    IReadOnlyList<SceneInstance> LoadedScenes { get; }

    SceneInstance LoadScene<TScene>()
        where TScene : class, IScene;

    SceneInstance LoadSceneAdditive<TScene>()
        where TScene : class, IScene;

    void SetActiveScene(SceneInstance sceneInstance);

    void UnloadScene(SceneInstance sceneInstance);

    void UnloadScene<TScene>()
        where TScene : class, IScene;

    void UnloadActiveScene();
}

public sealed class ScenesModule(
    IEnumerable<ISceneSystem> systems,
    IConfigProvider config,
    ILifetimeScope scope) :
    IModule,
    IModule.IUpdatable,
    ISceneManager
{
    public sealed class Definition : AModuleDefinition<ScenesModule>
    {
        protected override IReadOnlyList<Type> Exports =>
        [
            typeof(ISceneManager)
        ];

        public override void RegisterGlobal(ContainerBuilder builder)
        {
            builder
                .RegisterType<ComponentInstantiator>()
                .As<IComponentInstantiator>()
                .SingleInstance();
        }

        protected override void RegisterModule(ContainerBuilder builder)
        {
            builder
                .RegisterType<ScenesModule>()
                .AsSelf()
                .SingleInstance();
        }
    }

    private readonly IReadOnlyList<ISceneSystem> _systems = systems.ToArray();

    private readonly List<SceneInstance> _loadedScenes = [];

    private readonly Dictionary<Type, SceneInstance> _sceneToSceneInstance = [];

    private readonly Dictionary<SceneInstance, ILifetimeScope> _sceneScopes = [];

    private SceneInstance? _activeScene;
    private ConfigRef<SkyboxConfig>? _skyboxConfig;

    private bool _initialized;
    private bool _disposed;

    public SceneInstance? ActiveScene => _activeScene;

    public IReadOnlyList<SceneInstance> LoadedScenes => _loadedScenes;

    public void OnInitialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_initialized)
            return;

        _skyboxConfig =
            config.LoadConfig<SkyboxConfig>("SkyBox/Skybox.yaml");

        _initialized = true;
    }

    public SceneInstance LoadScene<TScene>()
        where TScene : class, IScene
    {
        return LoadScene<TScene>(makeActive: true);
    }

    public SceneInstance LoadSceneAdditive<TScene>()
        where TScene : class, IScene
    {
        return LoadScene<TScene>(makeActive: false);
    }

    private SceneInstance LoadScene<TScene>(bool makeActive)
        where TScene : class, IScene
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_initialized)
        {
            throw new InvalidOperationException(
                "Scenes module must be initialized before loading scenes.");
        }

        var sceneType = typeof(TScene);

        if (_sceneToSceneInstance.TryGetValue(
                sceneType,
                out var existingInstance))
        {
            if (makeActive)
                SetActiveScene(existingInstance);

            return existingInstance;
        }

        var sceneScope = scope.BeginLifetimeScope();
        SceneInstance? sceneInstance = null;

        try
        {
            var scene = sceneScope.Resolve<TScene>();

            sceneInstance = new SceneInstance(scene);

            foreach (var system in _systems)
                sceneInstance.RegisterSystem(system);

            if (_skyboxConfig?.TryGetValue(out var skyboxConfig) == true &&
                skyboxConfig is not null)
                sceneInstance.Lighting.Skybox.ApplyConfig(skyboxConfig);

            sceneInstance.Load();

            _sceneToSceneInstance.Add(sceneType, sceneInstance);
            _sceneScopes.Add(sceneInstance, sceneScope);
            _loadedScenes.Add(sceneInstance);
            
            if (makeActive)
                SetActiveScene(sceneInstance);

            return sceneInstance;
        }
        catch
        {
            if (sceneInstance is not null)
            {
                try
                {
                    sceneInstance.Unload();
                }
                catch
                {
                }
            }

            sceneScope.Dispose();

            throw;
        }
    }

    public void SetActiveScene(SceneInstance sceneInstance)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(sceneInstance);

        if (!_loadedScenes.Contains(sceneInstance))
        {
            throw new InvalidOperationException(
                "The scene must be loaded before it can become active.");
        }

        if (ReferenceEquals(_activeScene, sceneInstance))
            return;

        _activeScene?.Deactivate();

        _activeScene = sceneInstance;
        _activeScene.Activate();
    }

    public void UnloadScene(SceneInstance sceneInstance)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(sceneInstance);

        if (!_loadedScenes.Contains(sceneInstance))
            return;

        if (ReferenceEquals(_activeScene, sceneInstance))
        {
            sceneInstance.Deactivate();
            _activeScene = null;
        }

        var sceneType = sceneInstance.Scene.GetType();

        try
        {
            sceneInstance.Unload();
        }
        finally
        {
            _loadedScenes.Remove(sceneInstance);
            _sceneToSceneInstance.Remove(sceneType);

            if (_sceneScopes.Remove(sceneInstance, out var sceneScope))
                sceneScope.Dispose();
        }
    }

    public void UnloadScene<TScene>()
        where TScene : class, IScene
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_sceneToSceneInstance.TryGetValue(
                typeof(TScene),
                out var sceneInstance))
        {
            return;
        }

        UnloadScene(sceneInstance);
    }

    public void UnloadActiveScene()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_activeScene is null)
            return;

        UnloadScene(_activeScene);
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

        foreach (var sceneInstance in _loadedScenes.ToArray().Reverse())
        {
            try
            {
                UnloadScene(sceneInstance);
            }
            catch
            {
            }
        }

        foreach (var sceneScope in _sceneScopes.Values)
            sceneScope.Dispose();

        _activeScene = null;

        _loadedScenes.Clear();
        _sceneToSceneInstance.Clear();
        _sceneScopes.Clear();

        _skyboxConfig?.Dispose();
        _skyboxConfig = null;

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
