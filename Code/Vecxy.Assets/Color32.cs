using System.Numerics;

namespace Vecxy.Assets;

public readonly record struct Color32(byte R, byte G, byte B, byte A = 255)
{
    public int PackedRgb => R << 16 | G << 8 | B;

    public Vector4 ToVector4() =>
        new(R / 255.0f, G / 255.0f, B / 255.0f, A / 255.0f);

    public bool IsNearRgb(Color32 other, int tolerance)
    {
        if (tolerance < 0)
            throw new ArgumentOutOfRangeException(nameof(tolerance));

        return Math.Abs(R - other.R) <= tolerance &&
               Math.Abs(G - other.G) <= tolerance &&
               Math.Abs(B - other.B) <= tolerance;
    }

    public override string ToString() => $"{R}, {G}, {B}, {A}";
}
