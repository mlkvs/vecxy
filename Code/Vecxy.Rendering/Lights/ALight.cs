using System.Numerics;
using Vecxy.Scene;

namespace Vecxy.Rendering;

public abstract class ALight : AComponent
{
    private Vector3 _color = Vector3.One;
    private float _intensity = 1.0f;
    private float _range;

    public Vector3 Color
    {
        get => _color;
        set => _color = new Vector3(
            Math.Max(0.0f, value.X),
            Math.Max(0.0f, value.Y),
            Math.Max(0.0f, value.Z));
    }

    public float Intensity
    {
        get => _intensity;
        set
        {
            if (value < 0.0f)
                throw new ArgumentOutOfRangeException(nameof(value));

            _intensity = value;
        }
    }

    public float Range
    {
        get => _range;
        set
        {
            if (value < 0.0f)
                throw new ArgumentOutOfRangeException(nameof(value));

            _range = value;
        }
    }

    protected Vector4 GizmoColor =>
        new(
            Math.Clamp(_color.X, 0.0f, 1.0f),
            Math.Clamp(_color.Y, 0.0f, 1.0f),
            Math.Clamp(_color.Z, 0.0f, 1.0f),
            1.0f);

    protected float GizmoScale(float fallback = 1.0f)
    {
        return _range > 0.0f
            ? _range
            : fallback;
    }
}
