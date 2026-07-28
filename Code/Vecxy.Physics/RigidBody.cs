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
        set
        {
            if (_motionType == value)
                return;

            _motionType = value;
            NotifyChanged();
        }
    }

    public bool AffectedByGravity
    {
        get => _affectedByGravity;
        set
        {
            if (_affectedByGravity == value)
                return;

            _affectedByGravity = value;
            NotifyChanged();
        }
    }

    public float Mass
    {
        get => _mass;
        set
        {
            if (value <= 0.0f)
                throw new ArgumentOutOfRangeException(nameof(value));

            if (Math.Abs(_mass - value) <= float.Epsilon)
                return;

            _mass = value;
            NotifyChanged();
        }
    }

    public float Friction
    {
        get => _friction;
        set
        {
            if (value < 0.0f)
                throw new ArgumentOutOfRangeException(nameof(value));

            if (Math.Abs(_friction - value) <= float.Epsilon)
                return;

            _friction = value;
            NotifyChanged();
        }
    }

    public float Restitution
    {
        get => _restitution;
        set
        {
            if (value < 0.0f || value > 1.0f)
                throw new ArgumentOutOfRangeException(nameof(value));

            if (Math.Abs(_restitution - value) <= float.Epsilon)
                return;

            _restitution = value;
            NotifyChanged();
        }
    }

    public bool EnableSpeculativeContacts
    {
        get => _enableSpeculativeContacts;
        set
        {
            if (_enableSpeculativeContacts == value)
                return;

            _enableSpeculativeContacts = value;
            NotifyChanged();
        }
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
