using Autofac;

namespace Vecxy.Engine;

public abstract class AppLayer
{
    public virtual void OnGlobalBindings(ContainerBuilder builder) { }

    public virtual void OnLocalBindings(ContainerBuilder builder) { }
    
    public virtual void OnInitialize() { }
    public virtual void OnTick(float dt) { }
    public virtual void OnFrame() { }
}
