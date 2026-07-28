namespace Vecxy.Physics;

public sealed class SphereCollider : Collider
{
    private float _radius = 0.5f;

    public float Radius
    {
        get => _radius;
        set
        {
            if (value <= 0.0f)
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Sphere collider radius must be positive.");

            _radius = value;
            NotifyChanged();
        }
    }
}
