using System.Numerics;
using Vecxy.Assets;
using Vecxy.Kernel;
using Vecxy.Rendering;

namespace Vecxy.UI;

internal static class UiBitmapFont
{
    public static Vector2 Measure(
        UiElement element,
        UiFontAsset font,
        string text,
        float fontSize,
        float maximumWidth = float.PositiveInfinity)
    {
        var lines = Lines(element, font, text, fontSize, maximumWidth);
        var width = 0.0f;
        foreach (var line in lines)
            width = Math.Max(width, MeasureUnwrapped(font, line, fontSize).X);
        return new Vector2(
            width,
            lines.Length * font.LineHeight * (fontSize / font.SourceSize));
    }

    private static Vector2 MeasureUnwrapped(UiFontAsset font, string text, float fontSize)
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
        UiElement element,
        UiFontAsset font,
        Texture texture,
        string text,
        Rect bounds,
        float fontSize,
        Vector4 color,
        Rect clip,
        string textAlign,
        string verticalAlign,
        bool wrap)
    {
        var textureWidth = Math.Max(1, font.TextureWidth);
        var textureHeight = Math.Max(1, font.TextureHeight);
        var scale = fontSize / font.SourceSize;
        var lines = Lines(
            element,
            font,
            text,
            fontSize,
            wrap ? bounds.Width : float.PositiveInfinity);
        var lineHeight = font.LineHeight * scale;
        var contentHeight = lines.Length * lineHeight;
        var y = AlignVertical(bounds, contentHeight, verticalAlign);

        foreach (var line in lines)
        {
            var lineWidth = MeasureUnwrapped(font, line, fontSize).X;
            var x = AlignHorizontal(bounds, lineWidth, textAlign);
            var previous = -1;

            foreach (var character in line)
            {
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
            y += lineHeight;
        }
    }

    private static string[] Lines(
        UiElement element,
        UiFontAsset font,
        string text,
        float fontSize,
        float maximumWidth)
    {
        if (element.TextLayoutCache.TryGet(font, text, fontSize, maximumWidth, out var cached))
            return cached;
        var lines = UiTextWrap.Lines(
            text,
            maximumWidth,
            line => MeasureUnwrapped(font, line, fontSize).X);
        element.TextLayoutCache.Store(font, text, fontSize, maximumWidth, lines);
        return lines;
    }

    private static float AlignHorizontal(Rect bounds, float width, string alignment) =>
        alignment switch
        {
            "center" => bounds.X + (bounds.Width - width) * 0.5f,
            "right" or "end" => bounds.Right - width,
            _ => bounds.X
        };

    private static float AlignVertical(Rect bounds, float height, string alignment) =>
        alignment switch
        {
            "middle" or "center" => bounds.Y + (bounds.Height - height) * 0.5f,
            "bottom" or "end" => bounds.Bottom - height,
            _ => bounds.Y
        };
}
