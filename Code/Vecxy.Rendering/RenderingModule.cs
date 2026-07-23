using Silk.NET.OpenGL;
using Vecxy.Kernel;

namespace Vecxy.Rendering;

public sealed class RenderingModule(IWindow window) :
    IModule,
    IModule.IRenderable,
    IModule.IUpdatable
{
    private GraphicsDevice? _device;

    public void OnInitialize()
    {
        _device = new GraphicsDevice(window);
        
        window.Resized += OnWindowResized;
        
        SetViewport(window.Width, window.Height);
    }

    public void OnRender()
    {
        window.MakeCurrent();
        _device?.GL.ClearColor(0.02f, 0.08f, 0.04f, 1f);
        _device?.GL.Clear(ClearBufferMask.ColorBufferBit);
        window.SwapBuffers();
    }

    public void OnUpdate(float deltaTime)
    {
    }

    public void OnShutdown()
    {
        window.Resized -= OnWindowResized;
    }

    public void Dispose()
    {
        _device?.Dispose();
        _device = null;
    }

    private void OnWindowResized(int width, int height) =>
        SetViewport(width, height);

    private void SetViewport(int width, int height)
    {
        _device?.GL.Viewport(
            0,
            0,
            (uint)Math.Max(0, width),
            (uint)Math.Max(0, height));
    }
}
