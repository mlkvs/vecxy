using System.Numerics;

namespace Vecxy.Rendering._Legacy;

public enum InputKey { W, A, S, D, Q, E, LeftShift, Escape }
public enum TextEditCommand { Left, Right, SelectLeft, SelectRight, Home, End, SelectHome, SelectEnd, Backspace, Delete, SelectAll, Copy, Cut, Paste }

public interface IInput
{
    bool IsKeyDown(InputKey key);
    bool IsRightMouseDown { get; }
    bool IsLeftMouseDown { get; }
    bool IsLeftMousePressed { get; }
    bool IsLeftMouseReleased { get; }
    bool ConsumeF12Pressed();
    Vector2 MousePosition { get; }
    bool ConsumeLeftMousePressed();
    bool ConsumeLeftMouseReleased();
    Vector2 ConsumeScrollDelta();
    string ConsumeTextInput();
    IReadOnlyList<TextEditCommand> ConsumeTextEditCommands();
    string ClipboardText { get; set; }
    Vector2 ConsumeMouseDelta();
    void SetMouseCaptured(bool captured);
}
