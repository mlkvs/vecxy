using System.Numerics;

namespace Vecxy.UI;

internal readonly record struct UiWorldPlane(Matrix4x4 CanvasToWorld, int Width, int Height)
{
    public Vector3 Center => Vector3.Transform(new Vector3(Width * .5f, Height * .5f, 0), CanvasToWorld);

    public static UiWorldPlane FromBounds(Vector3 min, Vector3 max, Matrix4x4 model, float pixelsPerMeter)
    {
        if (!float.IsFinite(pixelsPerMeter) || pixelsPerMeter <= 0) throw new ArgumentOutOfRangeException(nameof(pixelsPerMeter));
        var size = max - min;
        var thin = size.Z <= size.X && size.Z <= size.Y ? 2 : size.Y <= size.X ? 1 : 0;
        var rightAxis = thin == 0 ? Vector3.UnitY : Vector3.UnitX;
        var upAxis = thin == 2 ? Vector3.UnitY : thin == 1 ? -Vector3.UnitZ : Vector3.UnitZ;
        var rightSize = thin == 0 ? size.Y : size.X;
        var upSize = thin == 2 ? size.Y : size.Z;
        var thickness = thin == 0 ? size.X : thin == 1 ? size.Y : size.Z;
        if (rightSize <= 0 || upSize <= 0 || thickness > Math.Min(rightSize, upSize) * .01f)
            throw new InvalidOperationException("World UI requires a flat rectangular plane, not a volume.");
        var right = Vector3.TransformNormal(rightAxis * rightSize, model);
        var up = Vector3.TransformNormal(upAxis * upSize, model);
        var normal = Vector3.Cross(right, up);
        if (!float.IsFinite(normal.LengthSquared()) || normal.LengthSquared() < 1e-12f)
            throw new InvalidOperationException("World UI plane has a singular transform.");
        var width = Math.Clamp((int)Math.Clamp(Math.Ceiling(right.Length() * pixelsPerMeter), 1, 32768), 1, 32768);
        var height = Math.Clamp((int)Math.Clamp(Math.Ceiling(up.Length() * pixelsPerMeter), 1, 32768), 1, 32768);
        var topLeft = Vector3.Transform((min + max) * .5f, model) - right * .5f + up * .5f;
        right /= width; up /= -height; normal = Vector3.Normalize(normal);
        return new UiWorldPlane(new Matrix4x4(
            right.X, right.Y, right.Z, 0, up.X, up.Y, up.Z, 0,
            normal.X, normal.Y, normal.Z, 0, topLeft.X, topLeft.Y, topLeft.Z, 1), width, height);
    }

    public bool TryRaycast(Vector3 origin, Vector3 direction, float maxDistance, bool clip, bool twoSided, out Vector2 point, out float distance)
    {
        point = default; distance = 0;
        if (!Matrix4x4.Invert(CanvasToWorld, out var inverse)) return false;
        var localOrigin = Vector3.Transform(origin, inverse);
        var localDirection = Vector3.TransformNormal(direction, inverse);
        if (Math.Abs(localDirection.Z) < 1e-7f || !twoSided && localDirection.Z >= 0) return false;
        distance = -localOrigin.Z / localDirection.Z;
        if (!float.IsFinite(distance) || distance < 0 || distance > maxDistance) return false;
        var local = localOrigin + localDirection * distance;
        point = new Vector2(local.X, local.Y);
        return !clip || point.X >= 0 && point.Y >= 0 && point.X <= Width && point.Y <= Height;
    }
}
