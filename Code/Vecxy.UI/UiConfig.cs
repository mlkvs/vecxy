using Vecxy.Assets;

namespace Vecxy.UI;

public enum EUiScaleMode : byte
{
    Fit,
    Fill,
    Width,
    Height,
    PixelPerfect,
    None
}

public sealed class UiConfig : IYamlConfig
{
    public float[] ReferenceResolution { get; set; } = [1920.0f, 1080.0f];
    public string ScaleMode { get; set; } = "fit";
    public float ScrollSpeed { get; set; } = 48.0f;
    public float DragScrollThreshold { get; set; } = 8.0f;
    public float ScrollDeceleration { get; set; } = 2400.0f;
    public bool EnableShadows { get; set; } = true;
    public Dictionary<string, string> SpriteAtlases { get; set; } =
        new(StringComparer.Ordinal);

    public float ReferenceWidth => ReferenceResolution[0];
    public float ReferenceHeight => ReferenceResolution[1];

    public EUiScaleMode ParsedScaleMode => ParseScaleMode(ScaleMode);

    public void Validate()
    {
        if (ReferenceResolution is not { Length: 2 } ||
            ReferenceResolution.Any(value => !float.IsFinite(value) || value <= 0.0f))
        {
            throw new InvalidDataException(
                "UI referenceResolution must contain two finite positive values.");
        }

        _ = ParseScaleMode(ScaleMode);
        if (!float.IsFinite(ScrollSpeed) || ScrollSpeed <= 0.0f)
            throw new InvalidDataException("UI scrollSpeed must be finite and positive.");
        if (!float.IsFinite(DragScrollThreshold) || DragScrollThreshold < 0.0f)
            throw new InvalidDataException("UI dragScrollThreshold must be finite and non-negative.");
        if (!float.IsFinite(ScrollDeceleration) || ScrollDeceleration <= 0.0f)
            throw new InvalidDataException("UI scrollDeceleration must be finite and positive.");
        if (SpriteAtlases.Any(pair =>
                string.IsNullOrWhiteSpace(pair.Key) ||
                string.IsNullOrWhiteSpace(pair.Value)))
        {
            throw new InvalidDataException("UI spriteAtlases contains an empty name or path.");
        }
    }

    internal static EUiScaleMode ParseScaleMode(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "fit" or "scale" or "scale-with-screen" => EUiScaleMode.Fit,
            "fill" => EUiScaleMode.Fill,
            "width" => EUiScaleMode.Width,
            "height" => EUiScaleMode.Height,
            "pixel-perfect" or "pixelperfect" => EUiScaleMode.PixelPerfect,
            "none" or null or "" => EUiScaleMode.None,
            _ => throw new InvalidDataException($"Unknown UI scale mode '{value}'.")
        };
}
