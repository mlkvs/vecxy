using Vecxy.Assets;

namespace Vecxy.Rendering;

public sealed class RetroPostProcessConfig : APostProcessConfig
{
    public int PixelWidth { get; set; } = 320;
    public float JitterStrength { get; set; }
    public float WarpStrength { get; set; }
    public float DriftStrength { get; set; }

    public override void Validate(string path)
    {
        if (PixelWidth <= 0)
        {
            throw new InvalidDataException(
                $"Retro post process config '{path}' must have positive pixelWidth.");
        }

        if (JitterStrength < 0.0f)
        {
            throw new InvalidDataException(
                $"Retro post process config '{path}' cannot have negative jitterStrength.");
        }

        if (WarpStrength < 0.0f)
        {
            throw new InvalidDataException(
                $"Retro post process config '{path}' cannot have negative warpStrength.");
        }

        if (DriftStrength < 0.0f)
        {
            throw new InvalidDataException(
                $"Retro post process config '{path}' cannot have negative driftStrength.");
        }
    }
}

public sealed class RetroPostProcessEffect
    : APostProcessEffect<RetroPostProcessConfig>
{
    public RetroPostProcessEffect()
        : base(new RetroPostProcessConfig
        {
            Enabled = false,
            Order = 100,
            PixelWidth = 320
        })
    {
    }

    public override string Name => "Retro";

    public override string ShaderPath => "Shaders/PostProcessing/Retro.glsl";

    public override void Apply(Shader shader, in PostProcessContext context)
    {
        var settings = Settings;
        shader.Set("uPixelWidth", settings.PixelWidth);
        shader.Set("uJitterStrength", settings.JitterStrength);
        shader.Set("uWarpStrength", settings.WarpStrength);
        shader.Set("uDriftStrength", settings.DriftStrength);
    }
}
