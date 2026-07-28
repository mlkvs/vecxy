using System.Numerics;
using Vecxy.Scene;

namespace Vecxy.Physics;

public sealed class SphereCollider : Collider
{
    private float _radius = 0.5f;

    public float Radius
    {
        get => _radius;
        set
        {
            if (!float.IsFinite(value) || value <= 0.0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Sphere collider radius must be positive.");
            }

            _radius = value;
        }
    }

    public override void OnGizmos(ISceneGizmoDrawer gizmos)
    {
        var center = Vector3.Transform(Center, Transform.WorldMatrix);
        var scale = Transform.WorldScale;
        var radius = Radius * Math.Max(
            Math.Abs(scale.X),
            Math.Max(Math.Abs(scale.Y), Math.Abs(scale.Z)));

        gizmos.WireSphere(
            center,
            radius,
            new Vector4(0.25f, 0.8f, 1.0f, 1.0f),
            20,
            1.5f);
    }
}
