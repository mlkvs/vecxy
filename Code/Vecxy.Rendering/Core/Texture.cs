using OpenTK.Graphics.OpenGL;
using StbImageSharp;
using System.Reflection;
using Vecxy.Reflection;

namespace Vecxy.Rendering;

public class Texture : IDisposable
{
    public int Id { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }

    private bool _isDisposed;

    public Texture(string filePath)
    {
        var assembly = Assembly.GetExecutingAssembly();

        using var stream = assembly
            .GetEmbeddedResource(filePath)!
            .Stream();

        var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

        Width = image.Width;
        Height = image.Height;

        Id = GL.GenTexture();
        Bind();

        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        GL.TexImage2D(TextureTarget.Texture2D,
            0,
            PixelInternalFormat.Rgba,
            Width,
            Height,
            0,
            OpenTK.Graphics.OpenGL.PixelFormat.Rgba,
            PixelType.UnsignedByte,
            image.Data);

        Unbind();
    }

    public void Bind(TextureUnit unit = TextureUnit.Texture0)
    {
        GL.ActiveTexture(unit);
        GL.BindTexture(TextureTarget.Texture2D, Id);
    }

    public void Unbind()
    {
        GL.BindTexture(TextureTarget.Texture2D, 0);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        GL.DeleteTexture(Id);
        _isDisposed = true;
    }
}