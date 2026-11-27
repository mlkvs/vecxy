using Silk.NET.OpenGL;
using Vecxy.Assets;

namespace Vecxy.Rendering;

public sealed class SceneRenderTarget : IRenderTarget, IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly bool _sampleDepth;
    private uint _framebuffer;
    private Texture? _colorTexture;
    private uint _depthRenderbuffer;
    private Texture? _depthTexture;
    private uint _viewModelDepth;
    private bool _disposed;

    public int Width { get; private set; }
    public int Height { get; private set; }
    public Texture ColorTexture => _colorTexture ??
        throw new InvalidOperationException("Render target is not initialized.");

    internal SceneRenderTarget(GraphicsDevice device, bool sampleDepth = false)
    {
        _device = device;
        _sampleDepth = sampleDepth;
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

    internal void BindDepthTexture(uint slot)
    {
        var depth = _depthTexture ?? throw new InvalidOperationException("The target has no sampled depth texture.");
        depth.Bind(slot, TextureSamplerState.PointClamp);
    }

    internal void BindViewModelDepth()
    {
        Bind(_device);
        var gl = _device.GL;
        if (_viewModelDepth == 0)
        {
            _viewModelDepth = gl.GenRenderbuffer();
            gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _viewModelDepth);
            gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent24,
                (uint)Width, (uint)Height);
            gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);
        }
        gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
            RenderbufferTarget.Renderbuffer, _viewModelDepth);
    }

    internal void RestoreSceneDepth()
    {
        var gl = _device.GL;
        if (_depthTexture is { } depth)
            gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
                TextureTarget.Texture2D, depth.Handle, 0);
        else
            gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
                RenderbufferTarget.Renderbuffer, _depthRenderbuffer);
    }

    internal void CopyDepthTo(SceneRenderTarget destination)
    {
        if (destination.Width != Width || destination.Height != Height)
            throw new ArgumentException("Depth targets must have the same size.", nameof(destination));

        var gl = _device.GL;
        gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _framebuffer);
        gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, destination._framebuffer);
        gl.BlitFramebuffer(0, 0, Width, Height, 0, 0, Width, Height,
            ClearBufferMask.DepthBufferBit, BlitFramebufferFilter.Nearest);
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
        _colorTexture = new Texture(_device, colorHandle);

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

        if (_sampleDepth)
        {
            var handle = gl.GenTexture();
            _depthTexture = new Texture(_device, handle);
            _depthTexture.Bind(0, TextureSamplerState.PointClamp);
            gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.DepthComponent24,
                (uint)width, (uint)height, 0, PixelFormat.DepthComponent, PixelType.UnsignedInt, null);
            gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
                TextureTarget.Texture2D, handle, 0);
        }
        else
        {
            _depthRenderbuffer = gl.GenRenderbuffer();
            gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _depthRenderbuffer);
            gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent24,
                (uint)width, (uint)height);
            gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
                RenderbufferTarget.Renderbuffer, _depthRenderbuffer);
        }

        var status = gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        gl.BindTexture(TextureTarget.Texture2D, 0);
        gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);

        if (status != GLEnum.FramebufferComplete)
        {
            DisposeHandles();
            throw new InvalidOperationException($"Scene framebuffer is incomplete: {status}.");
        }

        Width = width;
        Height = height;
    }

    private void DisposeHandles()
    {
        var gl = _device.GL;

        if (_viewModelDepth != 0)
        {
            gl.DeleteRenderbuffer(_viewModelDepth);
            _viewModelDepth = 0;
        }

        if (_depthRenderbuffer != 0)
        {
            gl.DeleteRenderbuffer(_depthRenderbuffer);
            _depthRenderbuffer = 0;
        }

        _depthTexture?.Dispose();
        _depthTexture = null;
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
