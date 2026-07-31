using System.Numerics;
using Vecxy.Assets;

namespace Vecxy.Rendering;

public sealed class VignettePostProcessConfig : APostProcessConfig
{
    public bool RoundEnabled { get; set; }
    public bool SideMasksEnabled { get; set; }
    public float[]? Color { get; set; }
    public float Intensity { get; set; } = 1.0f;
    public float RoundRadius { get; set; } = 0.72f;
    public float RoundSoftness { get; set; } = 0.18f;
    public float Top { get; set; }
    public float Right { get; set; }
    public float Bottom { get; set; }
    public float Left { get; set; }
    public float EdgeSoftness { get; set; } = 0.02f;

    public Vector4 GetColor()
    {
        if (Color is null)
            return new Vector4(0.0f, 0.0f, 0.0f, 1.0f);

        if (Color.Length != 4)
        {
            throw new InvalidDataException(
                $"Vignette post process config color must contain four components.");
        }

        return new Vector4(Color[0], Color[1], Color[2], Color[3]);
    }

    public override void Validate()
    {
        _ = GetColor();

        if (Intensity < 0.0f || Intensity > 1.0f)
        {
            throw new InvalidDataException(
                $"Vignette post process config intensity must be between 0 and 1.");
        }

        if (RoundRadius < 0.0f || RoundRadius > 2.0f)
        {
            throw new InvalidDataException(
                $"Vignette post process config roundRadius must be between 0 and 2.");
        }

        if (RoundSoftness <= 0.0f || RoundSoftness > 1.0f)
        {
            throw new InvalidDataException(
                $"Vignette post process config roundSoftness must be between 0 and 1.");
        }

        ValidateSide(Top, nameof(Top));
        ValidateSide(Right, nameof(Right));
        ValidateSide(Bottom, nameof(Bottom));
        ValidateSide(Left, nameof(Left));

        if (EdgeSoftness <= 0.0f || EdgeSoftness > 0.25f)
        {
            throw new InvalidDataException(
                $"Vignette post process config edgeSoftness must be between 0 and 0.25.");
        }
    }

    private static void ValidateSide(
        float value,
        string name)
    {
        if (value is < 0.0f or > 1.0f)
        {
            throw new InvalidDataException(
                $"Vignette post process config {name} must be between 0 and 1.");
        }
    }
}

public sealed class VignettePostProcessEffect
    : APostProcessEffect<VignettePostProcessConfig>
{
    public VignettePostProcessEffect()
        : base(new VignettePostProcessConfig
        {
            Enabled = false,
            Order = 200,
            Color = [0.0f, 0.0f, 0.0f, 1.0f]
        })
    {
    }

    public override string Name => "Vignette";

    public override string ShaderPath => "Shaders/PostProcessing/Vignette.glsl";

    public override void Apply(Shader shader, in PostProcessContext context)
    {
        var settings = Settings;

        shader.Set("uVignetteEnabled", 1);
        shader.Set("uVignetteRoundEnabled", settings.RoundEnabled ? 1 : 0);
        shader.Set("uVignetteSidesEnabled", settings.SideMasksEnabled ? 1 : 0);
        shader.Set("uVignetteColor", settings.GetColor());
        shader.Set("uVignetteIntensity", settings.Intensity);
        shader.Set("uVignetteRoundRadius", settings.RoundRadius);
        shader.Set("uVignetteRoundSoftness", settings.RoundSoftness);
        shader.Set(
            "uVignetteSides",
            new Vector4(
                settings.Top,
                settings.Right,
                settings.Bottom,
                settings.Left));
        shader.Set("uVignetteEdgeSoftness", settings.EdgeSoftness);
    }
}
