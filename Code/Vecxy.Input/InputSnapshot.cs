using System.Numerics;
using Vecxy.Assets;

namespace Vecxy.Input;

public sealed class InputSnapshot
{
    private readonly HashSet<EKeyboardKey> _pressedKeys = [];
    private readonly HashSet<EMouseButton> _pressedMouseButtons = [];

    public Vector2 MousePosition { get; set; }
    public Vector2 MouseDelta { get; set; }
    public Vector2 MouseWheelDelta { get; set; }

    public bool IsKeyPressed(EKeyboardKey key) => _pressedKeys.Contains(key);

    public bool IsMouseButtonPressed(EMouseButton button) =>
        _pressedMouseButtons.Contains(button);

    public void SetKey(EKeyboardKey key, bool isPressed)
    {
        if (key == EKeyboardKey.Undefined)
            return;

        if (isPressed)
            _pressedKeys.Add(key);
        else
            _pressedKeys.Remove(key);
    }

    public void SetMouseButton(EMouseButton button, bool isPressed)
    {
        if (button == EMouseButton.Undefined)
            return;

        if (isPressed)
            _pressedMouseButtons.Add(button);
        else
            _pressedMouseButtons.Remove(button);
    }

}
