using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace Vecxy.Rendering;

public class RenderWindow : GameWindow, IRenderWindow
{
    public int Width => Size.X;
    public int Height => Size.Y;

    public RenderWindow(RenderWindowOptions options) : base(GameWindowSettings.Default, new NativeWindowSettings())
    {
        Size = new Vector2i(options.Width, options.Height);
        Title = options.Title;
    }
    
    protected override void OnFramebufferResize(FramebufferResizeEventArgs e)
    {
        base.OnFramebufferResize(e);

        GL.Viewport(0, 0, e.Width, e.Height);
    }
}