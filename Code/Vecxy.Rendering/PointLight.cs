using System.Numerics;
using Vecxy.Scene;

namespace Vecxy.Rendering;

public sealed class PointLight : Light
{
    public override void OnGizmos(ISceneGizmoDrawer gizmos)
    {
        var radius = Math.Max(0.15f, GizmoScale(0.75f));
        gizmos.WireSphere(
            Transform.WorldPosition,
            radius,
            GizmoColor,
            segments: 20,
            thickness: 1.5f);
        gizmos.Axes(
            Matrix4x4.CreateTranslation(Transform.WorldPosition),
            size: Math.Max(0.3f, radius * 0.2f),
            thickness: 1.5f);
    }
}
