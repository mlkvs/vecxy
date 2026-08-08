using Silk.NET.OpenGL;
using Vecxy.Assets;

namespace Vecxy.Rendering;

public sealed class SceneRenderTarget : IRenderTarget, IDisposable
{
    private readonly GraphicsDevice _device;
    private uint _framebuffer;
    private Texture? _colorTexture;
    private uint _depthRenderbuffer;
    private bool _disposed;

    public int Width { get; private set; }
    public int Height { get; private set; }
    public Texture ColorTexture => _colorTexture ??
        throw new InvalidOperationException("Render target is not initialized.");

    internal SceneRenderTarget(GraphicsDevice device)
    {
        _device = device;
    }

    public void EnsureSize(int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        width = Math.Max(1, width);
        height = Math.Max(1, height);

        if (width == Width &&
            height == Height &&
            _framebuffer != 0)
        {
            return;
        }

        Recreate(width, height);
    }

    public void Bind(GraphicsDevice device)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_framebuffer == 0)
            throw new InvalidOperationException("Scene render target is not initialized.");

        var gl = _device.GL;
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        gl.Viewport(0, 0, (uint)Width, (uint)Height);
    }

    public void BindColorTexture(uint slot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ColorTexture.Bind(slot, TextureSamplerState.PointClamp);
    }

    public void Present()
    {
    }

    private unsafe void Recreate(int width, int height)
    {
        DisposeHandles();

        var gl = _device.GL;

        _framebuffer = gl.GenFramebuffer();
        var colorHandle = gl.GenTexture();
        _depthRenderbuffer = gl.GenRenderbuffer();

        gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);

        gl.BindTexture(TextureTarget.Texture2D, colorHandle);
        gl.TexImage2D(
            TextureTarget.Texture2D,
            0,
            InternalFormat.Rgba8,
            (uint)width,
            (uint)height,
            0,
            PixelFormat.Rgba,
            PixelType.UnsignedByte,
            null);
        gl.TexParameter(
            TextureTarget.Texture2D,
            TextureParameterName.TextureMinFilter,
            (int)TextureMinFilter.Nearest);
        gl.TexParameter(
            TextureTarget.Texture2D,
            TextureParameterName.TextureMagFilter,
            (int)TextureMagFilter.Nearest);
        gl.TexParameter(
            TextureTarget.Texture2D,
            TextureParameterName.TextureWrapS,
            (int)TextureWrapMode.ClampToEdge);
        gl.TexParameter(
            TextureTarget.Texture2D,
            TextureParameterName.TextureWrapT,
            (int)TextureWrapMode.ClampToEdge);
        gl.FramebufferTexture2D(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D,
            colorHandle,
            0);

        gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _depthRenderbuffer);
        gl.RenderbufferStorage(
            RenderbufferTarget.Renderbuffer,
            InternalFormat.DepthComponent24,
            (uint)width,
            (uint)height);
        gl.FramebufferRenderbuffer(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.DepthAttachment,
            RenderbufferTarget.Renderbuffer,
            _depthRenderbuffer);

        var status = gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        gl.BindTexture(TextureTarget.Texture2D, 0);
        gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);

        if (status != GLEnum.FramebufferComplete)
            throw new InvalidOperationException($"Scene framebuffer is incomplete: {status}.");

        _colorTexture = new Texture(_device, colorHandle);

        Width = width;
        Height = height;
    }

    private void DisposeHandles()
    {
        var gl = _device.GL;

        if (_depthRenderbuffer != 0)
        {
            gl.DeleteRenderbuffer(_depthRenderbuffer);
            _depthRenderbuffer = 0;
        }

        _colorTexture?.Dispose();
        _colorTexture = null;

        if (_framebuffer != 0)
        {
            gl.DeleteFramebuffer(_framebuffer);
            _framebuffer = 0;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        DisposeHandles();
        Width = 0;
        Height = 0;
    }
}
