namespace Vecxy.Engine.Objects;

public abstract class Script
{
    public SceneObject SceneObject { get; internal set; } = null!;
    public Transform Transform => SceneObject.Transform;
    public virtual void OnStart() { }
    public virtual void OnUpdate(float deltaTime) { }
    public virtual void OnDestroy() { }
}
