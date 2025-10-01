using System.Drawing;
using OpenTK.Graphics.OpenGL;

namespace Vecxy.Rendering;

public class D2RenderPhase : IRenderPhase<ID2RenderContext>
{
    public RenderPhase Type => RenderPhase.D2;
    public ID2RenderContext Context { get; }

    public D2RenderPhase()
    {
        Context = new D2RenderContext();
    }

  

    public void OnBegin()
    {
        GL.ClearColor(Color.DarkSlateGray);
    }

    public void OnRender()
    {
        GL.Clear(ClearBufferMask.ColorBufferBit);
    }

    public void OnEnd()
    {
       
    }

    public void Dispose()
    {
        
    }
}