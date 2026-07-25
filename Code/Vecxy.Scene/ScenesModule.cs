using Autofac;
using Vecxy.Kernel;

namespace Vecxy.Scene;

public interface ISceneManager
{
    Scene? ActiveScene { get; }

    void SetActiveScene(Scene scene);
    void UnloadActiveScene();
}

public sealed class ScenesModule : 
    IModule, 
    IModule.IUpdatable,
    ISceneManager
{
    public sealed class Definition : AModuleDefinition<ScenesModule>
    {
        protected override IReadOnlyList<Type> Exports => [typeof(ISceneManager)];

        protected override void RegisterModule(ContainerBuilder builder)
        {
            builder
                .RegisterType<ScenesModule>()
                .AsSelf()
                .SingleInstance();
        }
    }

    private Scene? _activeScene;
    private bool _initialized;
    private bool _disposed;

    public Scene? ActiveScene => _activeScene;

    public void OnInitialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_initialized)
            return;

        _initialized = true;
        _activeScene?.Activate();
    }

    public void SetActiveScene(Scene scene)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(scene);

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

        _activeScene?.Deactivate();
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

        _activeScene?.Deactivate();
        _activeScene = null;

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
