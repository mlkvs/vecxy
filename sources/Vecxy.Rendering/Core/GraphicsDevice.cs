using OpenTK.Graphics.OpenGL;

namespace Vecxy.Rendering;

public class GraphicsDevice
{
    private int _maxVertexAttribs = -1;
    
    public GraphicsDevice()
    {
        
    }

    private int GetMaxVertexAttribs()
    {
        if (_maxVertexAttribs == -1)
        {
            GL.GetInteger(GetPName.MaxVertexAttribs, out _maxVertexAttribs);
        }

        return _maxVertexAttribs;
    }
    
    
}