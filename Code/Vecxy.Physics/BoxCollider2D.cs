using System.Numerics;
using Vecxy.Scene;

namespace Vecxy.Physics;

/// <summary>
/// A box in the object's local XY plane. It is represented as a thin 3D shape
/// internally, so it participates in the same raycasts as every other collider.
/// </summary>
public sealed class BoxCollider2D : Collider
{
    private Vector2 _size = Vector2.One;
    private float _depth = 0.01f;
    private Vector2 _padding;

    /// <summary>
    /// Keeps this collider fitted to the first local bounds provider on the
    /// same scene object, such as SpriteRenderer or MeshRenderer.
    /// </summary>
    public bool AutoFit { get; set; } = true;

    public Vector2 Padding
    {
        get => _padding;
        set
        {
            if (!float.IsFinite(value.X) ||
                !float.IsFinite(value.Y) ||
                value.X < 0.0f ||
                value.Y < 0.0f)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _padding = value;
        }
    }

    public Vector2 Size
    {
        get => _size;
        set
        {
            if (!float.IsFinite(value.X) ||
                !float.IsFinite(value.Y) ||
                value.X <= 0.0f ||
                value.Y <= 0.0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "2D box collider size must be finite and positive on both axes.");
            }

            _size = value;
        }
    }

    /// <summary>
    /// Technical Z thickness used by the 3D physics backend.
    /// </summary>
    public float Depth
    {
        get => _depth;
        set
        {
            if (!float.IsFinite(value) || value <= 0.0f)
                throw new ArgumentOutOfRangeException(nameof(value));

            _depth = value;
        }
    }

    public override void Awake()
    {
        RefreshBounds();
    }

    public override void LateUpdate(float deltaTime)
    {
        RefreshBounds();
    }

    public bool RefreshBounds()
    {
        if (!AutoFit || SceneObject is null)
            return false;

        var bounds = SceneObject.Components
            .OfType<ILocalBoundsProvider>()
            .FirstOrDefault();
        if (bounds is null)
            return false;

        var boundsSize = bounds.LocalBoundsSize;
        var boundsCenter = bounds.LocalBoundsCenter;
        var fittedSize = new Vector2(
            boundsSize.X + _padding.X * 2.0f,
            boundsSize.Y + _padding.Y * 2.0f);
        if (fittedSize.X <= float.Epsilon ||
            fittedSize.Y <= float.Epsilon)
        {
            return false;
        }

        if (_size != fittedSize)
            _size = fittedSize;

        var fittedCenter = new Vector3(
            boundsCenter.X,
            boundsCenter.Y,
            Center.Z);
        if (Center != fittedCenter)
            Center = fittedCenter;

        return true;
    }

    public override void OnGizmos(ISceneGizmoDrawer gizmos)
    {
        var local =
            Matrix4x4.CreateFromQuaternion(Rotation) *
            Matrix4x4.CreateTranslation(Center);

        gizmos.WireBox(
            local * Transform.WorldMatrix,
            new Vector3(Size, Depth),
            new Vector4(0.2f, 0.75f, 1.0f, 1.0f),
            1.5f);
    }
}
