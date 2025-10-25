using Vecxy.Diagnostics;

namespace Vecxy.Engine;

public interface ISceneManager
{
    public Scene? CurrentScene { get; }

    public Scene CreateScene(string name);
    public void ActivateScene(Scene scene);
    public void Update(float deltaTime);
}

public class Scene(string name)
{
    public bool IsLoaded { get; private set; }

    public event Action<Scene> OnLoaded;

    public string Name => name;
    public List<GameObject> RootGameObjects { get; } = [];

    public void Load()
    {
        IsLoaded = true;

        OnLoaded?.Invoke(this);
    }

    public void Update(float deltaTime)
    {
        for (int index = 0, count = RootGameObjects.Count; index < count; ++index)
        {
            var gameObject = RootGameObjects[index];

            gameObject.Update(deltaTime);
        }
    }
}

public class SceneManager : ISceneManager
{
    public Scene? CurrentScene { get; private set; }

    public Scene CreateScene(string name)
    {
        var scene = new Scene(name);

        scene.OnLoaded += OnSceneLoaded;

        return scene;
    }

    public void Update(float deltaTime)
    {
        CurrentScene?.Update(deltaTime);
    }

    public void ActivateScene(Scene scene)
    {
        if (!scene.IsLoaded)
        {
            Logger.Warning($"Scene '{scene.Name}' not loaded!");
            
            return;
        }
        
        CurrentScene = scene;
    }

    private void OnSceneLoaded(Scene scene)
    {
    }
}