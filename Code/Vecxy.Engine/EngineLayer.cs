using Autofac;
using JetBrains.Annotations;
using Vecxy.Kernel;
using Vecxy.Rendering;

namespace Vecxy.Engine;

[UsedImplicitly]
public sealed class EngineLayer(IEnumerable<IModule> modules): AppLayer
{
    public sealed class Definition : Definition<EngineLayer>
    {
        public override void RegisterLocal(ContainerBuilder builder)
        {
            builder.RegisterType<RenderingModule>()
                .As<IModule>()
                .SingleInstance();
        }
    }
    
    public override void OnInitialize()
    {
        foreach (var module in modules)
        {
            module.OnInitialize();
        }
    }

    public override void OnUpdate(float deltaTime)
    {
        foreach (var module in modules)
        {
            if (module is IModule.IUpdatable updatable)
            {
                updatable.OnUpdate(deltaTime);
            }
        }
    }

    public override void OnRender()
    {
        foreach (var module in modules)
        {
            if (module is IModule.IRenderable renderable)
            {
                renderable.OnRender();
            }
        }
    }

    public override void OnUnload()
    {
        foreach (var module in modules)
        {
            try
            {
                module.OnShutdown();
            }
            catch
            {
                // Ignore
            }
        }
    }
}
