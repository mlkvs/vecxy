using System.Numerics;
using Vecxy.Pathfinding;
using Vecxy.Prototypes;
using Vecxy.Scene;

namespace Vecxy.AI;

public enum NavigationAgentState { Idle, Following, Arrived, NoPath }

[SingleComponent]
[Authoring("Navigation Agent", Category = "AI", Key = "navigationAgent")]
public sealed class NavigationAgent : AComponent
{
    private int _revision = -1;
    private NavigationMap? _map;
    private IReadOnlyList<Vector3> _path = [];
    private int _waypoint;
    private float _speed = 1.8f;

    public float Speed
    {
        get => _speed;
        set => _speed = value >= 0 && float.IsFinite(value) ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }
    public bool FaceMovement { get; set; } = true;
    public Vector3? Destination { get; private set; }
    public NavigationAgentState State { get; private set; }
    public IReadOnlyList<Vector3> Path => _path;
    public int NextWaypoint => _waypoint;

    public void SetDestination(Vector3 destination)
    {
        if (!float.IsFinite(destination.X) || !float.IsFinite(destination.Y) || !float.IsFinite(destination.Z))
            throw new ArgumentOutOfRangeException(nameof(destination));
        if (destination == Destination)
            return;
        Destination = destination;
        _revision = -1;
    }

    public void Stop()
    {
        Destination = null;
        ClearPath(NavigationAgentState.Idle);
    }

    public override void OnDisable()
    {
        _revision = -1;
        ClearPath(NavigationAgentState.Idle);
    }

    internal void Advance(NavigationMap? map, float deltaTime)
    {
        if (Destination is not { } destination)
            return;
        if (map is null)
        {
            _map = default;
            ClearPath(NavigationAgentState.NoPath);
            return;
        }

        if (!ReferenceEquals(map, _map) || _revision != map.Revision)
        {
            var canContinue = ReferenceEquals(map, _map) && _revision >= 0 &&
                (State == NavigationAgentState.Arrived || State == NavigationAgentState.Following &&
                    map.IsPathClear(Transform.WorldPosition, _path, _waypoint));
            _map = map;
            _revision = map.Revision;
            if (!canContinue)
            {
                _path = map.FindPath(Transform.WorldPosition, destination);
                _waypoint = 0;
                State = _path.Count > 0 ? NavigationAgentState.Following : NavigationAgentState.NoPath;
            }
        }

        var remaining = Speed * deltaTime;
        while (remaining > 0 && _waypoint < _path.Count)
        {
            var current = Transform.WorldPosition;
            var offset = _path[_waypoint] - current;
            var distance = offset.Length();
            if (distance <= remaining)
            {
                Transform.WorldPosition = _path[_waypoint++];
                remaining -= distance;
            }
            else
            {
                Transform.WorldPosition += offset * (remaining / distance);
                remaining = 0;
            }
            if (FaceMovement && offset.X * offset.X + offset.Z * offset.Z > 1e-8f)
                Transform.WorldRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.Atan2(-offset.X, -offset.Z));
        }
        if (State == NavigationAgentState.Following && _waypoint == _path.Count)
            State = NavigationAgentState.Arrived;
    }

    private void ClearPath(NavigationAgentState state)
    {
        _path = [];
        _waypoint = 0;
        State = state;
    }

    public sealed class Prototype : APrototype<NavigationAgent, Prototype.Options>
    {
        public sealed class Options
        {
            [Authoring("Speed", Min = 0, Unit = "m/s")]
            public float Speed { get; set; } = 1.8f;

            [Authoring("Face movement")]
            public bool FaceMovement { get; set; } = true;
        }

        protected override NavigationAgent Instantiate(IPrototypeContext context)
        {
            var scene = RequireContext<ScenePrototypeContext>(context);
            return (scene.Object ?? scene.CreateObject("Navigation agent")).AddComponent<NavigationAgent>();
        }

        protected override void Configure(NavigationAgent agent, Options options)
        {
            agent.Speed = options.Speed;
            agent.FaceMovement = options.FaceMovement;
        }
    }
}
