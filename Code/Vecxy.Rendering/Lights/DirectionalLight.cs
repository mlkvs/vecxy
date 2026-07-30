using System.Numerics;
using Vecxy.Scene;

namespace Vecxy.Rendering;

public sealed class DirectionalLight : ALight
{
    public override void OnGizmos(ISceneGizmoDrawer gizmos)
    {
        var origin = Transform.WorldPosition;
        var forward = Vector3.Normalize(Transform.Forward);
        var up = Vector3.Normalize(Transform.Up);
        var right = Vector3.Normalize(Transform.Right);
        var length = 1.75f;
        var spacing = 0.45f;

        for (var index = -1; index <= 1; ++index)
        {
            var offset = right * spacing * index + up * spacing * 0.5f * index;
            var start = origin + offset;
            var end = start + forward * length;
            gizmos.Line(start, end, GizmoColor, 1.6f);

            var headBase = end - forward * 0.28f;
            gizmos.Line(end, headBase + right * 0.14f, GizmoColor, 1.3f);
            gizmos.Line(end, headBase - right * 0.14f, GizmoColor, 1.3f);
            gizmos.Line(end, headBase + up * 0.14f, GizmoColor, 1.3f);
            gizmos.Line(end, headBase - up * 0.14f, GizmoColor, 1.3f);
        }
    }
}
