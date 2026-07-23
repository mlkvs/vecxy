using Vecxy.Kernel;
using Silk.NET.OpenGL;

namespace Vecxy.Rendering;

public sealed class GraphicsDevice : IDisposable
{
    public GL GL { get; }

    public GraphicsDevice(IGraphicsContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.MakeCurrent();
        GL = Silk.NET.OpenGL.GL.GetApi(context.GetProcAddress);
    }

    public void Dispose()
    {
        GL.Dispose();
    }
}
