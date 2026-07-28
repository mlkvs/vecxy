namespace Vecxy.Scene;

public interface ISceneSystem
{
    void OnObjectAdded(SceneObject sceneObject);
    void OnObjectRemoved(SceneObject sceneObject);

    void OnComponentAdded(SceneObject sceneObject, AComponent component);
    void OnComponentRemoved(SceneObject sceneObject, AComponent component);
    void OnComponentChanged(SceneObject sceneObject, AComponent component);

    void Update(float deltaTime);
}