namespace Vecxy.Physics;

public sealed class PhysicsMaterial
{
    private float _friction = 0.5f;
    private float _restitution;

    public float Friction
    {
        get => _friction;
        set
        {
            if (!float.IsFinite(value) || value < 0.0f)
                throw new ArgumentOutOfRangeException(nameof(value));

            _friction = value;
        }
    }

    public float Restitution
    {
        get => _restitution;
        set
        {
            if (!float.IsFinite(value) || value is < 0.0f or > 1.0f)
                throw new ArgumentOutOfRangeException(nameof(value));

            _restitution = value;
        }
    }
}
