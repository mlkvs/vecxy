namespace Vecxy.Scene;

public interface ISceneSystem
{
    void OnSceneAttached(SceneInstance sceneInstance);
    void OnSceneDetached(SceneInstance sceneInstance);

    void OnObjectAdded(SceneObject sceneObject);
    void OnObjectRemoved(SceneObject sceneObject);

    void OnComponentAdded(SceneObject sceneObject, AComponent component);
    void OnComponentRemoved(SceneObject sceneObject, AComponent component);

    void Update(SceneInstance sceneInstance, float deltaTime);
}

public abstract class ASceneSystem : ISceneSystem
{
    public virtual void OnSceneAttached(SceneInstance sceneInstance) { }
    public virtual void OnSceneDetached(SceneInstance sceneInstance) { }
    public virtual void OnObjectAdded(SceneObject sceneObject) { }
    public virtual void OnObjectRemoved(SceneObject sceneObject) { }

    public virtual void OnComponentAdded(
        SceneObject sceneObject,
        AComponent component) { }

    public virtual void OnComponentRemoved(
        SceneObject sceneObject,
        AComponent component) { }

    public virtual void Update(SceneInstance sceneInstance, float deltaTime) { }
}
