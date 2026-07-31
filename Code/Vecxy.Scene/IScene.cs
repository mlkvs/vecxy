namespace Vecxy.Scene;

public interface IScene
{
    void OnLoad(SceneInstance scene) { }
    void OnUnload(SceneInstance scene) { }
}