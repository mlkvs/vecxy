using System.Numerics;
using Vecxy.Assets;
using Vecxy.Prototypes;
using Vecxy.Scene;

namespace Vecxy.Rendering;

/// <summary>Projects a texture along local -Z onto opaque scene meshes within its box.</summary>
[Authoring("Decal", Category = "Rendering", Key = "decal")]
public sealed class Decal : AComponent
{
    private Vector3 _size = new(1, 1, 0.2f);
    private float _opacity = 1;
    private float _maxSurfaceAngle = 75;

    internal AssetRef<TextureAsset>? Texture { get; private set; }

    public Vector3 Size
    {
        get => _size;
        set
        {
            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z) ||
                value.X <= 0 || value.Y <= 0 || value.Z <= 0)
                throw new ArgumentOutOfRangeException(nameof(value));
            _size = value;
        }
    }

    public float Opacity
    {
        get => _opacity;
        set
        {
            if (!float.IsFinite(value) || value is < 0 or > 1)
                throw new ArgumentOutOfRangeException(nameof(value));
            _opacity = value;
        }
    }

    /// <summary>Maximum angle, in degrees, between the projector's +Z and the surface normal.</summary>
    public float MaxSurfaceAngle
    {
        get => _maxSurfaceAngle;
        set
        {
            if (!float.IsFinite(value) || value is < 0 or > 90)
                throw new ArgumentOutOfRangeException(nameof(value));
            _maxSurfaceAngle = value;
        }
    }

    public int Order { get; set; }

    public void SetTexture(AssetRef<TextureAsset>? texture)
    {
        ObjectDisposedException.ThrowIf(IsDestroyed, this);
        var previous = Texture;
        Texture = texture?.Acquire();
        previous?.Dispose();
    }

    public override void OnDestroy()
    {
        Texture?.Dispose();
        Texture = null;
    }

    public sealed class Prototype(IAssetsManager assets) : APrototype<Decal, Prototype.Options>
    {
        public sealed class Options
        {
            [Authoring("Texture", AssetType = "Texture2D", Order = 0)]
            public string Texture { get; set; } = "";
            [Authoring("Size", Min = .001, Unit = "m", Order = 1, Description = "Width, height and projection depth.")]
            public Vector3 Size { get; set; } = new(1, 1, .2f);
            [Authoring("Opacity", Min = 0, Max = 1, Order = 2)]
            public float Opacity { get; set; } = 1;
            [Authoring("Surface angle", Min = 0, Max = 90, Unit = "°", Order = 3)]
            public float MaxSurfaceAngle { get; set; } = 75;
            [Authoring("Layer order", Order = 4)]
            public int Order { get; set; }
        }

        protected override Decal Instantiate(IPrototypeContext context)
        {
            var scene = RequireContext<ScenePrototypeContext>(context);
            return (scene.Object ?? scene.CreateObject("Decal")).AddComponent<Decal>();
        }

        protected override void Configure(Decal decal, Options options)
        {
            decal.Size = options.Size;
            decal.Opacity = options.Opacity;
            decal.MaxSurfaceAngle = options.MaxSurfaceAngle;
            decal.Order = options.Order;
            if (string.IsNullOrWhiteSpace(options.Texture))
                return;
            using var texture = Guid.TryParse(options.Texture, out var id)
                ? assets.Load<TextureAsset>(new AssetId(id))
                : assets.Load<TextureAsset>(options.Texture);
            decal.SetTexture(texture);
        }
    }
}
