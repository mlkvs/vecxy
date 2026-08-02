using System.Numerics;
using Vecxy.Assets;
using Vecxy.Kernel;
using Vecxy.Rendering;

namespace Vecxy.UI;

internal static class UiBitmapFont
{
    public static Vector2 Measure(UiFontAsset font, string text, float fontSize)
    {
        var scale = fontSize / font.SourceSize;
        var width = 0.0f;
        var maxWidth = 0.0f;
        var lines = 1;
        var previous = -1;

        foreach (var character in text)
        {
            if (character == '\r')
                continue;
            if (character == '\n')
            {
                maxWidth = Math.Max(maxWidth, width);
                width = 0.0f;
                previous = -1;
                lines++;
                continue;
            }

            var codepoint = (int)character;
            if (!font.Glyphs.TryGetValue(codepoint, out var glyph) &&
                !font.Glyphs.TryGetValue('?', out glyph))
                continue;
            if (previous >= 0)
                width += font.GetKerning(previous, codepoint) * scale;
            width += glyph.XAdvance * scale;
            previous = codepoint;
        }

        return new Vector2(
            Math.Max(maxWidth, width),
            lines * font.LineHeight * scale);
    }

    public static void Paint(
        UiRenderer renderer,
        UiFontAsset font,
        Texture texture,
        string text,
        Rect bounds,
        float fontSize,
        Vector4 color,
        Rect clip)
    {
        var textureWidth = Math.Max(1, font.TextureWidth);
        var textureHeight = Math.Max(1, font.TextureHeight);
        var scale = fontSize / font.SourceSize;
        var x = bounds.X;
        var y = bounds.Y;
        var lineStart = x;
        var previous = -1;

        foreach (var character in text)
        {
            if (character == '\r')
                continue;
            if (character == '\n')
            {
                x = lineStart;
                y += font.LineHeight * scale;
                previous = -1;
                continue;
            }

            var codepoint = (int)character;
            if (!font.Glyphs.TryGetValue(codepoint, out var glyph) &&
                !font.Glyphs.TryGetValue('?', out glyph))
                continue;
            if (previous >= 0)
                x += font.GetKerning(previous, codepoint) * scale;

            if (glyph.Width > 0 && glyph.Height > 0)
            {
                renderer.AddTextured(
                    new Rect(
                        x + glyph.XOffset * scale,
                        y + glyph.YOffset * scale,
                        glyph.Width * scale,
                        glyph.Height * scale),
                    color,
                    texture,
                    new Vector4(
                        glyph.X / textureWidth,
                        glyph.Y / textureHeight,
                        (glyph.X + glyph.Width) / textureWidth,
                        (glyph.Y + glyph.Height) / textureHeight),
                    clip,
                    TextureSamplerState.LinearClamp);
            }

            x += glyph.XAdvance * scale;
            previous = codepoint;
        }
    }
}
