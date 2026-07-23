using Autofac;
using Vecxy.Kernel;

namespace Vecxy.Engine;

public sealed class EngineLayer : AppLayer
{
    private readonly List<IModule> _modules;
    private int _initializedModuleCount;

    public EngineLayer(IEnumerable<IModule> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);

        _modules = modules.ToList();
    }

    public override void OnLocalBindings(ContainerBuilder builder)
    {
        foreach (var module in _modules)
        {
            builder.RegisterInstance(module)
                .As(module.GetType())
                .AsImplementedInterfaces()
                .ExternallyOwned();
        }
    }

    internal override void OnScopeCreated(ILifetimeScope scope)
    {
        foreach (var module in _modules)
        {
            scope.InjectProperties(module);
        }
    }

    public override void OnInitialize()
    {
        foreach (var module in _modules)
        {
            module.OnInitialize();
            _initializedModuleCount++;
        }
    }

    public override void OnUpdate(float deltaTime)
    {
        foreach (var module in _modules)
        {
            if (module is IModule.IUpdatable updatable)
            {
                updatable.OnUpdate(deltaTime);
            }
        }
    }

    public override void OnRender()
    {
        foreach (var module in _modules)
        {
            if (module is IModule.IRenderable renderable)
            {
                renderable.OnRender();
            }
        }
    }

    public override void OnUnload()
    {
        for (var index = _initializedModuleCount - 1; index >= 0; index--)
        {
            try
            {
                _modules[index].OnShutdown();
            }
            catch
            {
            }
        }

        _initializedModuleCount = 0;

        for (var index = _modules.Count - 1; index >= 0; index--)
        {
            _modules[index].Dispose();
        }
    }
}