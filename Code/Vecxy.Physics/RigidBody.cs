using System.Numerics;
using Vecxy.Scene;

namespace Vecxy.Physics;

public sealed class RigidBody : AComponent
{
    private EPhysicsMotionType _motionType = EPhysicsMotionType.Static;
    private float _mass = 1.0f;
    private float _friction = 0.5f;
    private float _restitution;
    private bool _affectedByGravity = true;
    private bool _enableSpeculativeContacts;
    private Vector3 _velocity;
    private Vector3 _angularVelocity;

    internal Jitter2.Dynamics.RigidBody? NativeBody { get; set; }

    public EPhysicsMotionType MotionType
    {
        get => _motionType;
        set => _motionType = value;
    }

    public bool AffectedByGravity
    {
        get => _affectedByGravity;
        set => _affectedByGravity = value;
    }

    public float Mass
    {
        get => _mass;
        set
        {
            if (value <= 0.0f)
                throw new ArgumentOutOfRangeException(nameof(value));

            _mass = value;
        }
    }

    public float Friction
    {
        get => _friction;
        set
        {
            if (value < 0.0f)
                throw new ArgumentOutOfRangeException(nameof(value));

            _friction = value;
        }
    }

    public float Restitution
    {
        get => _restitution;
        set
        {
            if (value < 0.0f || value > 1.0f)
                throw new ArgumentOutOfRangeException(nameof(value));

            _restitution = value;
        }
    }

    public bool EnableSpeculativeContacts
    {
        get => _enableSpeculativeContacts;
        set => _enableSpeculativeContacts = value;
    }

    public Vector3 Velocity
    {
        get => NativeBody is null
            ? _velocity
            : PhysicsShapeFactory.ToVector3(NativeBody.Velocity);
        set
        {
            _velocity = value;

            if (NativeBody is not null &&
                NativeBody.MotionType != Jitter2.Dynamics.MotionType.Static)
            {
                NativeBody.Velocity = PhysicsShapeFactory.ToJVector(value);
            }
        }
    }

    public Vector3 AngularVelocity
    {
        get => NativeBody is null
            ? _angularVelocity
            : PhysicsShapeFactory.ToVector3(NativeBody.AngularVelocity);
        set
        {
            _angularVelocity = value;

            if (NativeBody is not null &&
                NativeBody.MotionType != Jitter2.Dynamics.MotionType.Static)
            {
                NativeBody.AngularVelocity = PhysicsShapeFactory.ToJVector(value);
            }
        }
    }
}
