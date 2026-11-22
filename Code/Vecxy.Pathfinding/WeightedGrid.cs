namespace Vecxy.Pathfinding;

public sealed class WeightedGrid
{
    private static readonly (int X, int Y)[] CardinalOffsets = [(1, 0), (-1, 0), (0, 1), (0, -1)];
    private static readonly (int X, int Y)[] DiagonalOffsets = [(1, 1), (1, -1), (-1, 1), (-1, -1)];
    private readonly float[] _costs;
    private readonly bool[] _blocked;
    private readonly Func<GridPoint, GridPoint, bool>? _connection;

    public WeightedGrid(int columns, int rows)
        : this(columns, rows, null)
    {
    }

    internal WeightedGrid(int columns, int rows, Func<GridPoint, GridPoint, bool>? connection)
    {
        if (columns <= 0) throw new ArgumentOutOfRangeException(nameof(columns));
        if (rows <= 0) throw new ArgumentOutOfRangeException(nameof(rows));
        Columns = columns;
        Rows = rows;
        _connection = connection;
        _costs = new float[checked(columns * rows)];
        _blocked = new bool[_costs.Length];
        Array.Fill(_costs, 1f);
    }

    public int Columns { get; }
    public int Rows { get; }

    public bool Contains(GridPoint point) =>
        (uint)point.X < (uint)Columns && (uint)point.Y < (uint)Rows;

    public bool IsWalkable(GridPoint point) => Contains(point) && !_blocked[Index(point)];

    public void SetBlocked(GridPoint point, bool blocked = true)
    {
        EnsureContains(point);
        _blocked[Index(point)] = blocked;
    }

    public float GetTraversalCost(GridPoint point)
    {
        EnsureContains(point);
        return _costs[Index(point)];
    }

    public void SetTraversalCost(GridPoint point, float cost)
    {
        EnsureContains(point);
        if (!float.IsFinite(cost) || cost < 1f)
            throw new ArgumentOutOfRangeException(nameof(cost), "Traversal cost must be finite and at least 1.");
        _costs[Index(point)] = cost;
    }

    internal IEnumerable<(GridPoint Point, float Distance)> GetNeighbors(
        GridPoint point,
        bool allowDiagonal,
        bool allowCornerCutting)
    {
        foreach (var offset in CardinalOffsets)
        {
            var neighbor = new GridPoint(point.X + offset.X, point.Y + offset.Y);
            if (CanTraverse(point, neighbor)) yield return (neighbor, 1f);
        }

        if (!allowDiagonal) yield break;
        foreach (var offset in DiagonalOffsets)
        {
            var neighbor = new GridPoint(point.X + offset.X, point.Y + offset.Y);
            if (!CanTraverse(point, neighbor)) continue;
            if (!allowCornerCutting &&
                (!Through(new(point.X + offset.X, point.Y)) ||
                 !Through(new(point.X, point.Y + offset.Y))))
                continue;
            yield return (neighbor, MathF.Sqrt(2f));

            bool Through(GridPoint via) => CanTraverse(point, via) && CanTraverse(via, neighbor);
        }
    }

    private bool CanTraverse(GridPoint from, GridPoint to) =>
        IsWalkable(to) && (_connection?.Invoke(from, to) ?? true);

    private int Index(GridPoint point) => point.Y * Columns + point.X;

    private void EnsureContains(GridPoint point)
    {
        if (!Contains(point)) throw new ArgumentOutOfRangeException(nameof(point));
    }
}
