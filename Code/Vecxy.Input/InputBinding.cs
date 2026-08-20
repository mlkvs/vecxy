using System.Numerics;
using Vecxy.Assets;

namespace Vecxy.Input;

public abstract class InputBinding
{
    public static KeyBinding Keyboard(EKeyboardKey key) => new(key);
    public static MouseButtonBinding Mouse(EMouseButton button) => new(button);
    public static CompositeBinding Composite(InputComposite composite) => new(composite);

    internal virtual bool ReadButton(InputSnapshot snapshot) => false;

    internal virtual Vector2 ReadVector2(InputSnapshot snapshot) => Vector2.Zero;
}

public sealed class KeyBinding(EKeyboardKey key) : InputBinding
{
    public EKeyboardKey Key { get; } = key;

    internal override bool ReadButton(InputSnapshot snapshot) =>
        snapshot.IsKeyPressed(Key);
}

public sealed class MouseButtonBinding(EMouseButton button) : InputBinding
{
    public EMouseButton Button { get; } = button;

    internal override bool ReadButton(InputSnapshot snapshot) =>
        snapshot.IsMouseButtonPressed(Button);
}

public sealed class MouseDeltaBinding : InputBinding
{
    internal override Vector2 ReadVector2(InputSnapshot snapshot) =>
        snapshot.MouseDelta;
}

public sealed class CompositeBinding(InputComposite composite) : InputBinding
{
    public InputComposite Kind { get; } = composite;

    internal override Vector2 ReadVector2(InputSnapshot snapshot)
    {
        return Kind switch
        {
            InputComposite.Wasd => ReadWasd(snapshot),
            _ => Vector2.Zero,
        };
    }

    private static Vector2 ReadWasd(InputSnapshot snapshot)
    {
        var x = 0.0f;
        var y = 0.0f;

        if (snapshot.IsKeyPressed(EKeyboardKey.A))
            x -= 1.0f;

        if (snapshot.IsKeyPressed(EKeyboardKey.D))
            x += 1.0f;

        if (snapshot.IsKeyPressed(EKeyboardKey.S))
            y -= 1.0f;

        if (snapshot.IsKeyPressed(EKeyboardKey.W))
            y += 1.0f;

        var value = new Vector2(x, y);
        return value.LengthSquared() > 1.0f
            ? Vector2.Normalize(value)
            : value;
    }
}
