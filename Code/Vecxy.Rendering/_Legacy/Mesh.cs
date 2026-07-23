using System.Runtime.InteropServices;
using Silk.NET.OpenGL;

namespace Vecxy.Rendering._Legacy;

public sealed class Mesh : IDisposable
{
    private readonly GraphicsDevice _device;
    private uint _vao;
    private uint _vertexBuffer;
    private uint _indexBuffer;
    private uint _instanceBuffer;
    private bool _disposed;
    private readonly System.Numerics.Vector3[] _positions;
    private readonly uint[] _indices;
    private readonly Vertex[] _vertices;

    internal uint IndexCount { get; }
    internal uint VertexCount { get; }
    public Bounds3 Bounds { get; }

    internal unsafe Mesh(GraphicsDevice device, ReadOnlySpan<Vertex> vertices, ReadOnlySpan<uint> indices)
    {
        if (vertices.IsEmpty) throw new ArgumentException("A mesh must contain vertices.", nameof(vertices));
        _device = device;
        device.EnsureReady();
        var gl = device.GL;
        _vao = gl.GenVertexArray();
        _vertexBuffer = gl.GenBuffer();
        _indexBuffer = indices.IsEmpty ? 0 : gl.GenBuffer();
        _instanceBuffer = gl.GenBuffer();
        VertexCount = (uint)vertices.Length;
        IndexCount = (uint)indices.Length;
        var min = new System.Numerics.Vector3(float.PositiveInfinity);
        var max = new System.Numerics.Vector3(float.NegativeInfinity);
        foreach (var vertex in vertices)
        {
            min = System.Numerics.Vector3.Min(min, vertex.Position);
            max = System.Numerics.Vector3.Max(max, vertex.Position);
        }
        Bounds = new Bounds3(min, max);
        _positions = vertices.ToArray().Select(vertex => vertex.Position).ToArray();
        _vertices = vertices.ToArray();
        _indices = indices.IsEmpty
            ? Enumerable.Range(0, vertices.Length).Select(i => (uint)i).ToArray()
            : indices.ToArray();

        gl.BindVertexArray(_vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);
        fixed (Vertex* data = vertices)
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * sizeof(Vertex)), data,
                BufferUsageARB.StaticDraw);

        if (!indices.IsEmpty)
        {
            gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _indexBuffer);
            fixed (uint* data = indices)
                gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(indices.Length * sizeof(uint)), data,
                    BufferUsageARB.StaticDraw);
        }

        var stride = (uint)Marshal.SizeOf<Vertex>();
        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 12);
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, stride, 24);
        gl.EnableVertexAttribArray(2);
        gl.VertexAttribPointer(3, 2, VertexAttribPointerType.Float, false, stride, 40);
        gl.EnableVertexAttribArray(3);

        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceBuffer);
        var matrixStride = (uint)Marshal.SizeOf<System.Numerics.Matrix4x4>();
        for (uint column = 0; column < 4; column++)
        {
            var location = 4 + column;
            gl.VertexAttribPointer(location, 4, VertexAttribPointerType.Float, false, matrixStride,
                (void*)(column * 16));
            gl.EnableVertexAttribArray(location);
            gl.VertexAttribDivisor(location, 1);
        }
        gl.BindVertexArray(0);
    }

    internal ReadOnlySpan<Vertex> Vertices => _vertices;
    internal ReadOnlySpan<uint> Indices => _indices;

    internal unsafe void DrawInstances(ReadOnlySpan<System.Numerics.Matrix4x4> transforms)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (transforms.IsEmpty) return;
        var gl = _device.GL;
        gl.BindVertexArray(_vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceBuffer);
        fixed (System.Numerics.Matrix4x4* data = transforms)
            gl.BufferData(BufferTargetARB.ArrayBuffer,
                (nuint)(transforms.Length * sizeof(System.Numerics.Matrix4x4)), data, BufferUsageARB.StreamDraw);
        if (IndexCount > 0)
            gl.DrawElementsInstanced(PrimitiveType.Triangles, IndexCount, DrawElementsType.UnsignedInt, null,
                (uint)transforms.Length);
        else
            gl.DrawArraysInstanced(PrimitiveType.Triangles, 0, VertexCount, (uint)transforms.Length);
    }

    public bool Intersects(Ray3 worldRay, System.Numerics.Matrix4x4 transform, out float distance)
    {
        distance = float.PositiveInfinity;
        if (!Bounds.Transform(transform).Intersects(worldRay, out _)) return false;
        if (!System.Numerics.Matrix4x4.Invert(transform, out var inverse)) return false;
        var localOrigin = System.Numerics.Vector3.Transform(worldRay.Origin, inverse);
        var localDirection = System.Numerics.Vector3.Normalize(
            System.Numerics.Vector3.TransformNormal(worldRay.Direction, inverse));
        var hit = false;
        for (var i = 0; i + 2 < _indices.Length; i += 3)
        {
            var a = _positions[_indices[i]];
            var b = _positions[_indices[i + 1]];
            var c = _positions[_indices[i + 2]];
            if (!IntersectTriangle(localOrigin, localDirection, a, b, c, out var localDistance)) continue;
            var localPoint = localOrigin + localDirection * localDistance;
            var worldPoint = System.Numerics.Vector3.Transform(localPoint, transform);
            var worldDistance = System.Numerics.Vector3.Distance(worldRay.Origin, worldPoint);
            if (worldDistance >= distance) continue;
            distance = worldDistance;
            hit = true;
        }
        return hit;
    }

    private static bool IntersectTriangle(System.Numerics.Vector3 origin, System.Numerics.Vector3 direction,
        System.Numerics.Vector3 a, System.Numerics.Vector3 b, System.Numerics.Vector3 c, out float distance)
    {
        const float epsilon = 1e-7f;
        var edge1 = b - a;
        var edge2 = c - a;
        var p = System.Numerics.Vector3.Cross(direction, edge2);
        var determinant = System.Numerics.Vector3.Dot(edge1, p);
        if (MathF.Abs(determinant) < epsilon) { distance = 0f; return false; }
        var inverse = 1f / determinant;
        var t = origin - a;
        var u = System.Numerics.Vector3.Dot(t, p) * inverse;
        if (u < 0f || u > 1f) { distance = 0f; return false; }
        var q = System.Numerics.Vector3.Cross(t, edge1);
        var v = System.Numerics.Vector3.Dot(direction, q) * inverse;
        if (v < 0f || u + v > 1f) { distance = 0f; return false; }
        distance = System.Numerics.Vector3.Dot(edge2, q) * inverse;
        return distance >= 0f;
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_device.IsInitialized)
        {
            if (_indexBuffer != 0) _device.GL.DeleteBuffer(_indexBuffer);
            if (_vertexBuffer != 0) _device.GL.DeleteBuffer(_vertexBuffer);
            if (_instanceBuffer != 0) _device.GL.DeleteBuffer(_instanceBuffer);
            if (_vao != 0) _device.GL.DeleteVertexArray(_vao);
        }
        _instanceBuffer = _indexBuffer = _vertexBuffer = _vao = 0;
        _disposed = true;
    }
}
