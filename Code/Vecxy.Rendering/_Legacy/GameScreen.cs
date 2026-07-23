using System.Numerics;

namespace Vecxy.Rendering;

public readonly record struct ScreenRect(int X, int Y, int Width, int Height)
{
    public bool Contains(Vector2 point) => point.X >= X && point.Y >= Y && point.X < X + Width && point.Y < Y + Height;
}

public sealed class GameScreen
{
    public ScreenRect Bounds { get; private set; }
    public float AspectRatio { get; private set; } = 16f / 9f;
    public void FillWindow(int width, int height) => Bounds = new(0, 0, Math.Max(1, width), Math.Max(1, height));
    public void SetBounds(int x, int y, int width, int height) => Bounds = new(x, y, Math.Max(1, width), Math.Max(1, height));
    public void FitInside(ScreenRect available, float aspectRatio)
    {
        AspectRatio = aspectRatio;
        var width = available.Width;
        var height = Math.Max(1, (int)MathF.Round(width / aspectRatio));
        if (height > available.Height)
        {
            height = available.Height;
            width = Math.Max(1, (int)MathF.Round(height * aspectRatio));
        }
        SetBounds(available.X + (available.Width - width) / 2, available.Y + (available.Height - height) / 2, width, height);
    }
    public Vector2 WindowToLocal(Vector2 point) => point - new Vector2(Bounds.X, Bounds.Y);
}
