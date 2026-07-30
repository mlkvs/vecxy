using Silk.NET.OpenGL;
using System.Numerics;

namespace Vecxy.Rendering;

public sealed class Mesh : IDisposable
{
    private readonly GraphicsDevice _device;
    private uint _vertexArray;
    private uint _vertexBuffer;
    private uint _indexBuffer;
    private bool _disposed;

    public string Name { get; }
    public uint IndexCount { get; }
    public Vector3 BoundsMin { get; }
    public Vector3 BoundsMax { get; }
    public Vector3 BoundsSize => BoundsMax - BoundsMin;
    public Vector3 BoundsCenter => (BoundsMin + BoundsMax) * 0.5f;

    internal Mesh(
        GraphicsDevice device,
        ReadOnlySpan<float> vertices,
        ReadOnlySpan<uint> indices,
        int stride,
        Vector3 boundsMin,
        Vector3 boundsMax,
        params VertexAttribute[] attributes)
        : this(
            device,
            vertices,
            indices,
            stride,
            boundsMin,
            boundsMax,
            null,
            attributes)
    {
    }

    internal Mesh(
        GraphicsDevice device,
        ReadOnlySpan<float> vertices,
        ReadOnlySpan<uint> indices,
        int stride,
        Vector3 boundsMin,
        Vector3 boundsMax,
        string? name,
        params VertexAttribute[] attributes)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (stride <= 0)
            throw new ArgumentOutOfRangeException(nameof(stride));

        if (vertices.Length == 0 || vertices.Length % stride != 0)
            throw new ArgumentException(
                "Vertex data does not match its stride.",
                nameof(vertices));

        if (indices.Length == 0)
            throw new ArgumentException(
                "Mesh must contain indices.",
                nameof(indices));

        if (attributes.Length == 0)
            throw new ArgumentException(
                "Mesh must define at least one vertex attribute.",
                nameof(attributes));

        _device = device;
        Name = string.IsNullOrWhiteSpace(name) ? "Mesh" : name;
        IndexCount = checked((uint)indices.Length);
        BoundsMin = boundsMin;
        BoundsMax = boundsMax;

        CreateBuffers(vertices, indices, stride, attributes);
    }

    private unsafe void CreateBuffers(
        ReadOnlySpan<float> vertices,
        ReadOnlySpan<uint> indices,
        int stride,
        IReadOnlyList<VertexAttribute> attributes)
    {
        var gl = _device.GL;
        _vertexArray = gl.GenVertexArray();
        _vertexBuffer = gl.GenBuffer();
        _indexBuffer = gl.GenBuffer();

        try
        {
            gl.BindVertexArray(_vertexArray);
            gl.BindBuffer(
                BufferTargetARB.ArrayBuffer,
                _vertexBuffer);

            fixed (float* data = vertices)
            {
                gl.BufferData(
                    BufferTargetARB.ArrayBuffer,
                    checked((nuint)(vertices.Length * sizeof(float))),
                    data,
                    BufferUsageARB.StaticDraw);
            }

            gl.BindBuffer(
                BufferTargetARB.ElementArrayBuffer,
                _indexBuffer);

            fixed (uint* data = indices)
            {
                gl.BufferData(
                    BufferTargetARB.ElementArrayBuffer,
                    checked((nuint)(indices.Length * sizeof(uint))),
                    data,
                    BufferUsageARB.StaticDraw);
            }

            foreach (var attribute in attributes)
            {
                if (attribute.ComponentCount is < 1 or > 4 ||
                    attribute.Offset < 0 ||
                    attribute.Offset + attribute.ComponentCount > stride)
                {
                    throw new ArgumentException(
                        "Mesh contains an invalid vertex attribute.",
                        nameof(attributes));
                }

                gl.VertexAttribPointer(
                    attribute.Location,
                    attribute.ComponentCount,
                    VertexAttribPointerType.Float,
                    false,
                    checked((uint)(stride * sizeof(float))),
                    checked(attribute.Offset * sizeof(float)));
                gl.EnableVertexAttribArray(attribute.Location);
            }

            gl.BindVertexArray(0);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal unsafe void Draw()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _device.GL.BindVertexArray(_vertexArray);
        _device.GL.DrawElements(
            PrimitiveType.Triangles,
            IndexCount,
            DrawElementsType.UnsignedInt,
            null);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_indexBuffer != 0)
            _device.GL.DeleteBuffer(_indexBuffer);

        if (_vertexBuffer != 0)
            _device.GL.DeleteBuffer(_vertexBuffer);

        if (_vertexArray != 0)
            _device.GL.DeleteVertexArray(_vertexArray);

        _indexBuffer = 0;
        _vertexBuffer = 0;
        _vertexArray = 0;
    }
}

internal readonly record struct VertexAttribute(
    uint Location,
    int ComponentCount,
    int Offset);
