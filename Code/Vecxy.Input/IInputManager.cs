using System.Numerics;
using Vecxy.Assets;

namespace Vecxy.Input;

public interface IInputManager
{
    Vector2 MousePosition { get; }
    Vector2 MouseDelta { get; }
    Vector2 MouseWheelDelta { get; }

    bool IsKeyPressed(EKeyboardKey key);
    bool IsMouseButtonPressed(EMouseButton button);

    InputMap Create(AssetRef<InputAsset> asset, string mapName);
}
