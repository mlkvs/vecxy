using System.Numerics;
using Vecxy.Assets;

namespace Vecxy.Physics;

public sealed class PhysicsConfig : IYamlConfig
{
    public float FixedUpdateRate { get; set; } = 60.0f;
    public int MaxSubSteps { get; set; } = 8;
    public float MaxFrameDelta { get; set; } = 0.25f;
    public float[] Gravity { get; set; } = [0.0f, -9.81f, 0.0f];
    public bool AllowSleeping { get; set; } = true;
    public bool InterpolationEnabled { get; set; } = true;
    public Dictionary<string, PhysicsCollisionLayerConfig> CollisionLayers
    {
        get;
        set;
    } = CreateDefaultCollisionLayers();

    // Restart-required: changing the solver layout while worlds are alive is
    // deliberately rejected to keep simulation state predictable.
    public int SolverIterations { get; set; } = 6;
    public int SolverRelaxationIterations { get; set; } = 2;

    public void Validate()
    {
        if (!float.IsFinite(FixedUpdateRate) ||
            FixedUpdateRate is <= 0.0f or > 1000.0f)
        {
            throw new InvalidDataException(
                $"Physics config has invalid fixedUpdateRate.");
        }

        if (MaxSubSteps is < 1 or > 64)
        {
            throw new InvalidDataException(
                $"Physics config has invalid maxSubSteps.");
        }

        if (!float.IsFinite(MaxFrameDelta) ||
            MaxFrameDelta <= 0.0f)
        {
            throw new InvalidDataException(
                $"Physics config has invalid maxFrameDelta.");
        }

        if (Gravity is not { Length: 3 } ||
            Gravity.Any(value => !float.IsFinite(value)))
        {
            throw new InvalidDataException(
                $"Physics config  must contain a finite gravity vector.");
        }

        if (SolverIterations is < 1 or > 64 ||
            SolverRelaxationIterations is < 0 or > 64)
        {
            throw new InvalidDataException(
                $"Physics config has invalid solver iterations.");
        }

        BuildCollisionLayers();
    }

    public PhysicsSettings ToSettings() => new(
        FixedDeltaTime: 1.0f / FixedUpdateRate,
        MaxSubSteps,
        MaxFrameDelta,
        Gravity: new Vector3(Gravity[0], Gravity[1], Gravity[2]),
        AllowSleeping,
        InterpolationEnabled,
        SolverIterations,
        SolverRelaxationIterations,
        BuildCollisionLayers());

    private PhysicsCollisionLayers BuildCollisionLayers()
    {
        if (CollisionLayers is null || CollisionLayers.Count == 0)
        {
            throw new InvalidDataException(
                $"Physics config  must define collisionLayers.");
        }

        var definitions = new Dictionary<
            string,
            PhysicsCollisionLayerConfig>(
            StringComparer.OrdinalIgnoreCase);
        var indexes = new HashSet<int>();

        foreach (var (name, definition) in CollisionLayers)
        {
            if (string.IsNullOrWhiteSpace(name) ||
                !string.Equals(name, name.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Physics config contains an invalid collision layer name.");
            }

            if (definition is null)
            {
                throw new InvalidDataException(
                    $"Physics config has no definition for collision layer '{name}'.");
            }

            if (!definitions.TryAdd(name, definition))
            {
                throw new InvalidDataException(
                    $"Physics config  contains duplicate collision layer '{name}'.");
            }

            if (definition.Index is < 0 or > 31)
            {
                throw new InvalidDataException(
                    $"Physics config collision layer '{name}' must use an index from 0 to 31.");
            }

            if (!indexes.Add(definition.Index))
            {
                throw new InvalidDataException(
                    $"Physics config reuses collision layer index {definition.Index}.");
            }

            if (definition.CollidesWith is null)
            {
                throw new InvalidDataException(
                    $"Physics config collision layer '{name}' must define collidesWith.");
            }
        }

        if (!definitions.ContainsKey(PhysicsCollisionLayers.DefaultLayerName))
        {
            throw new InvalidDataException(
                $"Physics config  must define the '{PhysicsCollisionLayers.DefaultLayerName}' collision layer.");
        }

        var resolved = new Dictionary<string, PhysicsLayer>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var (name, definition) in definitions)
        {
            var targets = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            uint mask = 0;

            foreach (var targetName in definition.CollidesWith)
            {
                if (string.IsNullOrWhiteSpace(targetName) ||
                    !definitions.TryGetValue(targetName, out var target))
                {
                    throw new InvalidDataException(
                        $"Physics config collision layer '{name}' references unknown layer '{targetName}'.");
                }

                if (!targets.Add(targetName))
                {
                    throw new InvalidDataException(
                        $"Physics config  collision layer '{name}' contains duplicate collidesWith entry '{targetName}'.");
                }

                mask |= 1u << target.Index;
            }

            resolved.Add(
                name,
                new PhysicsLayer(1u << definition.Index, mask));
        }

        foreach (var (name, definition) in definitions)
        {
            foreach (var targetName in definition.CollidesWith)
            {
                var target = definitions[targetName];
                if (!target.CollidesWith.Contains(
                        name,
                        StringComparer.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"Physics config collision matrix is not symmetric: '{name}' collides with '{targetName}', but the reverse relation is missing.");
                }
            }
        }

        return new PhysicsCollisionLayers(resolved);
    }

    private static Dictionary<string, PhysicsCollisionLayerConfig>
        CreateDefaultCollisionLayers() =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            [PhysicsCollisionLayers.DefaultLayerName] = new()
            {
                Index = 0,
                CollidesWith = [PhysicsCollisionLayers.DefaultLayerName]
            }
        };
}

public sealed class PhysicsCollisionLayerConfig
{
    public int Index { get; set; }
    public string[] CollidesWith { get; set; } = [];
}

public readonly record struct PhysicsLayer(uint Bit, uint Mask);

public sealed class PhysicsCollisionLayers
{
    public const string DefaultLayerName = "default";

    private readonly Dictionary<string, PhysicsLayer> _layers;

    internal PhysicsCollisionLayers(
        Dictionary<string, PhysicsLayer> layers)
    {
        _layers = new Dictionary<string, PhysicsLayer>(
            layers,
            StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<string> Names => _layers.Keys;

    public bool TryResolve(string? name, out PhysicsLayer layer)
    {
        if (name is not null && _layers.TryGetValue(name, out layer))
            return true;

        layer = default;
        return false;
    }

    public PhysicsLayer Resolve(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (_layers.TryGetValue(name, out var layer))
            return layer;

        throw new KeyNotFoundException(
            $"Unknown physics collision layer '{name}'.");
    }
}

public readonly record struct PhysicsSettings(
    float FixedDeltaTime,
    int MaxSubSteps,
    float MaxFrameDelta,
    Vector3 Gravity,
    bool AllowSleeping,
    bool InterpolationEnabled,
    int SolverIterations,
    int SolverRelaxationIterations,
    PhysicsCollisionLayers CollisionLayers)
{
    public static PhysicsSettings Default { get; } =
        new PhysicsConfig().ToSettings();
}
