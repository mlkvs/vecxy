using System.Numerics;
using Vecxy.Scene;

namespace Vecxy.Physics;

public sealed class BoxCollider : Collider
{
    private Vector3 _size = Vector3.One;
    private Vector3 _center;

    public Vector3 Size
    {
        get => _size;
        set
        {
            if (value.X <= 0 || value.Y <= 0 || value.Z <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Box collider size must be positive on all axes.");

            _size = value;
            
            NotifyChanged();
        }
    }

    public Vector3 Center
    {
        get => _center;
        set
        {
            _center = value;
            NotifyChanged();
        }
    }

    public override void OnGizmos(ISceneGizmoDrawer gizmos)
    {
        var transform =
            Matrix4x4.CreateTranslation(Center) *
            Transform.WorldMatrix;

        gizmos.WireBox(
            transform,
            Size,
            new Vector4(0.2f, 1.0f, 0.35f, 1.0f),
            1.5f);
    }
}
