using System.Numerics;
using Vecxy.Prototypes;
using Vecxy.Scene;

namespace Vecxy.Rendering;

/// <summary>Draws a pixel-width silhouette around a mesh or a hierarchy of meshes.</summary>
[Authoring("Outline", Category = "Rendering", Key = "outline")]
public sealed class Outline : AComponent
{
    public const float MaxThickness = 12;
    private float _thickness = 2;
    private float _opacity = 1;
    private Vector3 _color = new(1, .68f, .2f);

    public Vector3 Color
    {
        get => _color;
        set
        {
            if (!InUnitRange(value.X) || !InUnitRange(value.Y) || !InUnitRange(value.Z))
                throw new ArgumentOutOfRangeException(nameof(value));
            _color = value;
        }
    }

    public float Opacity
    {
        get => _opacity;
        set
        {
            if (!InUnitRange(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            _opacity = value;
        }
    }

    /// <summary>Thickness in pixels of the camera's render target.</summary>
    public float Thickness
    {
        get => _thickness;
        set
        {
            if (!float.IsFinite(value) || value is < 0 or > MaxThickness)
                throw new ArgumentOutOfRangeException(nameof(value));
            _thickness = value;
        }
    }

    public bool ThroughWalls { get; set; }
    public bool IncludeChildren { get; set; } = true;
    public int Order { get; set; }

    private static bool InUnitRange(float value) => float.IsFinite(value) && value is >= 0 and <= 1;

    public sealed class Prototype : APrototype<Outline, Prototype.Options>
    {
        public sealed class Options
        {
            [Authoring("Color (RGB)", Min = 0, Max = 1, Order = 0)]
            public Vector3 Color { get; set; } = new(1, .68f, .2f);

            [Authoring("Opacity", Min = 0, Max = 1, Order = 1)]
            public float Opacity { get; set; } = 1;

            [Authoring("Thickness", Min = 0, Max = MaxThickness, Unit = "px", Order = 2)]
            public float Thickness { get; set; } = 2;

            [Authoring("Through walls", Order = 3)]
            public bool ThroughWalls { get; set; }

            [Authoring("Include children", Order = 4)]
            public bool IncludeChildren { get; set; } = true;

            [Authoring("Layer order", Order = 5)]
            public int Order { get; set; }
        }

        protected override Outline Instantiate(IPrototypeContext context)
        {
            var scene = RequireContext<ScenePrototypeContext>(context);
            return (scene.Object ?? scene.CreateObject("Outline")).AddComponent<Outline>();
        }

        protected override void Configure(Outline outline, Options options)
        {
            outline.Color = options.Color;
            outline.Opacity = options.Opacity;
            outline.Thickness = options.Thickness;
            outline.ThroughWalls = options.ThroughWalls;
            outline.IncludeChildren = options.IncludeChildren;
            outline.Order = options.Order;
        }
    }
}
