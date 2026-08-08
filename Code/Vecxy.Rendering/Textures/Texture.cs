using Silk.NET.OpenGL;
using Vecxy.Assets;

namespace Vecxy.Rendering;

public sealed class Texture : IDisposable
{
    private readonly GraphicsDevice _device;
    private uint _handle;
    private TextureSamplerState? _sampler;
    private bool _disposed;

    internal uint Handle => _handle;

    internal unsafe Texture(GraphicsDevice device, TextureAsset asset)
        : this(
            device,
            asset.Width,
            asset.Height,
            asset.Pixels)
    {
    }

    internal unsafe Texture(
        GraphicsDevice device,
        int width,
        int height,
        ReadOnlySpan<byte> pixels)
    {
        _device = device;
        var gl = device.GL;
        _handle = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, _handle);

        fixed (byte* pixelsPointer = pixels)
        {
            gl.TexImage2D(
                TextureTarget.Texture2D,
                0,
                InternalFormat.Rgba8,
                (uint)width,
                (uint)height,
                0,
                PixelFormat.Rgba,
                PixelType.UnsignedByte,
                pixelsPointer);
        }

        ApplySampler(TextureSamplerState.Default);
        gl.BindTexture(TextureTarget.Texture2D, 0);
    }

    internal Texture(GraphicsDevice device, uint handle)
    {
        _device = device;
        _handle = handle != 0
            ? handle
            : throw new ArgumentOutOfRangeException(nameof(handle));
    }

    public void Bind(uint slot)
    {
        Bind(slot, TextureSamplerState.Default);
    }

    public void Bind(uint slot, TextureSamplerState sampler)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _device.GL.ActiveTexture((TextureUnit)((uint)TextureUnit.Texture0 + slot));
        _device.GL.BindTexture(TextureTarget.Texture2D, _handle);
        ApplySampler(sampler);
    }

    private void ApplySampler(TextureSamplerState sampler)
    {
        if (_sampler == sampler)
            return;

        var gl = _device.GL;
        gl.TexParameter(
            TextureTarget.Texture2D,
            TextureParameterName.TextureMinFilter,
            (int)ToMinFilter(sampler.MinFilter));
        gl.TexParameter(
            TextureTarget.Texture2D,
            TextureParameterName.TextureMagFilter,
            (int)ToMagFilter(sampler.MagFilter));
        gl.TexParameter(
            TextureTarget.Texture2D,
            TextureParameterName.TextureWrapS,
            (int)ToWrapMode(sampler.WrapU));
        gl.TexParameter(
            TextureTarget.Texture2D,
            TextureParameterName.TextureWrapT,
            (int)ToWrapMode(sampler.WrapV));
        _sampler = sampler;
    }

    private static TextureMinFilter ToMinFilter(ETextureFilter filter) =>
        filter switch
        {
            ETextureFilter.Nearest => TextureMinFilter.Nearest,
            ETextureFilter.Linear => TextureMinFilter.Linear,
            _ => throw new ArgumentOutOfRangeException(nameof(filter))
        };

    private static TextureMagFilter ToMagFilter(ETextureFilter filter) =>
        filter switch
        {
            ETextureFilter.Nearest => TextureMagFilter.Nearest,
            ETextureFilter.Linear => TextureMagFilter.Linear,
            _ => throw new ArgumentOutOfRangeException(nameof(filter))
        };

    private static TextureWrapMode ToWrapMode(ETextureWrap wrap) =>
        wrap switch
        {
            ETextureWrap.Repeat => TextureWrapMode.Repeat,
            ETextureWrap.Clamp => TextureWrapMode.ClampToEdge,
            _ => throw new ArgumentOutOfRangeException(nameof(wrap))
        };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_handle != 0)
        {
            _device.GL.DeleteTexture(_handle);
            _handle = 0;
        }
    }
}
