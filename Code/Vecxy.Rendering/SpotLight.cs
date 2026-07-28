using System.Numerics;
using Vecxy.Scene;

namespace Vecxy.Rendering;

public sealed class SpotLight : Light
{
    private float _innerConeAngle;
    private float _outerConeAngle = MathF.PI / 4.0f;

    public float InnerConeAngle
    {
        get => _innerConeAngle;
        set
        {
            if (value < 0.0f || value > _outerConeAngle)
                throw new ArgumentOutOfRangeException(nameof(value));

            _innerConeAngle = value;
        }
    }

    public float OuterConeAngle
    {
        get => _outerConeAngle;
        set
        {
            if (value <= 0.0f || value > MathF.PI * 0.5f || value < _innerConeAngle)
                throw new ArgumentOutOfRangeException(nameof(value));

            _outerConeAngle = value;
        }
    }

    public override void OnGizmos(ISceneGizmoDrawer gizmos)
    {
        var origin = Transform.WorldPosition;
        var forward = Vector3.Normalize(Transform.Forward);
        var range = Math.Max(0.5f, GizmoScale(2.0f));
        var radius = MathF.Tan(_outerConeAngle) * range;
        var right = Vector3.Normalize(Transform.Right);
        var up = Vector3.Normalize(Transform.Up);
        var tip = origin + forward * range;

        gizmos.Line(origin, tip, GizmoColor, 1.5f);

        var ringPoints = new Vector3[8];
        for (var index = 0; index < ringPoints.Length; ++index)
        {
            var angle = MathF.Tau * index / ringPoints.Length;
            ringPoints[index] =
                tip +
                right * MathF.Cos(angle) * radius +
                up * MathF.Sin(angle) * radius;

            gizmos.Line(origin, ringPoints[index], GizmoColor, 1.2f);
        }

        for (var index = 0; index < ringPoints.Length; ++index)
        {
            var next = (index + 1) % ringPoints.Length;
            gizmos.Line(
                ringPoints[index],
                ringPoints[next],
                GizmoColor,
                1.2f);
        }
    }
}
