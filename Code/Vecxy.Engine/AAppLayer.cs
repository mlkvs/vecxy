using Vecxy.Kernel;

namespace Vecxy.Engine;

public abstract class AAppLayer
{
    public interface IDefinition : Vecxy.Kernel.IDefinition
    {
        Type? LayerType { get; }
    }

    public abstract class ADefinition : Vecxy.Kernel.ADefinition, IDefinition
    {
        public virtual Type? LayerType => null;
    }

    public abstract class ADefinition<TLayer> : ADefinition
        where TLayer : AAppLayer
    {
        public sealed override Type LayerType => typeof(TLayer);
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
