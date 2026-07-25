using Silk.NET.OpenGL;

namespace Vecxy.Rendering;

public sealed class Mesh : IDisposable
{
    private readonly GraphicsDevice _device;
    private uint _vertexArray;
    private uint _vertexBuffer;
    private uint _indexBuffer;
    private bool _disposed;

    public uint IndexCount { get; }

    internal unsafe Mesh(
        GraphicsDevice device,
        ReadOnlySpan<float> vertices,
        ReadOnlySpan<uint> indices)
    {
        _device = device;
        IndexCount = (uint)indices.Length;

        var gl = device.GL;
        _vertexArray = gl.GenVertexArray();
        _vertexBuffer = gl.GenBuffer();
        _indexBuffer = gl.GenBuffer();

        gl.BindVertexArray(_vertexArray);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);
        fixed (float* data = vertices)
        {
            gl.BufferData(
                BufferTargetARB.ArrayBuffer,
                (nuint)(vertices.Length * sizeof(float)),
                data,
                BufferUsageARB.StaticDraw);
        }

        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _indexBuffer);
        fixed (uint* data = indices)
        {
            gl.BufferData(
                BufferTargetARB.ElementArrayBuffer,
                (nuint)(indices.Length * sizeof(uint)),
                data,
                BufferUsageARB.StaticDraw);
        }

        const uint stride = 4 * sizeof(float);
        gl.VertexAttribPointer(
            0,
            2,
            VertexAttribPointerType.Float,
            false,
            stride,
            0);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(
            1,
            2,
            VertexAttribPointerType.Float,
            false,
            stride,
            2 * sizeof(float));
        gl.EnableVertexAttribArray(1);
        gl.BindVertexArray(0);
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
        {
            return;
        }

        _disposed = true;
        if (_indexBuffer != 0)
        {
            _device.GL.DeleteBuffer(_indexBuffer);
        }

        if (_vertexBuffer != 0)
        {
            _device.GL.DeleteBuffer(_vertexBuffer);
        }

        if (_vertexArray != 0)
        {
            _device.GL.DeleteVertexArray(_vertexArray);
        }

        _indexBuffer = _vertexBuffer = _vertexArray = 0;
    }
}
