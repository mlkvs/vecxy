using System.Globalization;

namespace Vecxy.UI;

internal readonly record struct UiCanvas(float Scale, int Width, int Height)
{
    public static UiCanvas Resolve(
        UiElement root,
        int outputWidth,
        int outputHeight,
        UiConfig? settings = null)
    {
        outputWidth = Math.Max(1, outputWidth);
        outputHeight = Math.Max(1, outputHeight);

        var configuredMode = settings?.ParsedScaleMode ?? EUiScaleMode.None;
        var modeSource = root.Attributes.GetValueOrDefault("scale-mode");
        var mode = string.IsNullOrWhiteSpace(modeSource)
            ? configuredMode
            : UiConfig.ParseScaleMode(modeSource);
        if (mode == EUiScaleMode.None)
            return new UiCanvas(1.0f, outputWidth, outputHeight);

        var referenceWidth = settings?.ReferenceWidth ?? 1920.0f;
        var referenceHeight = settings?.ReferenceHeight ?? 1080.0f;
        if (TryPositive(root.Attributes.GetValueOrDefault("reference-width"), out var rootWidth))
            referenceWidth = rootWidth;
        if (TryPositive(root.Attributes.GetValueOrDefault("reference-height"), out var rootHeight))
            referenceHeight = rootHeight;

        var widthScale = outputWidth / referenceWidth;
        var heightScale = outputHeight / referenceHeight;
        var scale = mode switch
        {
            EUiScaleMode.Fill => Math.Max(widthScale, heightScale),
            EUiScaleMode.Width => widthScale,
            EUiScaleMode.Height => heightScale,
            EUiScaleMode.PixelPerfect => Math.Max(1.0f, MathF.Floor(Math.Min(widthScale, heightScale))),
            _ => Math.Min(widthScale, heightScale)
        };
        scale = Math.Max(0.0001f, scale);
        return new UiCanvas(
            scale,
            Math.Max(1, (int)MathF.Ceiling(outputWidth / scale)),
            Math.Max(1, (int)MathF.Ceiling(outputHeight / scale)));
    }

    private static bool TryPositive(string? source, out float value) =>
        float.TryParse(
            source,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value) && value > 0.0f;
}
