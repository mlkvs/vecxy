using Vecxy.Kernel;
using Silk.NET.OpenGL;

namespace Vecxy.Rendering;

public sealed class GraphicsDevice : IDisposable
{
    public GL GL { get; }

    public GraphicsDevice(IWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        window.MakeCurrent();
        GL = GL.GetApi(window.GetProcAddress);
    }

    public void Dispose()
    {
        GL.Dispose();
    }
}
