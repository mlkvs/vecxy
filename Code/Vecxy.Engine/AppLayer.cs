using Autofac;

namespace Vecxy.Engine;

public abstract class AppLayer
{
    public abstract void OnGlobalBindings(ContainerBuilder builder);
    public abstract void OnLocalBindings(ContainerBuilder builder);
    
    public abstract void OnInitialize();
    public abstract void OnTick(float dt);
}