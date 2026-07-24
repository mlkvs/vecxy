namespace Vecxy.Scene;

public sealed class SceneObject
{
    private readonly List<Component> _components = [];

    private bool _active;
    private bool _enabled = true;
    private bool _destroying;
    private bool _destroyed;

    public Scene Scene { get; }

    public string Name { get; set; }

    public Transform Transform { get; }

    public bool IsActive => _active;

    public bool IsDestroying => _destroying;

    public bool IsDestroyed => _destroyed;

    public IReadOnlyList<Component> Components => _components;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            ThrowIfDestroyed();

            if (_enabled == value)
                return;

            _enabled = value;

            if (!_active)
                return;

            for (int index = 0, count = _components.Count; index < count; ++index)
                _components[index].SetOwnerEnabled(value);
        }
    }

    internal SceneObject(Scene scene, string name)
    {
        Scene = scene;
        Name = name;

        Transform = AddComponent<Transform>();
    }

    public T AddComponent<T>() where T : Component, new()
    {
        return AddComponent(new T());
    }

    public T AddComponent<T>(T component) where T : Component
    {
        ThrowIfDestroyed();
        ArgumentNullException.ThrowIfNull(component);

        if (component.SceneObject is not null)
            throw new InvalidOperationException("Component is already attached to a scene object.");

        component.Attach(this);
        _components.Add(component);

        if (_active)
            component.Activate(_enabled);

        return component;
    }

    public T? GetComponent<T>() where T : Component
    {
        ThrowIfDestroyed();

        for (int index = 0, count = _components.Count; index < count; ++index)
        {
            if (_components[index] is T component)
                return component;
        }

        return null;
    }

    public bool TryGetComponent<T>(out T? component) where T : Component
    {
        component = GetComponent<T>();
        return component is not null;
    }

    public bool HasComponent<T>() where T : Component
    {
        return GetComponent<T>() is not null;
    }

    public bool RemoveComponent<T>() where T : Component
    {
        ThrowIfDestroyed();

        for (int index = 0, count = _components.Count; index < count; ++index)
        {
            if (_components[index] is not T component)
                continue;

            if (ReferenceEquals(component, Transform))
                throw new InvalidOperationException("Transform cannot be removed.");

            component.Destroy();
            _components.RemoveAt(index);

            return true;
        }

        return false;
    }

    public void Destroy()
    {
        if (_destroyed || _destroying)
            return;

        Scene.DestroyObject(this);
    }

    internal void Activate()
    {
        if (_active || _destroyed)
            return;

        _active = true;

        for (int index = 0, count = _components.Count; index < count; ++index)
            _components[index].Activate(_enabled);
    }

    internal void Update(float deltaTime)
    {
        if (!_active || !_enabled || _destroyed)
            return;

        for (int index = 0, count = _components.Count; index < count; ++index)
            _components[index].ProcessUpdate(deltaTime);
    }

    internal void LateUpdate(float deltaTime)
    {
        if (!_active || !_enabled || _destroyed)
            return;

        for (int index = 0, count = _components.Count; index < count; ++index)
            _components[index].ProcessLateUpdate(deltaTime);
    }

    internal void Deactivate()
    {
        if (!_active)
            return;

        for (var index = _components.Count - 1; index >= 0; --index)
            _components[index].Deactivate();

        _active = false;
    }

    internal void MarkForDestroy()
    {
        _destroying = true;
    }

    internal void DestroyImmediately()
    {
        if (_destroyed)
            return;

        for (var index = _components.Count - 1; index >= 0; --index)
            _components[index].Destroy();

        _components.Clear();

        _active = false;
        _destroying = false;
        _destroyed = true;
    }

    private void ThrowIfDestroyed()
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
    }
}