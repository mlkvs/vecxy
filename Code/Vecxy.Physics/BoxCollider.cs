using System.Numerics;
using Vecxy.Scene;

namespace Vecxy.Physics;

public sealed class BoxCollider : Collider
{
    private Vector3 _size = Vector3.One;

    public Vector3 Size
    {
        get => _size;
        set
        {
            ThrowIfNotFinite(value, nameof(value));

            if (value.X <= 0.0f ||
                value.Y <= 0.0f ||
                value.Z <= 0.0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Box collider size must be positive on all axes.");
            }

            _size = value;
        }
    }

    public override void OnGizmos(ISceneGizmoDrawer gizmos)
    {
        var local =
            Matrix4x4.CreateFromQuaternion(Rotation) *
            Matrix4x4.CreateTranslation(Center);

        gizmos.WireBox(
            local * Transform.WorldMatrix,
            Size,
            new Vector4(0.2f, 1.0f, 0.35f, 1.0f),
            1.5f);
    }
}
