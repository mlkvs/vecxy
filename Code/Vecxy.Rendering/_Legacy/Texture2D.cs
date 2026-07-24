using Silk.NET.OpenGL;
using Vecxy.Assets;
using Vecxy.Assets._Legacy;
using Vecxy.Diagnostics;

namespace Vecxy.Rendering._Legacy;

public sealed class Texture2D : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly ImageAsset? _asset;
    private readonly TextureOptions _options;   
    private uint _handle;
    private bool _disposed;

    public int Width { get; private set; }
    public int Height { get; private set; }

    internal Texture2D(GraphicsDevice device, ImageAsset image, TextureOptions options)
        : this(device, image.Width, image.Height, image.Pixels, options)
    {
        _asset = image;
        _asset.Reloaded += OnAssetReloaded;
    }

    internal Texture2D(GraphicsDevice device, int width, int height, ReadOnlySpan<byte> pixels, TextureOptions options)
    {
        _device = device;
        _options = options;
        _handle = device.GL.GenTexture();
        Upload(width, height, pixels);
    }

    internal void Bind(uint unit)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _device.GL.ActiveTexture(TextureUnit.Texture0 + (int)unit);
        _device.GL.BindTexture(TextureTarget.Texture2D, _handle);
    }

    private void OnAssetReloaded(Asset _)
    {
        try
        {
            if (_asset is not null) Upload(_asset.Width, _asset.Height, _asset.Pixels);
            Logger.Info($"Texture reloaded: {_asset?.Path}");
        }
        catch (Exception exception)
        {
            Logger.Error(exception, $"Texture reload failed: {_asset?.Path}");
        }
    }

    private unsafe void Upload(int width, int height, ReadOnlySpan<byte> pixels)
    {
        _device.EnsureReady();
        if (width <= 0 || height <= 0 || pixels.Length != width * height * 4)
            throw new ArgumentException("Texture must contain RGBA8 pixels matching its dimensions.", nameof(pixels));
        Width = width;
        Height = height;
        var gl = _device.GL;
        gl.BindTexture(TextureTarget.Texture2D, _handle);
        fixed (byte* data = pixels)
            gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)width, (uint)height, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, data);
        var filter = _options.Filter == TextureFilter.Nearest ? (int)GLEnum.Nearest : (int)GLEnum.Linear;
        var wrap = _options.Wrap == TextureWrap.Clamp ? (int)GLEnum.ClampToEdge : (int)GLEnum.Repeat;
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, filter);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, filter);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, wrap);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, wrap);
        gl.BindTexture(TextureTarget.Texture2D, 0);
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_asset is not null) _asset.Reloaded -= OnAssetReloaded;
        if (_handle != 0 && _device.IsInitialized) _device.GL.DeleteTexture(_handle);
        _handle = 0;
        _disposed = true;
    }
}
