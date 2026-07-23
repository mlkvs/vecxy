using Autofac;

namespace Vecxy.Engine;

public abstract class AppLayer
{
    public interface IDefinition
    {
        Type LayerType { get; }

        void RegisterGlobal(ContainerBuilder builder);
        
        void RegisterLocal(ContainerBuilder builder);
    }

    public abstract class Definition<TLayer> : IDefinition where TLayer : AppLayer
    {
        public Type LayerType => typeof(TLayer);

        public virtual void RegisterGlobal(ContainerBuilder builder) { }
        
        public virtual void RegisterLocal(ContainerBuilder builder) { }
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
