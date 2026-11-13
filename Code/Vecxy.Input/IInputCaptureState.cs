using Vecxy.Assets;

namespace Vecxy.Input;

public interface IInputCaptureState
{
    bool SuppressKeyboard { get; set; }
    bool SuppressMouse { get; set; }
    // Defaults preserve existing custom capture implementations.
    bool SuppressMouseMotion { get => false; set { } }
    bool SuppressMouseButtons { get => false; set { } }
    EKeyboardKey SuppressedKey { get => EKeyboardKey.Undefined; set { } }
}

internal sealed class InputCaptureState : IInputCaptureState
{
    public bool SuppressKeyboard { get; set; }
    public bool SuppressMouse { get; set; }
    public bool SuppressMouseMotion { get; set; }
    public bool SuppressMouseButtons { get; set; }
    public EKeyboardKey SuppressedKey { get; set; }
}
