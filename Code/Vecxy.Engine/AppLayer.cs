using Autofac;

namespace Vecxy.Engine;

public abstract class AppLayer
{
    public virtual void OnGlobalBindings(ContainerBuilder builder)
    {
    }

    public virtual void OnLocalBindings(ContainerBuilder builder)
    {
    }

    internal virtual void OnScopeCreated(ILifetimeScope scope)
    {
    }

    public virtual void OnInitialize()
    {
    }

    public virtual void OnUpdate(float deltaTime)
    {
    }

    public virtual void OnRender()
    {
    }

    public virtual void OnUnload()
    {
    }
}