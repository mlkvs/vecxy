using System.Numerics;
using Vecxy.Scene;

namespace Vecxy.Physics;

public sealed class CapsuleCollider : Collider
{
    private float _radius = 0.35f;
    private float _height = 1.1f;

    public float Radius
    {
        get => _radius;
        set
        {
            if (!float.IsFinite(value) || value <= 0.0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Capsule collider radius must be positive.");
            }

            _radius = value;
        }
    }

    public float Height
    {
        get => _height;
        set
        {
            if (!float.IsFinite(value) || value < 0.0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Capsule collider height cannot be negative.");
            }

            _height = value;
        }
    }

    public override void OnGizmos(ISceneGizmoDrawer gizmos)
    {
        var localRotation = Matrix4x4.CreateFromQuaternion(Rotation);
        var up = Vector3.TransformNormal(Vector3.UnitY, localRotation);
        var upOffset = up * (Height * 0.5f);
        var top = Vector3.Transform(Center + upOffset, Transform.WorldMatrix);
        var bottom = Vector3.Transform(Center - upOffset, Transform.WorldMatrix);
        var worldRotation = Quaternion.Normalize(
            Rotation * Transform.WorldRotation);
        var right = Vector3.Transform(Vector3.UnitX, worldRotation);
        var forward = Vector3.Transform(Vector3.UnitZ, worldRotation);

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

        var color = new Vector4(0.25f, 0.8f, 1.0f, 1.0f);
        gizmos.Line(top + right * Radius, bottom + right * Radius, color, 1.5f);
        gizmos.Line(top - right * Radius, bottom - right * Radius, color, 1.5f);
        gizmos.Line(top + forward * Radius, bottom + forward * Radius, color, 1.5f);
        gizmos.Line(top - forward * Radius, bottom - forward * Radius, color, 1.5f);
    }
}
