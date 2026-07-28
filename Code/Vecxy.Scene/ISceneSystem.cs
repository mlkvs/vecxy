namespace Vecxy.Scene;

public interface ISceneSystem
{
    void OnSceneAttached(Scene scene);
    void OnSceneDetached(Scene scene);

    void OnObjectAdded(SceneObject sceneObject);
    void OnObjectRemoved(SceneObject sceneObject);

    void OnComponentAdded(SceneObject sceneObject, AComponent component);
    void OnComponentRemoved(SceneObject sceneObject, AComponent component);

    void Update(Scene scene, float deltaTime);
}

public abstract class ASceneSystem : ISceneSystem
{
    public virtual void OnSceneAttached(Scene scene) { }
    public virtual void OnSceneDetached(Scene scene) { }
    public virtual void OnObjectAdded(SceneObject sceneObject) { }
    public virtual void OnObjectRemoved(SceneObject sceneObject) { }

    public virtual void OnComponentAdded(
        SceneObject sceneObject,
        AComponent component) { }

    public virtual void OnComponentRemoved(
        SceneObject sceneObject,
        AComponent component) { }

    public virtual void Update(Scene scene, float deltaTime) { }
}
