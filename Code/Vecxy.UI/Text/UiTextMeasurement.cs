namespace Vecxy.UI;

/// <summary>Shared proportional-font measurement and caret hit-testing foundation.</summary>
public static class UiTextMeasurement
{
    public static float MeasureWidth(UiElement element, string text)
    {
        ArgumentNullException.ThrowIfNull(element);
        text ??= string.Empty;
        return element.Font is { } font
            ? UiBitmapFont.Measure(element, font, text, element.ComputedStyle.FontSize).X
            : UiFallbackFont.Measure(element, text, element.ComputedStyle.FontSize).X;
    }

    public static float MeasureRange(UiElement element, string text, int start, int length)
    {
        ArgumentNullException.ThrowIfNull(text);
        start = TextNavigation.ClampBoundary(text, start);
        var end = TextNavigation.ClampBoundary(text, Math.Clamp(start + length, start, text.Length));
        return MeasureWidth(element, text[start..end]);
    }

    public static int HitTestCaret(UiElement element, string text, float x)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (x <= 0 || text.Length == 0) return 0;
        var previous = 0;
        foreach (var boundary in System.Globalization.StringInfo.ParseCombiningCharacters(text).Skip(1).Append(text.Length).Distinct())
        {
            var left = MeasureWidth(element, text[..previous]);
            var right = MeasureWidth(element, text[..boundary]);
            if (x <= (left + right) * 0.5f) return previous;
            previous = boundary;
        }
        return text.Length;
    }
}
