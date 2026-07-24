namespace Vecxy.Scene;

public sealed class Scene
{
    private readonly List<SceneObject> _objects = [];
    private readonly List<SceneObject> _pendingDestroy = [];

    private bool _active;
    private bool _updating;

    public string Name { get; }

    public bool IsActive => _active;

    public IReadOnlyList<SceneObject> Objects => _objects;

    public Scene(string name = "Scene")
    {
        Name = name;
    }

    public SceneObject CreateObject(string name = "SceneObject")
    {
        var sceneObject = new SceneObject(this, name);

        _objects.Add(sceneObject);

        if (_active)
            sceneObject.Activate();

        return sceneObject;
    }

    public void DestroyObject(SceneObject sceneObject)
    {
        ArgumentNullException.ThrowIfNull(sceneObject);

        if (!ReferenceEquals(sceneObject.Scene, this))
            throw new InvalidOperationException("Scene object belongs to another scene.");

        if (sceneObject.IsDestroyed || sceneObject.IsDestroying)
            return;

        sceneObject.MarkForDestroy();

        if (_updating)
        {
            _pendingDestroy.Add(sceneObject);
            return;
        }

        DestroyObjectImmediately(sceneObject);
    }

    internal void Activate()
    {
        if (_active)
            return;

        _active = true;

        for (int index = 0, count = _objects.Count; index < count; ++index)
            _objects[index].Activate();
    }

    internal void Update(float deltaTime)
    {
        if (!_active)
            return;

        _updating = true;

        try
        {
            for (int index = 0, count = _objects.Count; index < count; ++index)
            {
                var sceneObject = _objects[index];

                if (!sceneObject.IsDestroying)
                    sceneObject.Update(deltaTime);
            }

            for (int index = 0, count = _objects.Count; index < count; ++index)
            {
                var sceneObject = _objects[index];

                if (!sceneObject.IsDestroying)
                    sceneObject.LateUpdate(deltaTime);
            }
        }
        finally
        {
            _updating = false;
            FlushDestroyedObjects();
        }
    }

    internal void Deactivate()
    {
        if (!_active)
            return;

        _active = false;

        for (var index = _objects.Count - 1; index >= 0; --index)
            _objects[index].Deactivate();

        FlushDestroyedObjects();
    }

    private void FlushDestroyedObjects()
    {
        for (var index = _pendingDestroy.Count - 1; index >= 0; --index)
            DestroyObjectImmediately(_pendingDestroy[index]);

        _pendingDestroy.Clear();
    }

    private void DestroyObjectImmediately(SceneObject sceneObject)
    {
        if (!_objects.Remove(sceneObject))
            return;

        sceneObject.DestroyImmediately();
    }
}