using Vecxy.Physics;

namespace Vecxy.Scene;

public abstract class AComponent
{
    private bool _enabled = true;
    private bool _awake;
    private bool _started;
    private bool _active;
    private bool _destroyed;

    public SceneObject? SceneObject { get; internal set; }

    public Scene Scene =>
        SceneObject?.Scene
        ?? throw new InvalidOperationException("Component is not attached to a scene object.");

    public Transform Transform =>
        SceneObject?.Transform
        ?? throw new InvalidOperationException("Component is not attached to a scene object.");

    public bool IsActive => _active && _enabled;

    public bool IsDestroyed => _destroyed;
    
    public event Action<AComponent>? Changed;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            ObjectDisposedException.ThrowIf(_destroyed, this);

            if (_enabled == value)
                return;

            _enabled = value;

            if (!_active)
                return;

            if (_enabled)
                OnEnable();
            else
                OnDisable();
        }
    }

    public virtual void Awake() { }
    public virtual void Start() { }
    public virtual void Update(float deltaTime) { }
    public virtual void LateUpdate(float deltaTime) { }
    public virtual void OnEnable() { }
    public virtual void OnDisable() { }
    public virtual void OnDestroy() { }
    public virtual void OnGizmos(ISceneGizmoDrawer gizmos) { }
    public virtual void OnCollisionEnter(Collider self, Collider other) { }
    public virtual void OnCollisionStay(Collider self, Collider other) { }
    public virtual void OnCollisionExit(Collider self, Collider other) { }
    public virtual void OnTriggerEnter(Collider self, Collider other) { }
    public virtual void OnTriggerStay(Collider self, Collider other) { }
    public virtual void OnTriggerExit(Collider self, Collider other) { }

    internal void Attach(SceneObject sceneObject)
    {
        SceneObject = sceneObject;
    }
    
    protected void NotifyChanged()
    {
        if (!_active || !_enabled || _destroyed)
            return;

        Changed?.Invoke(this);

        foreach (var system in SceneObject!.Scene.Systems)
        {
            system.OnComponentChanged(SceneObject, this);
        }
    }

    internal void Activate(bool ownerEnabled)
    {
        if (_destroyed || _active)
            return;

        if (!_awake)
        {
            _awake = true;
            Awake();
        }

        _active = ownerEnabled;

        if (_active && _enabled)
            OnEnable();
    }

    internal void ProcessUpdate(float deltaTime)
    {
        if (!_active || !_enabled || _destroyed)
            return;

        if (!_started)
        {
            _started = true;
            Start();

            if (!_active || !_enabled || _destroyed)
                return;
        }

        Update(deltaTime);
    }

    internal void ProcessLateUpdate(float deltaTime)
    {
        if (!_active || !_enabled || !_started || _destroyed)
            return;

        LateUpdate(deltaTime);
    }

    internal void SetOwnerEnabled(bool enabled)
    {
        if (_destroyed || _active == enabled)
            return;

        _active = enabled;

        if (!_awake && enabled)
        {
            _awake = true;
            Awake();
        }

        if (!_enabled)
            return;

        if (_active)
            OnEnable();
        else
            OnDisable();
    }

    internal void Deactivate()
    {
        if (!_active)
            return;

        if (_enabled)
            OnDisable();

        _active = false;
    }

    internal void Destroy()
    {
        if (_destroyed)
            return;

        if (_active && _enabled)
            OnDisable();

        OnDestroy();

        _active = false;
        _destroyed = true;
        SceneObject = null;
    }

    public void DrawGizmos(ISceneGizmoDrawer gizmos)
    {
        if (!_active || !_enabled || _destroyed)
            return;

        OnGizmos(gizmos);
    }
}
