using Vecxy.Kernel;

namespace Vecxy.Rendering;

public sealed class BackbufferRenderTarget(IWindow window) : IRenderTarget
{
    public int Width => window.Width;
    public int Height => window.Height;

    public void Bind(GraphicsDevice device)
    {
        window.MakeCurrent();
        device.GL.BindFramebuffer(
            Silk.NET.OpenGL.FramebufferTarget.Framebuffer,
            0);
        device.GL.Viewport(
            0,
            0,
            (uint)Math.Max(0, Width),
            (uint)Math.Max(0, Height));
    }

    public void Present() => window.SwapBuffers();
}
