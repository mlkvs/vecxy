using System.Drawing;
using OpenTK.Graphics.OpenGL;

namespace Vecxy.Rendering;

public abstract class RenderContextBase : IRenderContext
{
    private readonly IRenderWindow _window;
    
    public RenderContextBase(IRenderWindow window)
    {
        _window = window;
    }
    
    public void Clear()
    {
        GL.Clear(ClearBufferMask.ColorBufferBit);
        GL.ClearColor(Color.DarkSlateGray);
    }

    public void SwapBuffers()
    {
        _window.SwapBuffers();
    }
}