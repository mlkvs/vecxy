using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using Vecxy.Kernel;

namespace Vecxy.UI;

public readonly record struct UiTransform(
    Vector2 Translation,
    Vector2 Scale,
    float RotationDegrees,
    Vector2 Origin)
{
    public static UiTransform Identity { get; } =
        new(Vector2.Zero, Vector2.One, 0.0f, new Vector2(0.5f));

    public Matrix3x2 ToMatrix(Rect bounds)
    {
        var origin = new Vector2(
            bounds.X + bounds.Width * Origin.X,
            bounds.Y + bounds.Height * Origin.Y);
        return Matrix3x2.CreateTranslation(-origin) *
               Matrix3x2.CreateScale(Scale) *
               Matrix3x2.CreateRotation(RotationDegrees * MathF.PI / 180.0f) *
               Matrix3x2.CreateTranslation(origin + Translation);
    }

    public static UiTransform Lerp(UiTransform from, UiTransform to, float amount) =>
        new(
            Vector2.Lerp(from.Translation, to.Translation, amount),
            Vector2.Lerp(from.Scale, to.Scale, amount),
            float.Lerp(from.RotationDegrees, to.RotationDegrees, amount),
            Vector2.Lerp(from.Origin, to.Origin, amount));
}

internal readonly record struct UiTransformDefinition(
    UiLength TranslateX,
    UiLength TranslateY,
    Vector2 Scale,
    float RotationDegrees,
    Vector2 Origin)
{
    public static UiTransformDefinition Identity { get; } =
        new(UiLength.Ui(0), UiLength.Ui(0), Vector2.One, 0.0f, new Vector2(0.5f));

    public UiTransform Resolve(float viewportWidth, float viewportHeight) =>
        new(
            new Vector2(
                UiLayout.ResolvePoints(TranslateX, viewportWidth, viewportHeight),
                UiLayout.ResolvePoints(TranslateY, viewportWidth, viewportHeight)),
            Scale,
            RotationDegrees,
            Origin);
}

internal static class UiTransformParser
{
    private static readonly Regex FunctionPattern = new(
        @"([A-Za-z0-9-]+)\s*\(([^\)]*)\)",
        RegexOptions.Compiled);

    public static UiTransformDefinition Parse(string source, Vector2 origin)
    {
        var result = UiTransformDefinition.Identity with { Origin = origin };
        if (source.Trim().Equals("none", StringComparison.OrdinalIgnoreCase))
            return result;

        foreach (Match match in FunctionPattern.Matches(source))
        {
            var name = match.Groups[1].Value.ToLowerInvariant();
            var values = match.Groups[2].Value
                .Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            switch (name)
            {
                case "scale" when values.Length > 0 && TryFloat(values[0], out var uniform):
                    result = result with
                    {
                        Scale = new Vector2(
                            uniform,
                            values.Length > 1 && TryFloat(values[1], out var vertical) ? vertical : uniform)
                    };
                    break;
                case "scalex" when values.Length > 0 && TryFloat(values[0], out var scaleX):
                    result = result with { Scale = result.Scale with { X = scaleX } };
                    break;
                case "scaley" when values.Length > 0 && TryFloat(values[0], out var scaleY):
                    result = result with { Scale = result.Scale with { Y = scaleY } };
                    break;
                case "translate" when values.Length > 0 && UiLength.TryParse(values[0], out var translateX):
                    result = result with
                    {
                        TranslateX = translateX,
                        TranslateY = values.Length > 1 && UiLength.TryParse(values[1], out var translateY)
                            ? translateY
                            : UiLength.Ui(0)
                    };
                    break;
                case "translatex" when values.Length > 0 && UiLength.TryParse(values[0], out var x):
                    result = result with { TranslateX = x };
                    break;
                case "translatey" when values.Length > 0 && UiLength.TryParse(values[0], out var y):
                    result = result with { TranslateY = y };
                    break;
                case "rotate" when values.Length > 0 && TryAngle(values[0], out var angle):
                    result = result with { RotationDegrees = angle };
                    break;
            }
        }
        return result;
    }

    public static Vector2 ParseOrigin(string source)
    {
        var values = source.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (values.Length == 0)
            return new Vector2(0.5f);
        var x = ParseOriginAxis(values[0], true);
        var y = values.Length > 1 ? ParseOriginAxis(values[1], false) : 0.5f;
        return new Vector2(x, y);
    }

    private static float ParseOriginAxis(string source, bool horizontal)
    {
        source = source.ToLowerInvariant();
        if (source is "left" or "top") return 0.0f;
        if (source is "right" or "bottom") return 1.0f;
        if (source == "center") return 0.5f;
        if (source.EndsWith('%') && TryFloat(source[..^1], out var percent))
            return percent * 0.01f;
        return horizontal ? 0.5f : 0.5f;
    }

    private static bool TryAngle(string source, out float degrees)
    {
        source = source.Trim().ToLowerInvariant();
        var multiplier = 1.0f;
        if (source.EndsWith("deg")) source = source[..^3];
        else if (source.EndsWith("rad")) { source = source[..^3]; multiplier = 180.0f / MathF.PI; }
        else if (source.EndsWith("turn")) { source = source[..^4]; multiplier = 360.0f; }
        if (TryFloat(source, out var value))
        {
            degrees = value * multiplier;
            return true;
        }
        degrees = 0.0f;
        return false;
    }

    private static bool TryFloat(string source, out float value) =>
        float.TryParse(source, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
