using System.Numerics;
using Vecxy.Scene;

namespace Vecxy.Physics;

public abstract class Collider : AComponent
{
    private Vector3 _center;
    private Quaternion _rotation = Quaternion.Identity;
    private PhysicsMaterial _material = new();
    private string _collisionLayer =
        PhysicsCollisionLayers.DefaultLayerName;

    public Vector3 Center
    {
        get => _center;
        set
        {
            ThrowIfNotFinite(value, nameof(value));
            _center = value;
        }
    }

    public Quaternion Rotation
    {
        get => _rotation;
        set
        {
            if (!IsFinite(value) ||
                value.LengthSquared() <= float.Epsilon)
            {
                throw new ArgumentException(
                    "Collider rotation must be a finite, non-zero quaternion.",
                    nameof(value));
            }

            _rotation = Quaternion.Normalize(value);
        }
    }

    public bool IsTrigger { get; set; }

    public string CollisionLayer
    {
        get => _collisionLayer;
        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            _collisionLayer = value;
        }
    }

    public PhysicsMaterial Material
    {
        get => _material;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _material = value;
        }
    }

    protected static void ThrowIfNotFinite(
        Vector3 value,
        string parameterName)
    {
        if (float.IsFinite(value.X) &&
            float.IsFinite(value.Y) &&
            float.IsFinite(value.Z))
        {
            return;
        }

        throw new ArgumentException(
            "Collider value must be finite.",
            parameterName);
    }

    private static bool IsFinite(Quaternion value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) &&
        float.IsFinite(value.W);
}
