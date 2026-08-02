using System.Numerics;
using Vecxy.Assets;
using Vecxy.Scene;

namespace Vecxy.Rendering;

/// <summary>
/// Draws a texture as a camera-facing quad in world space.
/// One world unit corresponds to one source pixel when PixelsPerUnit is 1.
/// </summary>
[SingleComponent]
public sealed class SpriteRenderer : AComponent, ILocalBoundsProvider
{
    private AssetRef<TextureAsset>? _texture;
    private float _pixelsPerUnit = 100.0f;
    private float _alphaCutoff = 0.001f;
    private Vector2 _pivot = new(0.5f, 0.5f);

    public bool IsConfigured => _texture is { HasError: false };

    public TextureAsset Texture =>
        TextureReference.Value;

    internal AssetRef<TextureAsset> TextureReference =>
        _texture ??
        throw new InvalidOperationException(
            "SpriteRenderer has no texture.");

    /// <summary>
    /// Number of source pixels represented by one local-space unit.
    /// </summary>
    public float PixelsPerUnit
    {
        get => _pixelsPerUnit;
        set
        {
            if (value <= 0.0f || float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value));

            _pixelsPerUnit = value;
        }
    }

    /// <summary>
    /// Normalized pivot measured from the bottom-left of the sprite.
    /// </summary>
    public Vector2 Pivot
    {
        get => _pivot;
        set
        {
            if (!float.IsFinite(value.X) ||
                !float.IsFinite(value.Y))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _pivot = value;
        }
    }

    public Vector4 Color { get; set; } = Vector4.One;

    public int SortingLayer { get; set; }

    public int OrderInLayer { get; set; }

    public bool FlipX { get; set; }

    public bool FlipY { get; set; }

    public float AlphaCutoff
    {
        get => _alphaCutoff;
        set
        {
            if (value is < 0.0f or > 1.0f)
                throw new ArgumentOutOfRangeException(nameof(value));

            _alphaCutoff = value;
        }
    }

    public TextureSamplerState Sampler { get; set; } =
        new(
            ETextureFilter.Linear,
            ETextureFilter.Linear,
            ETextureWrap.Clamp,
            ETextureWrap.Clamp);

    public Vector2 LocalSize
    {
        get
        {
            var asset = Texture;
            return new Vector2(asset.Width, asset.Height) / _pixelsPerUnit;
        }
    }

    public Vector3 LocalBoundsMin
    {
        get
        {
            var size = LocalSize;
            return new Vector3(
                -_pivot.X * size.X,
                -_pivot.Y * size.Y,
                0.0f);
        }
    }

    public Vector3 LocalBoundsMax
    {
        get
        {
            var size = LocalSize;
            var minimum = LocalBoundsMin;
            return minimum + new Vector3(size, 0.0f);
        }
    }

    public Vector3 LocalBoundsSize =>
        LocalBoundsMax - LocalBoundsMin;

    public Vector3 LocalBoundsCenter =>
        (LocalBoundsMin + LocalBoundsMax) * 0.5f;

    public void SetTexture(AssetRef<TextureAsset> texture)
    {
        ObjectDisposedException.ThrowIf(IsDestroyed, this);
        ArgumentNullException.ThrowIfNull(texture);

        var acquired = texture.Acquire();
        _texture?.Dispose();
        _texture = acquired;
    }

    public override void OnDestroy()
    {
        _texture?.Dispose();
        _texture = null;
    }
}
