using System.Numerics;
using Vecxy.Kernel;

namespace Vecxy.UI;

internal static class UiFallbackFont
{
    private static readonly IReadOnlyDictionary<char, string> Glyphs =
        new Dictionary<char, string>
        {
            ['A']="011101000110001111111000110001", ['B']="111101000111110100011000111110",
            ['C']="011111000010000100001000001111", ['D']="111101000110001100011000111110",
            ['E']="111111000011110100001000011111", ['F']="111111000011110100001000010000",
            ['G']="011111000010000101111000101111", ['H']="100011000111111100011000110001",
            ['I']="111110010000100001000010011111", ['J']="001110001000010000101001001100",
            ['K']="100011001011100100101000110001", ['L']="100001000010000100001000011111",
            ['M']="100011101110101101011000110001", ['N']="100011100110101100111000110001",
            ['O']="011101000110001100011000101110", ['P']="111101000110001111101000010000",
            ['Q']="011101000110001101011001001101", ['R']="111101000110001111101001010001",
            ['S']="011111000001110000011000111110", ['T']="111110010000100001000010000100",
            ['U']="100011000110001100011000101110", ['V']="100011000110001100010101000100",
            ['W']="100011000110101101011010101010", ['X']="100011000101010001001010110001",
            ['Y']="100011000101010001000010000100", ['Z']="111110000100010001000100011111",
            ['0']="011101000110011101011100101110", ['1']="001000110000100001000010001110",
            ['2']="011101000100001001100100011111", ['3']="111100000100110000011000111110",
            ['4']="000100011001010100101111100010", ['5']="111111000011110000011000111110",
            ['6']="011101000010000111101000101110", ['7']="111110000100010001000100001000",
            ['8']="011101000101110100011000101110", ['9']="011101000110001011110000101110",
            ['-']="000000000000000111110000000000", ['_']="000000000000000000000000011111",
            ['.']="000000000000000000000110000110", [':']="000000110001100000000110001100",
            ['/']="000010001000100010001000010000", ['?']="011101000100010001000000000100",
            ['!']="001000010000100001000000000100", ['+']="000000010000100111110010000100"
        };

    public static Vector2 Measure(string text, float fontSize)
    {
        var scale = Math.Max(1.0f, fontSize / 6.0f);
        var lines = text.Replace("\r", string.Empty).Split('\n');
        return new Vector2(
            lines.Max(line => line.Length) * 6.0f * scale,
            lines.Length * 7.0f * scale);
    }

    public static void Paint(
        UiRenderer renderer,
        string text,
        Rect bounds,
        float fontSize,
        Vector4 color,
        Rect clip)
    {
        var scale = Math.Max(1.0f, fontSize / 6.0f);
        var originX = bounds.X;
        var x = originX;
        var y = bounds.Y;
        foreach (var sourceCharacter in text)
        {
            if (sourceCharacter == '\r')
                continue;
            if (sourceCharacter == '\n')
            {
                x = originX;
                y += 7.0f * scale;
                continue;
            }

            var character = char.ToUpperInvariant(sourceCharacter);
            if (character != ' ')
            {
                if (!Glyphs.TryGetValue(character, out var pixels))
                    pixels = "111111000110101101011000111111";
                for (var row = 0; row < 6; row++)
                for (var column = 0; column < 5; column++)
                {
                    if (pixels[row * 5 + column] == '1')
                    {
                        renderer.AddSolid(
                            new Rect(x + column * scale, y + row * scale, scale, scale),
                            color,
                            clip);
                    }
                }
            }

            x += 6.0f * scale;
        }
    }
}
