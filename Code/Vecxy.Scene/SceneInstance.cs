namespace Vecxy.Scene;

/*  TODO:
 *  - Mode: Single / Additive
 *  - Load / Unload Resources
 */
public sealed class SceneInstance
{
    private readonly List<SceneObject> _objects = [];
    private readonly List<SceneObject> _pendingDestroy = [];
    private readonly List<ISceneSystem> _systems = [];

    private bool _active;
    private bool _updating;
    private bool _systemsDetached;
    
    private bool _loaded;

    public IScene Scene { get; }

    public SceneLightingSettings Lighting { get; } = new();

    public bool IsActive => _active;

    public IReadOnlyList<SceneObject> Objects => _objects;

    public IEnumerable<SceneObject> RootObjects =>
        _objects.Where(sceneObject => sceneObject.Parent is null);
    
    public IEnumerable<ISceneSystem> Systems => _systems;

    internal SceneInstance(IScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        Scene = scene;
    }
    
    internal void Load()
    {
        if (_loaded)
            return;
    
        Scene.OnLoad(this);
        _loaded = true;
    }
    
    internal void Unload()
    {
        if (!_loaded)
            return;
    
        Deactivate();
    
        Scene.OnUnload(this);
    
        foreach (var sceneObject in RootObjects.ToArray().Reverse())
        {
            DestroyObjectImmediately(sceneObject);
        }
    
        FlushDestroyedObjects();
        DetachSystems();
    
        _loaded = false;
    }

    public void RegisterSystem(ISceneSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);

        if (_systemsDetached)
            throw new InvalidOperationException(
                "Cannot register a system after the scene has been detached.");

        if (_systems.Contains(system))
            return;

        _systems.Add(system);
        system.OnSceneAttached(this);

        foreach (var sceneObject in _objects)
        {
            system.OnObjectAdded(sceneObject);

            foreach (var component in sceneObject.Components)
                system.OnComponentAdded(sceneObject, component);
        }
    }

    public T? GetSystem<T>() where T : class, ISceneSystem
    {
        for (var index = 0; index < _systems.Count; ++index)
        {
            if (_systems[index] is T system)
                return system;
        }

        return null;
    }

    public bool TryGetSystem<T>(out T? system)
        where T : class, ISceneSystem
    {
        system = GetSystem<T>();
        return system is not null;
    }

    public SceneObject CreateObject(
        string name = "SceneObject",
        bool isStatic = false)
    {
        if (_systemsDetached)
            throw new ObjectDisposedException(nameof(SceneInstance));

        var sceneObject = new SceneObject(this, name, isStatic);

        _objects.Add(sceneObject);

        foreach (var system in Systems)
        {
            system.OnObjectAdded(sceneObject);

            foreach (var component in sceneObject.Components)
                system.OnComponentAdded(sceneObject, component);
        }

        if (_active)
            sceneObject.Activate();

        return sceneObject;
    }

    public void DestroyObject(SceneObject sceneObject)
    {
        ArgumentNullException.ThrowIfNull(sceneObject);

        if (!ReferenceEquals(sceneObject.SceneInstance, this))
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

        foreach (var sceneObject in RootObjects.ToArray())
            sceneObject.Activate();
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

            for (int index = 0, count = _systems.Count; index < count; ++index)
            {
                var system = _systems[index];

                system.Update(this, deltaTime);
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

        foreach (var sceneObject in RootObjects.ToArray().Reverse())
            sceneObject.Deactivate();

        FlushDestroyedObjects();
    }

    internal void ProcessFixedUpdate(float deltaTime)
    {
        if (!_active)
            return;

        for (int index = 0, count = _objects.Count; index < count; ++index)
        {
            var sceneObject = _objects[index];

            if (!sceneObject.IsDestroying)
                sceneObject.FixedUpdate(deltaTime);
        }
    }

    internal void DetachSystems()
    {
        if (_systemsDetached)
            return;

        Deactivate();

        for (var index = _systems.Count - 1; index >= 0; --index)
            _systems[index].OnSceneDetached(this);

        _systems.Clear();
        _systemsDetached = true;
    }

    private void FlushDestroyedObjects()
    {
        for (var index = _pendingDestroy.Count - 1; index >= 0; --index)
            DestroyObjectImmediately(_pendingDestroy[index]);

        _pendingDestroy.Clear();
    }

    private void DestroyObjectImmediately(SceneObject sceneObject)
    {
        if (sceneObject.IsDestroyed || !_objects.Contains(sceneObject))
            return;

        foreach (var child in sceneObject.Children.ToArray().Reverse())
            DestroyObjectImmediately(child);

        _objects.Remove(sceneObject);
        
        sceneObject.DestroyImmediately();
        
        foreach (var system in Systems)
        {
            system.OnObjectRemoved(sceneObject);
        }
    }
}
