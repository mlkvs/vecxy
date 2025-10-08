using OpenTK.Graphics.OpenGL;

namespace Vecxy.Rendering;

public class VertexArrayObject
{
    public int Id { get; private set; }

    public void Initialize()
    {
        Id = GL.GenVertexArray();
        
        GL.BindVertexArray(Id);
        
        GL.BindBuffer(BufferTarget.ArrayBuffer, Id);
    }

    public void Bind()
    {
        GL.BindVertexArray(Id);
    }
}