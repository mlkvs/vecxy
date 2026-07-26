using System.Numerics;
using Vecxy.Assets;

namespace Vecxy.Input;

public interface IInputManager
{
    Vector2 MousePosition { get; }
    Vector2 MouseDelta { get; }

    InputMap Create(AssetRef<InputAsset> asset, string mapName);
}
