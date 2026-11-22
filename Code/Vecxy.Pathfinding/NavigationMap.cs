using System.Numerics;

namespace Vecxy.Pathfinding;

/// <summary>The baked walkable surface with a mutable overlay of live obstacles.</summary>
public sealed class NavigationMap
{
    private readonly WeightedGrid _grid;
    private readonly int[] _occupancy;
    private readonly float _rise;

    public NavigationData Data { get; }
    public int Revision { get; private set; }

    public NavigationMap(NavigationData data)
    {
        Data = data.Validate();
        _occupancy = new int[data.Heights.Length];
        _rise = Math.Max(data.Settings.MaxStep,
            MathF.Tan(data.Settings.MaxSlope * MathF.PI / 180) * data.Settings.CellSize);
        _grid = new WeightedGrid(data.Columns, data.Rows, CanStep);
        for (var i = 0; i < data.Heights.Length; i++)
            _grid.SetBlocked(data.Point(i), !data.Heights[i].HasValue);
    }

    public bool IsBlocked(int cell) => _occupancy[cell] > 0;

    /// <summary>Apply a change in one obstacle's coverage without changing the baked surface.</summary>
    public void UpdateObstacle(IReadOnlySet<int> previous, IReadOnlySet<int> current)
    {
        var changed = false;
        foreach (var cell in previous)
            if (!current.Contains(cell) && --_occupancy[cell] == 0)
            {
                _grid.SetBlocked(Data.Point(cell), !Data.Heights[cell].HasValue);
                changed = true;
            }
        foreach (var cell in current)
            if (!previous.Contains(cell) && _occupancy[cell]++ == 0)
            {
                _grid.SetBlocked(Data.Point(cell));
                changed = true;
            }
        if (changed)
            Revision++;
    }

    public IReadOnlyList<Vector3> FindPath(Vector3 start, Vector3 destination)
    {
        if (!NavigationBaker.IsFinite(start) || !NavigationBaker.IsFinite(destination))
            throw new ArgumentException("Navigation points must be finite.");
        var origin = Data.CellAt(start);
        var goal = Data.CellAt(destination);
        if (!Data.Contains(origin) || !Data.Contains(goal) ||
            Data.Heights[Data.Index(origin)] is not { } originY || !_grid.IsWalkable(goal) ||
            MathF.Abs(start.Y - originY) > Math.Max(_rise, Data.Settings.CellSize))
            return [];

        // An obstacle can move into an agent's current cell. Let it leave that
        // cell along a valid connection, while all other blocked cells stay closed.
        var blockedOrigin = !_grid.IsWalkable(origin);
        if (blockedOrigin)
            _grid.SetBlocked(origin, false);
        try
        {
            var search = new AStarSearch(_grid, origin, goal, new PathfindingOptions());
            while (search.State == EPathSearchState.Searching)
                search.Step();
            if (search.Result is not { Status: EPathStatus.Ok } result)
                return [];
            var points = result.Path.Select(point => Data.Center(Data.Index(point))).ToList();
            var center = points[^1];
            var destinationHeight = center.Y + Vector2.Dot(Data.Gradient(Data.Index(goal)),
                new(destination.X - center.X, destination.Z - center.Z));
            var end = new Vector3(destination.X, destinationHeight, destination.Z);
            if (points[^1] != end)
                points.Add(end);
            if (Vector3.DistanceSquared(start, points[0]) < .0001f)
                points.RemoveAt(0);
            return points.Count == 0 ? [end] : points;
        }
        finally
        {
            if (blockedOrigin)
                _grid.SetBlocked(origin);
        }
    }

    public bool IsPathClear(Vector3 position, IReadOnlyList<Vector3> path, int next)
    {
        var current = Data.CellAt(position);
        if (!Data.Contains(current))
            return false;
        for (var i = next; i < path.Count; i++)
        {
            var target = Data.CellAt(path[i]);
            if (!_grid.IsWalkable(target) || current != target &&
                !_grid.GetNeighbors(current, allowDiagonal: true, allowCornerCutting: false)
                    .Any(neighbor => neighbor.Point == target))
                return false;
            current = target;
        }
        return true;
    }

    private bool CanStep(GridPoint a, GridPoint b)
    {
        var start = Data.Index(a);
        var target = Data.Index(b);
        if (Data.Heights[start] is not { } from || Data.Heights[target] is not { } to)
            return false;
        return NavigationBaker.Connect(from, Data.Gradient(start), to, Data.Gradient(target),
            b.X - a.X, b.Y - a.Y, Data.Settings);
    }
}
