namespace Vecxy.Engine;

public abstract class Component
{
    public Node Node { get; }
    
    public virtual void OnAwake() { }
    public virtual void OnStart() { }
    
    public virtual void OnEnable() { }
    public virtual void OnDisable() { }
    
    public virtual void OnUpdate(float deltaTime) { }
    public virtual void OnLateUpdate(float deltaTime) { }
        
    public virtual void OnDestroy() { }
}