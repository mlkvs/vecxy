using Silk.NET.Input;
using Vecxy.Assets;

namespace Vecxy.Input;

internal static class InputTypeMaps
{
    public static EKeyboardKey MapKey(int key)
    {
        return Enum.IsDefined(typeof(EKeyboardKey), key)
            ? (EKeyboardKey)key
            : EKeyboardKey.Undefined;
    }

    public static EMouseButton MapMouseButton(int button)
    {
        return Enum.IsDefined(typeof(EMouseButton), button)
            ? (EMouseButton)button
            : EMouseButton.Undefined;
    }
}
