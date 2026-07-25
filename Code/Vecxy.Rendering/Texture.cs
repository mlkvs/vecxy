using Silk.NET.OpenGL;
using Vecxy.Assets;

namespace Vecxy.Rendering;

public sealed class Texture : IDisposable
{
    private readonly GraphicsDevice _device;
    private uint _handle;
    private bool _disposed;

    internal unsafe Texture(GraphicsDevice device, TextureAsset asset)
    {
        _device = device;
        var gl = device.GL;
        _handle = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, _handle);

        fixed (byte* pixels = asset.Pixels)
        {
            gl.TexImage2D(
                TextureTarget.Texture2D,
                0,
                InternalFormat.Rgba8,
                (uint)asset.Width,
                (uint)asset.Height,
                0,
                PixelFormat.Rgba,
                PixelType.UnsignedByte,
                pixels);
        }

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
            (int)TextureWrapMode.Repeat);
        gl.TexParameter(
            TextureTarget.Texture2D,
            TextureParameterName.TextureWrapT,
            (int)TextureWrapMode.Repeat);
        gl.BindTexture(TextureTarget.Texture2D, 0);
    }

    public void Bind(uint slot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _device.GL.ActiveTexture((TextureUnit)((uint)TextureUnit.Texture0 + slot));
        _device.GL.BindTexture(TextureTarget.Texture2D, _handle);
    }

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
