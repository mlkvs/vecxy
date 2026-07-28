using System.Numerics;
using Vecxy.Scene;

namespace Vecxy.Physics;

[SingleComponent]
public sealed class RigidBody : AComponent
{
    private EPhysicsMotionType _motionType = EPhysicsMotionType.Dynamic;
    private float _mass = 1.0f;
    private float _linearDamping;
    private float _angularDamping;
    private Vector3 _velocity;
    private Vector3 _angularVelocity;
    private int _velocityVersion;
    private int _angularVelocityVersion;

    public EPhysicsMotionType MotionType
    {
        get => _motionType;
        set
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value));

            _motionType = value;
        }
    }

    public bool AffectedByGravity { get; set; } = true;

    public float Mass
    {
        get => _mass;
        set
        {
            if (!float.IsFinite(value) || value <= 0.0f)
                throw new ArgumentOutOfRangeException(nameof(value));

            _mass = value;
        }
    }

    public float LinearDamping
    {
        get => _linearDamping;
        set
        {
            if (!float.IsFinite(value) || value is < 0.0f or > 1.0f)
                throw new ArgumentOutOfRangeException(nameof(value));

            _linearDamping = value;
        }
    }

    public float AngularDamping
    {
        get => _angularDamping;
        set
        {
            if (!float.IsFinite(value) || value is < 0.0f or > 1.0f)
                throw new ArgumentOutOfRangeException(nameof(value));

            _angularDamping = value;
        }
    }

    public bool EnableSpeculativeContacts { get; set; }

    public Vector3 Velocity
    {
        get => _velocity;
        set
        {
            ThrowIfNotFinite(value, nameof(value));
            _velocity = value;
            _velocityVersion++;
        }
    }

    public Vector3 AngularVelocity
    {
        get => _angularVelocity;
        set
        {
            ThrowIfNotFinite(value, nameof(value));
            _angularVelocity = value;
            _angularVelocityVersion++;
        }
    }

    internal int VelocityVersion => _velocityVersion;
    internal int AngularVelocityVersion => _angularVelocityVersion;

    internal void SetSimulationVelocity(
        Vector3 velocity,
        Vector3 angularVelocity)
    {
        _velocity = velocity;
        _angularVelocity = angularVelocity;
    }

    private static void ThrowIfNotFinite(
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
            "Velocity must be finite.",
            parameterName);
    }
}
