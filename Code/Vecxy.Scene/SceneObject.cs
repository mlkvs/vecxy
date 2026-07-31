namespace Vecxy.Scene;

public sealed class SceneObject
{
    private static int Count { get; set; } = 0;
    
    private readonly List<AComponent> _components = [];
    private readonly List<SceneObject> _children = [];

    private bool _active;
    private bool _enabled = true;
    private bool _destroying;
    private bool _destroyed;

    public readonly int Id;

    public SceneInstance SceneInstance { get; }

    public string Name { get; set; }

    public Transform Transform { get; }

    public SceneObject? Parent { get; private set; }

    public IReadOnlyList<SceneObject> Children => _children;

    public bool IsActive => _active;

    public bool IsDestroying => _destroying;

    public bool IsDestroyed => _destroyed;

    public IReadOnlyList<AComponent> Components => _components;

    public bool IsStatic { get; set; }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            ThrowIfDestroyed();

            if (_enabled == value)
                return;

            _enabled = value;
            RefreshActiveState();
        }
    }

    internal SceneObject(SceneInstance sceneInstance, string name, bool isStatic = false)
    {
        Id = Count++;
        IsStatic = isStatic;
        
        SceneInstance = sceneInstance;
        Name = name;

        Transform = new Transform();
        Transform.Attach(this);
        _components.Add(Transform);
    }

    public T AddComponent<T>() where T : AComponent, new()
    {
        return AddComponent(new T());
    }

    public T AddComponent<T>(T component) where T : AComponent
    {
        ThrowIfDestroyed();
        ArgumentNullException.ThrowIfNull(component);

        if (component.SceneObject is not null)
            throw new InvalidOperationException(
                "Component is already attached to a scene object.");

        if (component.GetType().IsDefined(
                typeof(SingleComponentAttribute),
                inherit: true) &&
            _components.Any(existing =>
                existing.GetType() == component.GetType()))
        {
            throw new InvalidOperationException(
                $"Scene object already contains component " +
                $"'{component.GetType().Name}'.");
        }

        component.Attach(this);
        _components.Add(component);

        if (_active)
            component.Activate(ownerEnabled: true);
        
        foreach (var system in SceneInstance.Systems)
        {
            system.OnComponentAdded(this, component);
        }

        return component;
    }

    public SceneObject CreateChild(
        string name = "SceneObject",
        bool? isStatic = null)
    {
        ThrowIfDestroyed();
        var child = SceneInstance.CreateObject(name, isStatic ?? IsStatic);
        child.SetParent(this, worldPositionStays: false);
        return child;
    }

    public void SetParent(
        SceneObject? parent,
        bool worldPositionStays = true)
    {
        ThrowIfDestroyed();

        if (ReferenceEquals(Parent, parent))
            return;

        if (parent is not null)
        {
            parent.ThrowIfDestroyed();

            if (!ReferenceEquals(parent.SceneInstance, SceneInstance))
                throw new InvalidOperationException(
                    "Parent belongs to another scene.");

            for (var ancestor = parent;
                 ancestor is not null;
                 ancestor = ancestor.Parent)
            {
                if (ReferenceEquals(ancestor, this))
                    throw new InvalidOperationException(
                        "Scene object hierarchy cannot contain a cycle.");
            }
        }

        var worldMatrix = Transform.WorldMatrix;

        Parent?._children.Remove(this);
        Parent = parent;
        Parent?._children.Add(this);

        Transform.MarkWorldDirty();

        if (worldPositionStays)
            Transform.WorldMatrix = worldMatrix;

        RefreshActiveState();
    }

    public T? GetComponent<T>() where T : AComponent
    {
        ThrowIfDestroyed();

        for (int index = 0, count = _components.Count;
             index < count;
             ++index)
        {
            if (_components[index] is T component)
                return component;
        }

        return null;
    }

    public bool TryGetComponent<T>(out T? component)
        where T : AComponent
    {
        component = GetComponent<T>();
        return component is not null;
    }

    public IEnumerable<T> GetComponents<T>() where T : AComponent
    {
        ThrowIfDestroyed();

        for (int index = 0, count = _components.Count;
             index < count;
             ++index)
        {
            if (_components[index] is T component)
                yield return component;
        }
    }

    public void GetComponents<T>(List<T> result)
        where T : AComponent
    {
        ThrowIfDestroyed();
        ArgumentNullException.ThrowIfNull(result);

        for (int index = 0, count = _components.Count;
             index < count;
             ++index)
        {
            if (_components[index] is T component)
                result.Add(component);
        }
    }

    public bool HasComponent<T>() where T : AComponent
    {
        return GetComponent<T>() is not null;
    }

    public IEnumerable<SceneObject> EnumerateHierarchy(
        bool includeSelf = true)
    {
        ThrowIfDestroyed();

        if (includeSelf)
            yield return this;

        foreach (var child in _children)
        {
            yield return child;

            foreach (var nested in child.EnumerateHierarchy(includeSelf: false))
                yield return nested;
        }
    }

    public IEnumerable<T> GetComponentsInChildren<T>(
        bool includeSelf = true)
        where T : AComponent
    {
        foreach (var sceneObject in EnumerateHierarchy(includeSelf))
        {
            foreach (var component in sceneObject.GetComponents<T>())
                yield return component;
        }
    }

    public T? GetComponentInChildren<T>(
        bool includeSelf = true)
        where T : AComponent
    {
        return GetComponentsInChildren<T>(includeSelf)
            .FirstOrDefault();
    }

    public SceneObject? FindChild(
        string name,
        bool recursive = true)
    {
        ThrowIfDestroyed();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (recursive)
        {
            return EnumerateHierarchy(includeSelf: false)
                .FirstOrDefault(
                    child => string.Equals(
                        child.Name,
                        name,
                        StringComparison.Ordinal));
        }

        return _children.FirstOrDefault(
            child => string.Equals(
                child.Name,
                name,
                StringComparison.Ordinal));
    }

    public bool RemoveComponent<T>() where T : AComponent
    {
        ThrowIfDestroyed();

        for (int index = 0, count = _components.Count;
             index < count;
             ++index)
        {
            if (_components[index] is not T component)
                continue;

            if (ReferenceEquals(component, Transform))
                throw new InvalidOperationException(
                    "Transform cannot be removed.");

            foreach (var system in SceneInstance.Systems)
            {
                system.OnComponentRemoved(this, component);
            }

            _components.RemoveAt(index);
            component.Destroy();

            return true;
        }

        return false;
    }

    public bool RemoveComponent(AComponent component)
    {
        ThrowIfDestroyed();
        ArgumentNullException.ThrowIfNull(component);

        var index = _components.IndexOf(component);
        if (index < 0)
            return false;

        if (ReferenceEquals(component, Transform))
            throw new InvalidOperationException(
                "Transform cannot be removed.");

        foreach (var system in SceneInstance.Systems)
        {
            system.OnComponentRemoved(this, component);
        }

        _components.RemoveAt(index);
        component.Destroy();
        return true;
    }

    public void Destroy()
    {
        if (_destroyed || _destroying)
            return;

        SceneInstance.DestroyObject(this);
    }

    internal void Activate()
    {
        if (_destroyed)
            return;

        RefreshActiveState();
    }

    internal void Update(float deltaTime)
    {
        if (!_active || _destroyed)
            return;

        for (int index = 0, count = _components.Count;
             index < count;
             ++index)
        {
            _components[index].ProcessUpdate(deltaTime);
        }
    }

    internal void LateUpdate(float deltaTime)
    {
        if (!_active || _destroyed)
            return;

        for (int index = 0, count = _components.Count;
             index < count;
             ++index)
        {
            _components[index].ProcessLateUpdate(deltaTime);
        }
    }

    internal void FixedUpdate(float deltaTime)
    {
        if (!_active || _destroyed)
            return;

        for (int index = 0, count = _components.Count;
             index < count;
             ++index)
        {
            _components[index].ProcessFixedUpdate(deltaTime);
        }
    }

    internal void Deactivate()
    {
        SetActiveRecursively(false);
    }

    internal void MarkForDestroy()
    {
        _destroying = true;

        foreach (var child in _children)
            child.MarkForDestroy();
    }

    internal void DestroyImmediately()
    {
        if (_destroyed)
            return;

        for (var index = _components.Count - 1;
             index >= 0;
             --index)
        {
            var component = _components[index];
            
            foreach (var system in SceneInstance.Systems)
            {
                system.OnComponentRemoved(this, component);
            }

            component.Destroy();
        }

        _components.Clear();

        Parent?._children.Remove(this);
        Parent = null;
        _children.Clear();

        _active = false;
        _destroying = false;
        _destroyed = true;
    }

    private void ThrowIfDestroyed()
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
    }

    private void RefreshActiveState()
    {
        var shouldBeActive =
            SceneInstance.IsActive &&
            _enabled &&
            (Parent?.IsActive ?? true);

        SetActiveRecursively(shouldBeActive);
    }

    private void SetActiveRecursively(bool active)
    {
        if (_destroyed)
            return;

        if (_active != active)
        {
            _active = active;

            if (_active)
            {
                for (int index = 0, count = _components.Count;
                     index < count;
                     ++index)
                {
                    _components[index].Activate(ownerEnabled: true);
                }
            }
            else
            {
                for (var index = _components.Count - 1;
                     index >= 0;
                     --index)
                {
                    _components[index].Deactivate();
                }
            }
        }

        foreach (var child in _children)
            child.SetActiveRecursively(_active && child._enabled);
    }
}
