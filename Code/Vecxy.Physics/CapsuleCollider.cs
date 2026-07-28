using System.Numerics;
using Vecxy.Scene;

namespace Vecxy.Physics;

public sealed class CapsuleCollider : Collider
{
    private float _radius = 0.35f;
    private float _height = 1.1f;
    private Vector3 _center;

    public float Radius
    {
        get => _radius;
        set
        {
            if (value <= 0.0f)
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Capsule collider radius must be positive.");

            if (Math.Abs(_radius - value) <= float.Epsilon)
                return;

            _radius = value;
            NotifyChanged();
        }
    }

    public float Height
    {
        get => _height;
        set
        {
            if (value < 0.0f)
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Capsule collider height cannot be negative.");

            if (Math.Abs(_height - value) <= float.Epsilon)
                return;

            _height = value;
            NotifyChanged();
        }
    }

    public Vector3 Center
    {
        get => _center;
        set
        {
            if (_center == value)
                return;

            _center = value;
            NotifyChanged();
        }
    }

    public override void OnGizmos(ISceneGizmoDrawer gizmos)
    {
        var upOffset = Vector3.UnitY * (Height * 0.5f);
        var topLocal = Center + upOffset;
        var bottomLocal = Center - upOffset;

        var top = Vector3.Transform(topLocal, Transform.WorldMatrix);
        var bottom = Vector3.Transform(bottomLocal, Transform.WorldMatrix);

        gizmos.WireSphere(
            top,
            Radius,
            new Vector4(0.25f, 0.8f, 1.0f, 1.0f),
            20,
            1.5f);
        gizmos.WireSphere(
            bottom,
            Radius,
            new Vector4(0.25f, 0.8f, 1.0f, 1.0f),
            20,
            1.5f);

        var right = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitX, Transform.WorldMatrix));
        var forward = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, Transform.WorldMatrix));

        gizmos.Line(top + right * Radius, bottom + right * Radius, new Vector4(0.25f, 0.8f, 1.0f, 1.0f), 1.5f);
        gizmos.Line(top - right * Radius, bottom - right * Radius, new Vector4(0.25f, 0.8f, 1.0f, 1.0f), 1.5f);
        gizmos.Line(top + forward * Radius, bottom + forward * Radius, new Vector4(0.25f, 0.8f, 1.0f, 1.0f), 1.5f);
        gizmos.Line(top - forward * Radius, bottom - forward * Radius, new Vector4(0.25f, 0.8f, 1.0f, 1.0f), 1.5f);
    }
}
