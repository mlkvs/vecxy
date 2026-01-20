using OpenTK.Graphics.OpenGL;

namespace Vecxy.Rendering;

public class VertexBuffer : IDisposable
{
    public int Id { get; private set; }

    public VertexBuffer()
    {
        Id = GL.GenBuffer();
    }

    public void SetData(float[] data)
    {
        GL.BindBuffer(BufferTarget.ArrayBuffer, Id);

        GL.BufferData(BufferTarget.ArrayBuffer, data.Length * sizeof(float), data, BufferUsageHint.StaticDraw);
    }

    public void Dispose()
    {
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        GL.DeleteBuffer(Id);
    }
}