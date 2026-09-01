namespace Vecxy.Pathfinding;

public sealed class PathResult
{
    public required EPathStatus Status { get; init; }
    public IReadOnlyList<GridPoint> Path { get; init; } = [];
    public float TotalCost { get; init; }
    public int VisitedNodes { get; init; }
}
