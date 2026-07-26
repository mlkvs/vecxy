namespace Vecxy.Input;

public readonly record struct InputActionContext(
    InputAction Action,
    InputMap Map,
    InputActionPhase Phase);

public readonly record struct InputActionContext<TValue>(
    InputAction<TValue> Action,
    InputMap Map,
    InputActionPhase Phase,
    TValue Value);
