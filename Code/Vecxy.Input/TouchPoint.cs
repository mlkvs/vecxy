using System.Numerics;
using Vecxy.Kernel;

namespace Vecxy.Input;

public enum EPointerKind : byte
{
    Mouse,
    Touch
}

public readonly record struct TouchPoint(
    int Id,
    Vector2 Position,
    Vector2 Delta,
    ETouchPhase Phase,
    float Pressure,
    bool IsPrimary)
{
    public bool IsActive => Phase is not (ETouchPhase.Ended or ETouchPhase.Cancelled);
}
