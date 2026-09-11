namespace Vecxy.Isometric;

public enum EIsometricObjectSlot : byte
{
    Surface,
    Inside
}

public class IsometricObjectSlot
{
    public EIsometricObjectSlot Type { get; }

    public int Capacity { get; }

    public List<IsometricObject> Objects { get; } = [];
}
