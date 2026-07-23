namespace Vecxy.Rendering._Legacy.UI;

internal static class UiTextMetrics
{
    public static float PixelScale(float fontSize) => MathF.Max(1f, fontSize / 7f);
    public static float Advance(float fontSize) => PixelScale(fontSize) * 6f;
    public static float Width(string text, float fontSize) => text.Length * Advance(fontSize);
    public static float Height(float fontSize) => PixelScale(fontSize) * 7f;
}
