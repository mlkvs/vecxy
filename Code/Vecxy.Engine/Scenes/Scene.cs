using Vecxy.Assets;
using Vecxy.Engine.Objects;

namespace Vecxy.Engine.Scenes;

public enum SceneMode : byte { Single, Additive }

public sealed class Scene : IDisposable
{
    private readonly List<SceneObject> _rootObjects = [];

    public string Name { get; set; }
    public SceneMode Mode { get; set; }
    public IReadOnlyList<SceneObject> RootObjects => _rootObjects;
    public IEnumerable<Transform> RootTransforms => _rootObjects.Select(x => x.Transform);

    public Scene(string name, SceneMode mode = SceneMode.Single)
    {
        Name = name;
        Mode = mode;
    }

    public SceneObject CreateObject(string name = "New Object", SceneObject? parent = null)
    {
        var result = new SceneObject(name);
        if (parent is null) _rootObjects.Add(result);
        else parent.AddChild(result);
        return result;
    }

    public SceneObject Instantiate(ModelAsset model, string? name = null, SceneObject? parent = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        var root = CreateObject(name ?? model.Name, parent);
        root.AddScript(new ModelInstance(model));
        return root;
    }

    public bool Remove(SceneObject sceneObject)
    {
        if (!_rootObjects.Remove(sceneObject)) return false;
        sceneObject.Destroy();
        return true;
    }

    internal IEnumerable<SceneObject> Traverse() => _rootObjects.SelectMany(Traverse);
    public IEnumerable<SceneObject> Objects => _rootObjects.SelectMany(Traverse);

    private static IEnumerable<SceneObject> Traverse(SceneObject root)
    {
        yield return root;
        foreach (var child in root.Children)
        foreach (var descendant in Traverse(child)) yield return descendant;
    }

    internal void Start() { foreach (var root in _rootObjects) root.Start(); }
    internal void Update(float deltaTime) { foreach (var root in _rootObjects) root.Update(deltaTime); }

    public void Dispose()
    {
        foreach (var root in _rootObjects) root.Destroy();
        _rootObjects.Clear();
    }
}
