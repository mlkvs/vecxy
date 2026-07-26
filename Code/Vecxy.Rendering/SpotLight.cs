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
}
