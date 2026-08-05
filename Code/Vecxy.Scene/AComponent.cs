using System.Numerics;

namespace Vecxy.Scene;

public abstract class AComponent
{
    private bool _enabled = true;
    private bool _awake;
    private bool _started;
    private bool _active;
    private bool _destroyed;

    public SceneObject? SceneObject { get; internal set; }

    public SceneInstance SceneInstance =>
        SceneObject?.SceneInstance
        ?? throw new InvalidOperationException(
            "Component is not attached to a scene object.");

    public Transform Transform =>
        SceneObject?.Transform
        ?? throw new InvalidOperationException(
            "Component is not attached to a scene object.");

    public bool IsActive => _active && _enabled;

    public bool IsDestroyed => _destroyed;

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
    public virtual void FixedUpdate(float deltaTime) { }
    public virtual void Update(float deltaTime) { }
    public virtual void LateUpdate(float deltaTime) { }
    public virtual void OnEnable() { }
    public virtual void OnDisable() { }
    public virtual void OnDestroy() { }
    public virtual void OnGizmos(ISceneGizmoDrawer gizmos) { }

    internal void Attach(SceneObject sceneObject)
    {
        SceneObject = sceneObject;
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

        if (!EnsureStarted())
            return;

        Update(deltaTime);
    }

    internal void ProcessFixedUpdate(float deltaTime)
    {
        if (!_active || !_enabled || _destroyed)
            return;

        if (!EnsureStarted())
            return;

        FixedUpdate(deltaTime);
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

    private bool EnsureStarted()
    {
        if (_started)
            return true;

        _started = true;
        Start();

        return _active && _enabled && !_destroyed;
    }
    
    public interface IPrototype
    {
        public interface IOptions;
        
        Type ComponentType { get; }

        public void Configure(AComponent component, IOptions options);
        public AComponent Instantiate(InstantiateContext ctx);
    }
    
    public abstract class APrototype<TComponent, TOptions> : IPrototype
        where TComponent : AComponent
        where TOptions : IPrototype.IOptions
     {
        public Type ComponentType => typeof(TComponent);

        void IPrototype.Configure(AComponent component, IPrototype.IOptions options) => Configure((TComponent)component, (TOptions)options);
        AComponent IPrototype.Instantiate(InstantiateContext ctx) => Instantiate(ctx);

        protected abstract TComponent Instantiate(InstantiateContext ctx);
        protected abstract void Configure(TComponent component, TOptions options);
    }
}
