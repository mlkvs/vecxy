using System.Numerics;
using Vecxy.Kernel;

namespace Vecxy.UI;

internal static class UiTextFit
{
    public static float Shrink(
        float fontSize,
        float minimumFontSize,
        Vector2 measuredSize,
        Rect availableBounds)
    {
        fontSize = Math.Max(1.0f, fontSize);
        minimumFontSize = Math.Clamp(minimumFontSize, 1.0f, fontSize);
        if (measuredSize.X <= 0.0f || measuredSize.Y <= 0.0f ||
            availableBounds.Width <= 0.0f || availableBounds.Height <= 0.0f)
            return fontSize;

        var widthScale = Math.Min(1.0f, availableBounds.Width / measuredSize.X);
        var heightScale = Math.Min(1.0f, availableBounds.Height / measuredSize.Y);
        return Math.Clamp(fontSize * Math.Min(widthScale, heightScale), minimumFontSize, fontSize);
    }
}
