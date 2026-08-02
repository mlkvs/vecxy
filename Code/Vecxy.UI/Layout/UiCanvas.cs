using System.Globalization;

namespace Vecxy.UI;

internal readonly record struct UiCanvas(float Scale, int Width, int Height)
{
    public static UiCanvas Resolve(UiElement root, int outputWidth, int outputHeight)
    {
        outputWidth = Math.Max(1, outputWidth);
        outputHeight = Math.Max(1, outputHeight);

        var mode = root.Attributes.GetValueOrDefault("scale-mode");
        if (mode is not ("scale-with-screen" or "scale"))
            return new UiCanvas(1.0f, outputWidth, outputHeight);

        if (!TryPositive(root.Attributes.GetValueOrDefault("reference-width"), out var referenceWidth) ||
            !TryPositive(root.Attributes.GetValueOrDefault("reference-height"), out var referenceHeight))
            return new UiCanvas(1.0f, outputWidth, outputHeight);

        var scale = Math.Max(
            0.0001f,
            Math.Min(outputWidth / referenceWidth, outputHeight / referenceHeight));
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
