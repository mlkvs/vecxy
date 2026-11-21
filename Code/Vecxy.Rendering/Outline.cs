using System.Numerics;
using Vecxy.Prototypes;
using Vecxy.Scene;

namespace Vecxy.Rendering;

public enum OutlineMode
{
    VisibleOnly = 0,
    Always = 1,
    OccludedOnly = 2,
    VisibleAndOccluded = 3
}

/// <summary>Draws a pixel-width silhouette around a mesh or a hierarchy of meshes.</summary>
[Authoring("Outline", Category = "Rendering", Key = "outline")]
public sealed class Outline : AComponent
{
    public const float MaxThickness = 12;
    private float _thickness = 2;
    private float _opacity = 1;
    private Vector3 _color = new(1, .68f, .2f);
    private Vector3 _occludedColor = new(1, .68f, .2f);
    private float _occludedOpacity = .45f;
    private OutlineMode _mode;

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

    public OutlineMode Mode
    {
        get => _mode;
        set
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            _mode = value;
        }
    }

    /// <summary>Compatibility switch for the original visible-only and through-wall modes.</summary>
    public bool ThroughWalls
    {
        get => Mode == OutlineMode.Always;
        set => Mode = value ? OutlineMode.Always : OutlineMode.VisibleOnly;
    }

    public Vector3 OccludedColor
    {
        get => _occludedColor;
        set
        {
            if (!InUnitRange(value.X) || !InUnitRange(value.Y) || !InUnitRange(value.Z))
                throw new ArgumentOutOfRangeException(nameof(value));
            _occludedColor = value;
        }
    }

    public float OccludedOpacity
    {
        get => _occludedOpacity;
        set
        {
            if (!InUnitRange(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            _occludedOpacity = value;
        }
    }

    public bool IncludeChildren { get; set; } = true;
    public int Order { get; set; }

    private static bool InUnitRange(float value) => float.IsFinite(value) && value is >= 0 and <= 1;

    public sealed class Prototype : APrototype<Outline, Prototype.Options>
    {
        public sealed class Options
        {
            private OutlineMode? _mode;

            [Authoring("Color (RGB)", Min = 0, Max = 1, Order = 0)]
            public Vector3 Color { get; set; } = new(1, .68f, .2f);

            [Authoring("Opacity", Min = 0, Max = 1, Order = 1)]
            public float Opacity { get; set; } = 1;

            [Authoring("Thickness", Min = 0, Max = MaxThickness, Unit = "px", Order = 2)]
            public float Thickness { get; set; } = 2;

            [Authoring("Mode", Order = 3, Description = "Visible only, always through walls, occluded only, or separate styles for visible and occluded edges.")]
            public OutlineMode Mode
            {
                get => _mode ?? (ThroughWalls ? OutlineMode.Always : OutlineMode.VisibleOnly);
                set => _mode = value;
            }

            public bool ThroughWalls { get; set; }

            [Authoring("Occluded color (RGB)", Min = 0, Max = 1, Order = 4, Description = "Used for hidden edges in VisibleAndOccluded mode.")]
            public Vector3 OccludedColor { get; set; } = new(1, .68f, .2f);

            [Authoring("Occluded opacity", Min = 0, Max = 1, Order = 5, Description = "Used for hidden edges in VisibleAndOccluded mode.")]
            public float OccludedOpacity { get; set; } = .45f;

            [Authoring("Include children", Order = 6)]
            public bool IncludeChildren { get; set; } = true;

            [Authoring("Layer order", Order = 7)]
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
            outline.Mode = options.Mode;
            outline.OccludedColor = options.OccludedColor;
            outline.OccludedOpacity = options.OccludedOpacity;
            outline.IncludeChildren = options.IncludeChildren;
            outline.Order = options.Order;
        }
    }
}
