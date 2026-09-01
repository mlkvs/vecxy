namespace Vecxy.Pathfinding;

public readonly record struct GridPoint(int X, int Y);

public enum EPathSearchState : byte
{
    Searching,
    Found,
    NoPath
}

public sealed record PathSearchStep(
    GridPoint Expanded,
    IReadOnlyList<GridPoint> Opened,
    EPathSearchState State,
    PathResult? Result);
