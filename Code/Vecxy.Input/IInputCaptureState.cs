namespace Vecxy.Input;

public interface IInputCaptureState
{
    bool SuppressKeyboard { get; set; }

    bool SuppressMouse { get; set; }
}

internal sealed class InputCaptureState : IInputCaptureState
{
    public bool SuppressKeyboard { get; set; }

    public bool SuppressMouse { get; set; }
}
