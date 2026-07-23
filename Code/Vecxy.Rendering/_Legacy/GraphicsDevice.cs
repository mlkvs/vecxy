using Silk.NET.OpenGL;

namespace Vecxy.Rendering._Legacy;

public sealed class GraphicsDevice : IDisposable
{
    private readonly Window _window;
    private bool _disposed;

    internal GL GL { get; private set; } = null!;
    public bool IsInitialized { get; private set; }

    internal GraphicsDevice(Window window) => _window = window;

    internal void Initialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsInitialized) return;
        GL = GL.GetApi(_window);
        IsInitialized = true;
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
    }

    internal void Resize(int width, int height)
    {
        if (IsInitialized)
            GL.Viewport(0, 0, (uint)Math.Max(1, width), (uint)Math.Max(1, height));
    }

    internal void EnsureReady()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsInitialized) throw new InvalidOperationException("Graphics device is not initialized.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (IsInitialized) GL.Dispose();
        IsInitialized = false;
        _disposed = true;
    }
}
