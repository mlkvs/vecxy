using System.Numerics;
using Vecxy.Assets;

namespace Vecxy.Input;

public interface IInputManager
{
    Vector2 MousePosition { get; }
    Vector2 MouseDelta { get; }
    Vector2 MouseWheelDelta { get; }
    IReadOnlyList<TouchPoint> Touches { get; }
    Vector2 PointerPosition { get; }
    Vector2 PointerDelta { get; }
    EPointerKind PointerKind { get; }
    bool IsPrimaryPointerPressed { get; }

    bool IsKeyPressed(EKeyboardKey key);
    bool IsMouseButtonPressed(EMouseButton button);

    InputMap Create(AssetRef<InputAsset> asset, string mapName);
}
