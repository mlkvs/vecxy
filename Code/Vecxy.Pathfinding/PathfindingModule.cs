using System.Runtime.CompilerServices;
using Autofac;
using Vecxy.Kernel;

namespace Vecxy.Pathfinding;

public sealed record PathfindingOptions
{
    public bool AllowDiagonal { get; init; } = true;
    public bool AllowCornerCutting { get; init; }
}

public interface IPathfinding
{
    PathResult FindPath(WeightedGrid grid, GridPoint start, GridPoint goal, PathfindingOptions? options = null);
    IPathSearch StartSearch(WeightedGrid grid, GridPoint start, GridPoint goal, PathfindingOptions? options = null);
    IAsyncEnumerable<PathSearchStep> SearchAsync(
        WeightedGrid grid,
        GridPoint start,
        GridPoint goal,
        PathfindingOptions? options = null,
        TimeSpan stepDelay = default,
        CancellationToken cancellationToken = default);
}

public sealed class PathfindingModule : IModule, IPathfinding
{
    public sealed class Definition : AModuleDefinition<PathfindingModule>
    {
        protected override IReadOnlyList<Type> Exports => [typeof(IPathfinding)];

        protected override void RegisterModule(ContainerBuilder builder) =>
            builder.RegisterType<PathfindingModule>().AsSelf().SingleInstance();
    }

    public PathResult FindPath(WeightedGrid grid, GridPoint start, GridPoint goal, PathfindingOptions? options = null)
    {
        var search = StartSearch(grid, start, goal, options);
        while (search.State == EPathSearchState.Searching) search.Step();
        return search.Result!;
    }

    public IPathSearch StartSearch(WeightedGrid grid, GridPoint start, GridPoint goal, PathfindingOptions? options = null) =>
        new AStarSearch(grid, start, goal, options ?? new PathfindingOptions());

    public async IAsyncEnumerable<PathSearchStep> SearchAsync(
        WeightedGrid grid,
        GridPoint start,
        GridPoint goal,
        PathfindingOptions? options = null,
        TimeSpan stepDelay = default,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (stepDelay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(stepDelay));
        var search = StartSearch(grid, start, goal, options);
        while (search.State == EPathSearchState.Searching)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (stepDelay > TimeSpan.Zero)
                await Task.Delay(stepDelay, cancellationToken);
            yield return search.Step();
        }
    }

    public void OnInitialize() { }
    public void OnShutdown() { }
    public void Dispose() { }
}

public interface IPathSearch
{
    EPathSearchState State { get; }
    PathResult? Result { get; }
    IReadOnlyCollection<GridPoint> Visited { get; }
    PathSearchStep Step();
}

internal sealed class AStarSearch : IPathSearch
{
    private readonly WeightedGrid _grid;
    private readonly GridPoint _start;
    private readonly GridPoint _goal;
    private readonly PathfindingOptions _options;
    private readonly PriorityQueue<GridPoint, float> _open = new();
    private readonly Dictionary<GridPoint, float> _costs = [];
    private readonly Dictionary<GridPoint, GridPoint> _parents = [];
    private readonly HashSet<GridPoint> _closed = [];

    internal AStarSearch(WeightedGrid grid, GridPoint start, GridPoint goal, PathfindingOptions options)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (!grid.IsWalkable(start)) throw new ArgumentException("Start must be a walkable grid cell.", nameof(start));
        if (!grid.IsWalkable(goal)) throw new ArgumentException("Goal must be a walkable grid cell.", nameof(goal));
        _grid = grid;
        _start = start;
        _goal = goal;
        _options = options;
        _costs[start] = 0f;
        _open.Enqueue(start, Heuristic(start));
    }

    public EPathSearchState State { get; private set; } = EPathSearchState.Searching;
    public PathResult? Result { get; private set; }
    public IReadOnlyCollection<GridPoint> Visited => _closed;

    public PathSearchStep Step()
    {
        if (State != EPathSearchState.Searching)
            throw new InvalidOperationException("The path search has already completed.");

        while (_open.TryDequeue(out var current, out _))
        {
            if (!_closed.Add(current)) continue;
            if (current == _goal)
            {
                CompleteFound();
                return new PathSearchStep(current, [], State, Result);
            }

            var opened = new List<GridPoint>();
            foreach (var (neighbor, distance) in _grid.GetNeighbors(current, _options.AllowDiagonal, _options.AllowCornerCutting))
            {
                if (_closed.Contains(neighbor)) continue;
                var cost = _costs[current] + distance * _grid.GetTraversalCost(neighbor);
                if (_costs.TryGetValue(neighbor, out var knownCost) && cost >= knownCost) continue;
                _costs[neighbor] = cost;
                _parents[neighbor] = current;
                _open.Enqueue(neighbor, cost + Heuristic(neighbor));
                opened.Add(neighbor);
            }
            return new PathSearchStep(current, opened, State, null);
        }

        State = EPathSearchState.NoPath;
        Result = new PathResult { Status = EPathStatus.NoPath, VisitedNodes = _closed.Count };
        return new PathSearchStep(_start, [], State, Result);
    }

    private void CompleteFound()
    {
        var path = new List<GridPoint> { _goal };
        while (path[^1] != _start) path.Add(_parents[path[^1]]);
        path.Reverse();
        State = EPathSearchState.Found;
        Result = new PathResult
        {
            Status = EPathStatus.Ok,
            Path = path,
            TotalCost = _costs[_goal],
            VisitedNodes = _closed.Count
        };
    }

    private float Heuristic(GridPoint point)
    {
        var dx = Math.Abs(point.X - _goal.X);
        var dy = Math.Abs(point.Y - _goal.Y);
        return _options.AllowDiagonal
            ? Math.Max(dx, dy) + (MathF.Sqrt(2f) - 1f) * Math.Min(dx, dy)
            : dx + dy;
    }
}
