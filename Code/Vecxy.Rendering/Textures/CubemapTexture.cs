using Silk.NET.OpenGL;
using Vecxy.Assets;

namespace Vecxy.Rendering;

internal sealed class CubemapTexture : IDisposable
{
    private readonly GraphicsDevice _device;
    private uint _handle;
    private bool _disposed;

    public CubemapTexture(
        GraphicsDevice device,
        TextureAsset positiveX,
        TextureAsset negativeX,
        TextureAsset positiveY,
        TextureAsset negativeY,
        TextureAsset positiveZ,
        TextureAsset negativeZ)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(positiveX);
        ArgumentNullException.ThrowIfNull(negativeX);
        ArgumentNullException.ThrowIfNull(positiveY);
        ArgumentNullException.ThrowIfNull(negativeY);
        ArgumentNullException.ThrowIfNull(positiveZ);
        ArgumentNullException.ThrowIfNull(negativeZ);

        _device = device;
        Create(
            positiveX,
            negativeX,
            positiveY,
            negativeY,
            positiveZ,
            negativeZ);
    }

    public void Bind(uint slot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _device.GL.ActiveTexture((TextureUnit)((uint)TextureUnit.Texture0 + slot));
        _device.GL.BindTexture(TextureTarget.TextureCubeMap, _handle);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_handle != 0)
        {
            _device.GL.DeleteTexture(_handle);
            _handle = 0;
        }
    }

    private unsafe void Create(
        TextureAsset positiveX,
        TextureAsset negativeX,
        TextureAsset positiveY,
        TextureAsset negativeY,
        TextureAsset positiveZ,
        TextureAsset negativeZ)
    {
        var gl = _device.GL;
        _handle = gl.GenTexture();
        gl.BindTexture(TextureTarget.TextureCubeMap, _handle);

        UploadFace(TextureTarget.TextureCubeMapPositiveX, positiveX);
        UploadFace(TextureTarget.TextureCubeMapNegativeX, negativeX);
        UploadFace(TextureTarget.TextureCubeMapPositiveY, positiveY);
        UploadFace(TextureTarget.TextureCubeMapNegativeY, negativeY);
        UploadFace(TextureTarget.TextureCubeMapPositiveZ, positiveZ);
        UploadFace(TextureTarget.TextureCubeMapNegativeZ, negativeZ);

        gl.TexParameter(
            TextureTarget.TextureCubeMap,
            TextureParameterName.TextureMinFilter,
            (int)TextureMinFilter.Linear);
        gl.TexParameter(
            TextureTarget.TextureCubeMap,
            TextureParameterName.TextureMagFilter,
            (int)TextureMagFilter.Linear);
        gl.TexParameter(
            TextureTarget.TextureCubeMap,
            TextureParameterName.TextureWrapS,
            (int)TextureWrapMode.ClampToEdge);
        gl.TexParameter(
            TextureTarget.TextureCubeMap,
            TextureParameterName.TextureWrapT,
            (int)TextureWrapMode.ClampToEdge);
        gl.TexParameter(
            TextureTarget.TextureCubeMap,
            TextureParameterName.TextureWrapR,
            (int)TextureWrapMode.ClampToEdge);
        gl.BindTexture(TextureTarget.TextureCubeMap, 0);

        void UploadFace(
            TextureTarget target,
            TextureAsset face)
        {
            fixed (byte* pixels = face.Pixels)
            {
                gl.TexImage2D(
                    target,
                    0,
                    InternalFormat.Rgba8,
                    (uint)face.Width,
                    (uint)face.Height,
                    0,
                    PixelFormat.Rgba,
                    PixelType.UnsignedByte,
                    pixels);
            }
        }
    }
}
