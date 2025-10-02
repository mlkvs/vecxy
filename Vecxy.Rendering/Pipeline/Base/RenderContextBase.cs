using System.Drawing;
using OpenTK.Graphics.OpenGL;

namespace Vecxy.Rendering;

public abstract class RenderContextBase(IRenderWindow window) : IRenderContext
{
    public void Clear()
    {
        GL.Clear(ClearBufferMask.ColorBufferBit);
        GL.ClearColor(Color.DarkSlateGray);
    }

    public void SwapBuffers()
    {
        window.SwapBuffers();
    }
}