using System.Numerics;
using System.Runtime.InteropServices;

namespace Vecxy.Rendering;

public readonly record struct Color(float R, float G, float B, float A = 1f)
{
    public static Color Black => new(0f, 0f, 0f, 1f);
    public static Color White => new(1f, 1f, 1f, 1f);
    public static Color CornflowerBlue => new(0.1f, 0.2f, 0.35f, 1f);
}

public readonly record struct RenderStats(
    int SubmittedObjects,
    int DrawCalls,
    int Triangles,
    int MaterialSwitches,
    int InstancedBatches,
    int Instances,
    int StaticBatches);

[StructLayout(LayoutKind.Sequential)]
public readonly struct Vertex(Vector3 position, Color color, Vector2 texCoord)
{
    public readonly Vector3 Position = position;
    public readonly Vector3 Normal = Vector3.UnitY;
    public readonly Color Color = color;
    public readonly Vector2 TexCoord = texCoord;

    public Vertex(Vector3 position, Vector3 normal, Color color, Vector2 texCoord)
        : this(position, color, texCoord) => Normal = normal;
}

public enum TextureFilter { Nearest, Linear }
public enum TextureWrap { Repeat, Clamp }

public readonly record struct TextureOptions(
    TextureFilter Filter = TextureFilter.Linear,
    TextureWrap Wrap = TextureWrap.Repeat);

public readonly record struct Bounds3(Vector3 Min, Vector3 Max)
{
    public Bounds3 Transform(Matrix4x4 transform)
    {
        Span<Vector3> corners =
        [
            new(Min.X, Min.Y, Min.Z), new(Max.X, Min.Y, Min.Z),
            new(Min.X, Max.Y, Min.Z), new(Max.X, Max.Y, Min.Z),
            new(Min.X, Min.Y, Max.Z), new(Max.X, Min.Y, Max.Z),
            new(Min.X, Max.Y, Max.Z), new(Max.X, Max.Y, Max.Z)
        ];
        var min = new Vector3(float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity);
        foreach (var corner in corners)
        {
            var point = Vector3.Transform(corner, transform);
            min = Vector3.Min(min, point);
            max = Vector3.Max(max, point);
        }
        return new Bounds3(min, max);
    }

    public bool Intersects(Ray3 ray, out float distance)
    {
        var tMin = 0f;
        var tMax = float.PositiveInfinity;
        for (var axis = 0; axis < 3; axis++)
        {
            var origin = axis == 0 ? ray.Origin.X : axis == 1 ? ray.Origin.Y : ray.Origin.Z;
            var direction = axis == 0 ? ray.Direction.X : axis == 1 ? ray.Direction.Y : ray.Direction.Z;
            var min = axis == 0 ? Min.X : axis == 1 ? Min.Y : Min.Z;
            var max = axis == 0 ? Max.X : axis == 1 ? Max.Y : Max.Z;
            if (MathF.Abs(direction) < 1e-7f)
            {
                if (origin < min || origin > max) { distance = 0f; return false; }
                continue;
            }
            var inverse = 1f / direction;
            var near = (min - origin) * inverse;
            var far = (max - origin) * inverse;
            if (near > far) (near, far) = (far, near);
            tMin = MathF.Max(tMin, near);
            tMax = MathF.Min(tMax, far);
            if (tMin > tMax) { distance = 0f; return false; }
        }
        distance = tMin;
        return true;
    }
}

public readonly record struct Ray3(Vector3 Origin, Vector3 Direction)
{
    public static Ray3 FromScreen(Vector2 screenPosition, int width, int height, IRenderCamera camera)
    {
        var x = screenPosition.X / Math.Max(1, width) * 2f - 1f;
        var y = 1f - screenPosition.Y / Math.Max(1, height) * 2f;
        if (!Matrix4x4.Invert(camera.GetViewProjection(width, height), out var inverse))
            throw new InvalidOperationException("Camera view-projection matrix is not invertible.");
        var near = Vector4.Transform(new Vector4(x, y, -1f, 1f), inverse);
        var far = Vector4.Transform(new Vector4(x, y, 1f, 1f), inverse);
        var nearPoint = new Vector3(near.X, near.Y, near.Z) / near.W;
        var farPoint = new Vector3(far.X, far.Y, far.Z) / far.W;
        return new Ray3(nearPoint, Vector3.Normalize(farPoint - nearPoint));
    }
}

public interface IRenderCamera
{
    Matrix4x4 GetViewProjection(int width, int height);
}

public readonly record struct Camera2D(Vector2 Position, float Rotation, float Zoom) : IRenderCamera
{
    public static Camera2D Default => new(Vector2.Zero, 0f, 1f);

    public Matrix4x4 GetViewProjection(int width, int height)
    {
        var safeHeight = Math.Max(1, height);
        var aspect = Math.Max(1, width) / (float)safeHeight;
        var halfHeight = 1f / Math.Max(0.0001f, Zoom);
        var projection = Matrix4x4.CreateOrthographicOffCenter(
            -aspect * halfHeight, aspect * halfHeight, -halfHeight, halfHeight, -1f, 1f);
        var view = Matrix4x4.CreateTranslation(-Position.X, -Position.Y, 0f) *
                   Matrix4x4.CreateRotationZ(-Rotation);
        return view * projection;
    }
}

public sealed class Camera3D : IRenderCamera
{
    public Vector3 Position { get; set; } = new(0f, 2f, 8f);
    public Quaternion Rotation { get; set; } = Quaternion.Identity;
    public float FieldOfView { get; set; } = MathF.PI / 3f;
    public float NearPlane { get; set; } = 0.05f;
    public float FarPlane { get; set; } = 1000f;

    public Vector3 Forward => Vector3.Normalize(Vector3.Transform(-Vector3.UnitZ, Rotation));
    public Vector3 Right => Vector3.Normalize(Vector3.Cross(Forward, Vector3.UnitY));

    public Matrix4x4 GetViewProjection(int width, int height)
    {
        var aspect = Math.Max(1, width) / (float)Math.Max(1, height);
        var view = Matrix4x4.CreateLookAt(Position, Position + Forward, Vector3.UnitY);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(FieldOfView, aspect, NearPlane, FarPlane);
        return view * projection;
    }
}
