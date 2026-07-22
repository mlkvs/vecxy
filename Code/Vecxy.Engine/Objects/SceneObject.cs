namespace Vecxy.Engine.Objects;

public class SceneObject
{
    private readonly List<SceneObject> _children = [];
    private readonly List<Script> _scripts = [];
    private bool _started;

    public string Name { get; set; }
    public bool IsActive { get; set; } = true;
    public Transform Transform { get; } = new();
    public SceneObject? Parent { get; private set; }
    public IReadOnlyList<SceneObject> Children => _children;
    public IReadOnlyList<Script> Scripts => _scripts;

    public SceneObject(string name = "New Object") => Name = name;

    public void AddChild(SceneObject child)
    {
        ArgumentNullException.ThrowIfNull(child);
        child.Parent?._children.Remove(child);
        child.Parent = this;
        child.Transform.SetParent(Transform);
        if (!_children.Contains(child)) _children.Add(child);
        if (_started) child.Start();
    }

    internal void ClearChildren()
    {
        foreach (var child in _children)
        {
            child.Destroy();
            child.Transform.SetParent(null);
        }
        _children.Clear();
    }

    public T AddScript<T>() where T : Script, new()
    {
        return AddScript(new T());
    }

    public T AddScript<T>(T script) where T : Script
    {
        ArgumentNullException.ThrowIfNull(script);
        if (script.SceneObject is not null) throw new InvalidOperationException("Script is already attached.");
        script.SceneObject = this;
        _scripts.Add(script);
        if (_started) script.OnStart();
        return script;
    }

    public T? GetScript<T>() where T : Script => _scripts.OfType<T>().FirstOrDefault();

    internal void Start()
    {
        if (_started) return;
        _started = true;
        var scriptCount = _scripts.Count;
        for (var i = 0; i < scriptCount; i++) _scripts[i].OnStart();
        foreach (var child in _children) child.Start();
    }

    internal void Update(float deltaTime)
    {
        if (!IsActive) return;
        var scriptCount = _scripts.Count;
        for (var i = 0; i < scriptCount; i++) _scripts[i].OnUpdate(deltaTime);
        foreach (var child in _children) child.Update(deltaTime);
    }

    internal void Destroy()
    {
        foreach (var child in _children) child.Destroy();
        foreach (var script in _scripts) script.OnDestroy();
    }
}
