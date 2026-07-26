using System.Numerics;

namespace Vecxy.Input;

public class InputAction
{
    protected InputBinding[] Bindings;
    protected bool IsPressedState;

    public InputAction(string name, params InputBinding[] bindings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(bindings);

        Name = name;
        Bindings = bindings;
    }

    public string Name { get; }

    public bool IsPressed => IsPressedState;

    public event Action<InputActionContext>? Started;
    public event Action<InputActionContext>? Performed;
    public event Action<InputActionContext>? Canceled;

    internal virtual void Sync(InputSnapshot snapshot, InputMap map)
    {
        IsPressedState = EvaluateButton(snapshot);
    }

    internal virtual void Update(InputSnapshot snapshot, InputMap map)
    {
        var pressed = EvaluateButton(snapshot);

        if (!IsPressedState && pressed)
        {
            IsPressedState = true;
            RaiseStarted(map);
            RaisePerformed(map);
            return;
        }

        if (IsPressedState && !pressed)
        {
            IsPressedState = false;
            RaiseCanceled(map);
        }
    }

    internal virtual void Reset()
    {
        IsPressedState = false;
    }

    internal virtual void Rebind(params InputBinding[] bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        Bindings = bindings;
        Reset();
    }

    protected bool EvaluateButton(InputSnapshot snapshot)
    {
        for (var index = 0; index < Bindings.Length; index++)
        {
            if (Bindings[index].ReadButton(snapshot))
                return true;
        }

        return false;
    }

    protected void RaiseStarted(InputMap map)
    {
        Started?.Invoke(new InputActionContext(this, map, InputActionPhase.Started));
    }

    protected void RaisePerformed(InputMap map)
    {
        Performed?.Invoke(new InputActionContext(this, map, InputActionPhase.Performed));
    }

    protected void RaiseCanceled(InputMap map)
    {
        Canceled?.Invoke(new InputActionContext(this, map, InputActionPhase.Canceled));
    }
}

public sealed class InputAction<TValue> : InputAction
{
    public InputAction(string name, params InputBinding[] bindings)
        : base(name, bindings)
    {
    }

    public TValue Value { get; private set; } = default!;

    public new event Action<InputActionContext<TValue>>? Started;
    public new event Action<InputActionContext<TValue>>? Performed;
    public new event Action<InputActionContext<TValue>>? Canceled;

    internal override void Sync(InputSnapshot snapshot, InputMap map)
    {
        if (typeof(TValue) == typeof(Vector2))
        {
            var value = EvaluateVector2(snapshot);
            Value = (TValue)(object)value;
            IsPressedState = value != Vector2.Zero;
            return;
        }

        throw new NotSupportedException(
            $"Unsupported input action value type '{typeof(TValue).FullName}'.");
    }

    internal override void Update(InputSnapshot snapshot, InputMap map)
    {
        if (typeof(TValue) != typeof(Vector2))
        {
            throw new NotSupportedException(
                $"Unsupported input action value type '{typeof(TValue).FullName}'.");
        }

        var previous = Value is Vector2 vector
            ? vector
            : Vector2.Zero;
        var current = EvaluateVector2(snapshot);

        if (previous == current)
            return;

        var typedCurrent = (TValue)(object)current;
        Value = typedCurrent;

        var wasActive = previous != Vector2.Zero;
        var isActive = current != Vector2.Zero;
        IsPressedState = isActive;

        if (!wasActive && isActive)
        {
            RaiseStarted(map, typedCurrent);
            RaisePerformed(map, typedCurrent);
            return;
        }

        if (wasActive && !isActive)
        {
            RaiseCanceled(map, typedCurrent);
            return;
        }

        RaisePerformed(map, typedCurrent);
    }

    internal override void Reset()
    {
        base.Reset();
        Value = default!;
    }

    private Vector2 EvaluateVector2(InputSnapshot snapshot)
    {
        var value = Vector2.Zero;

        for (var index = 0; index < Bindings.Length; index++)
            value += Bindings[index].ReadVector2(snapshot);

        if (value.LengthSquared() > 1.0f)
            value = Vector2.Normalize(value);

        return value;
    }

    private void RaiseStarted(InputMap map, TValue value)
    {
        base.RaiseStarted(map);
        Started?.Invoke(
            new InputActionContext<TValue>(
                this,
                map,
                InputActionPhase.Started,
                value));
    }

    private void RaisePerformed(InputMap map, TValue value)
    {
        base.RaisePerformed(map);
        Performed?.Invoke(
            new InputActionContext<TValue>(
                this,
                map,
                InputActionPhase.Performed,
                value));
    }

    private void RaiseCanceled(InputMap map, TValue value)
    {
        base.RaiseCanceled(map);
        Canceled?.Invoke(
            new InputActionContext<TValue>(
                this,
                map,
                InputActionPhase.Canceled,
                value));
    }
}
