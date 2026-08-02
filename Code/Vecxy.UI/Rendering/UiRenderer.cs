using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;
using Vecxy.Assets;
using Vecxy.Kernel;
using Vecxy.Rendering;
using RenderTexture = Vecxy.Rendering.Texture;

namespace Vecxy.UI;

internal sealed class UiRenderer : IDisposable
{
    private const int VertexStride = 8;
    private readonly GraphicsDevice _device;
    private readonly List<float> _vertices = [];
    private readonly List<uint> _indices = [];
    private readonly List<Batch> _batches = [];
    private uint _program;
    private uint _vertexArray;
    private uint _vertexBuffer;
    private uint _indexBuffer;
    private uint _whiteTexture;
    private int _viewportUniform;
    private int _textureUniform;
    private bool _disposed;

    public UiRenderer(GraphicsDevice device)
    {
        _device = device;
    }

    public void Draw(IReadOnlyList<UiDocument> documents, int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureResources();
        _vertices.Clear();
        _indices.Clear();
        _batches.Clear();

        var viewport = new Rect(0, 0, width, height);
        foreach (var document in documents.Where(document => document.IsVisible))
        {
            document.Layout(width, height);
            PaintElement(document, document.Root, viewport, 1.0f, document.LayoutScale);
        }

        if (_indices.Count == 0)
            return;

        UploadAndDraw(width, height);
    }

    private void PaintElement(
        UiDocument document,
        UiElement element,
        Rect clip,
        float inheritedOpacity,
        float scale)
    {
        var style = element.ComputedStyle;
        var bounds = Scale(element.Bounds, scale);
        if (style.Display == "none" || style.Visibility == "hidden" ||
            bounds.Width <= 0 || bounds.Height <= 0)
            return;

        var opacity = inheritedOpacity * style.Opacity;
        var background = style.BackgroundColor with { W = style.BackgroundColor.W * opacity };
        if (background.W > 0.001f)
            AddQuad(bounds, background, null, clip, false);

        var image = document.ResolveImage(element);
        if (image is { } resolvedImage)
        {
            var (imageBounds, imageUv) = FitImage(
                bounds,
                resolvedImage.Uv,
                resolvedImage.Size,
                style.ObjectFit);
            AddTextured(
                imageBounds,
                Vector4.One with { W = opacity },
                resolvedImage.Texture,
                imageUv,
                clip);
        }

        if (style.BorderWidth > 0.0f && style.BorderColor.W > 0.001f)
            AddBorder(bounds, style.BorderWidth * scale, style.BorderColor with { W = style.BorderColor.W * opacity }, clip);

        if (element.TagName == "text" && element.Text.Length > 0)
        {
            var color = style.Color with { W = style.Color.W * opacity };
            if (element.Font is { } font && document.ResolveFontTexture(element) is { } fontTexture)
                UiBitmapFont.Paint(this, font, fontTexture, element.Text, bounds, style.FontSize * scale, color, clip);
            else
                UiFallbackFont.Paint(this, element.Text, bounds, style.FontSize * scale, color, clip);
        }

        var childClip = style.Overflow is "hidden" or "scroll" or "auto"
            ? Intersect(clip, bounds)
            : clip;
        foreach (var child in element.Children
                     .Select((value, index) => (value, index))
                     .OrderBy(item => item.value.ComputedStyle.ZIndex)
                     .ThenBy(item => item.index)
                     .Select(item => item.value))
            PaintElement(document, child, childClip, opacity, scale);
    }

    private static Rect Scale(Rect bounds, float scale) =>
        new(
            bounds.X * scale,
            bounds.Y * scale,
            bounds.Width * scale,
            bounds.Height * scale);

    private static (Rect Bounds, Vector4 Uv) FitImage(
        Rect bounds,
        Vector4 uv,
        Vector2 sourceSize,
        string objectFit)
    {
        if (objectFit == "fill" || sourceSize.X <= 0.0f || sourceSize.Y <= 0.0f ||
            bounds.Width <= 0.0f || bounds.Height <= 0.0f)
            return (bounds, uv);

        var sourceAspect = sourceSize.X / sourceSize.Y;
        var targetAspect = bounds.Width / bounds.Height;
        if (objectFit == "contain")
        {
            var width = bounds.Width;
            var height = width / sourceAspect;
            if (height > bounds.Height)
            {
                height = bounds.Height;
                width = height * sourceAspect;
            }

            return (
                new Rect(
                    bounds.X + (bounds.Width - width) * 0.5f,
                    bounds.Y + (bounds.Height - height) * 0.5f,
                    width,
                    height),
                uv);
        }

        if (objectFit != "cover")
            return (bounds, uv);

        if (sourceAspect > targetAspect)
        {
            var visible = targetAspect / sourceAspect;
            var inset = (1.0f - visible) * 0.5f * (uv.Z - uv.X);
            uv.X += inset;
            uv.Z -= inset;
        }
        else
        {
            var visible = sourceAspect / targetAspect;
            var inset = (1.0f - visible) * 0.5f * (uv.W - uv.Y);
            uv.Y += inset;
            uv.W -= inset;
        }

        return (bounds, uv);
    }

    internal void AddSolid(Rect bounds, Vector4 color, Rect clip) =>
        AddQuad(bounds, color, null, clip, false);

    internal void AddTextured(
        Rect bounds,
        Vector4 color,
        RenderTexture texture,
        Vector4 uv,
        Rect clip,
        TextureSamplerState? sampler = null) =>
        AddQuad(bounds, color, texture, clip, false, uv, sampler);

    private void AddBorder(Rect bounds, float width, Vector4 color, Rect clip)
    {
        width = Math.Min(width, Math.Min(bounds.Width, bounds.Height) * 0.5f);
        AddSolid(new Rect(bounds.X, bounds.Y, bounds.Width, width), color, clip);
        AddSolid(new Rect(bounds.X, bounds.Bottom - width, bounds.Width, width), color, clip);
        AddSolid(new Rect(bounds.X, bounds.Y + width, width, Math.Max(0, bounds.Height - width * 2)), color, clip);
        AddSolid(new Rect(bounds.Right - width, bounds.Y + width, width, Math.Max(0, bounds.Height - width * 2)), color, clip);
    }

    private void AddQuad(
        Rect bounds,
        Vector4 color,
        RenderTexture? texture,
        Rect clip,
        bool flipVertically,
        Vector4? uvOverride = null,
        TextureSamplerState? sampler = null)
    {
        clip = Intersect(clip, bounds);
        if (clip.Width <= 0 || clip.Height <= 0)
            return;

        var firstVertex = (uint)(_vertices.Count / VertexStride);
        var uv = uvOverride ?? new Vector4(0, 0, 1, 1);
        var topV = flipVertically ? uv.W : uv.Y;
        var bottomV = flipVertically ? uv.Y : uv.W;
        AddVertex(bounds.Left, bounds.Top, uv.X, topV, color);
        AddVertex(bounds.Left, bounds.Bottom, uv.X, bottomV, color);
        AddVertex(bounds.Right, bounds.Bottom, uv.Z, bottomV, color);
        AddVertex(bounds.Right, bounds.Top, uv.Z, topV, color);

        var indexStart = _indices.Count;
        _indices.Add(firstVertex + 0);
        _indices.Add(firstVertex + 1);
        _indices.Add(firstVertex + 2);
        _indices.Add(firstVertex + 2);
        _indices.Add(firstVertex + 3);
        _indices.Add(firstVertex + 0);

        if (_batches.Count > 0 &&
            ReferenceEquals(_batches[^1].Texture, texture) &&
            _batches[^1].Clip == clip &&
            _batches[^1].Sampler == sampler)
        {
            _batches[^1] = _batches[^1] with { IndexCount = _batches[^1].IndexCount + 6 };
        }
        else
        {
            _batches.Add(new Batch(texture, sampler, clip, indexStart, 6));
        }
    }

    private void AddVertex(float x, float y, float u, float v, Vector4 color)
    {
        _vertices.Add(x);
        _vertices.Add(y);
        _vertices.Add(u);
        _vertices.Add(v);
        _vertices.Add(color.X);
        _vertices.Add(color.Y);
        _vertices.Add(color.Z);
        _vertices.Add(color.W);
    }

    private unsafe void UploadAndDraw(int width, int height)
    {
        var gl = _device.GL;
        gl.BindVertexArray(_vertexArray);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);
        var vertexSpan = CollectionsMarshal.AsSpan(_vertices);
        fixed (float* vertices = vertexSpan)
        {
            gl.BufferData(
                BufferTargetARB.ArrayBuffer,
                checked((nuint)(_vertices.Count * sizeof(float))),
                vertices,
                BufferUsageARB.DynamicDraw);
        }

        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _indexBuffer);
        var indexSpan = CollectionsMarshal.AsSpan(_indices);
        fixed (uint* indices = indexSpan)
        {
            gl.BufferData(
                BufferTargetARB.ElementArrayBuffer,
                checked((nuint)(_indices.Count * sizeof(uint))),
                indices,
                BufferUsageARB.DynamicDraw);
        }

        gl.UseProgram(_program);
        gl.Uniform2(_viewportUniform, (float)width, (float)height);
        gl.Uniform1(_textureUniform, 0);
        gl.Disable(EnableCap.DepthTest);
        gl.DepthMask(false);
        gl.Enable(EnableCap.Blend);
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        gl.Enable(EnableCap.ScissorTest);

        foreach (var batch in _batches)
        {
            var x = Math.Clamp((int)MathF.Floor(batch.Clip.X), 0, width);
            var y = Math.Clamp((int)MathF.Floor(height - batch.Clip.Bottom), 0, height);
            var right = Math.Clamp((int)MathF.Ceiling(batch.Clip.Right), 0, width);
            var top = Math.Clamp((int)MathF.Ceiling(height - batch.Clip.Top), 0, height);
            gl.Scissor(x, y, (uint)Math.Max(0, right - x), (uint)Math.Max(0, top - y));

            if (batch.Texture is null)
            {
                gl.ActiveTexture(TextureUnit.Texture0);
                gl.BindTexture(TextureTarget.Texture2D, _whiteTexture);
            }
            else
            {
                batch.Texture.Bind(0, batch.Sampler ?? TextureSamplerState.Default);
            }

            gl.DrawElements(
                PrimitiveType.Triangles,
                (uint)batch.IndexCount,
                DrawElementsType.UnsignedInt,
                (void*)(batch.IndexStart * sizeof(uint)));
        }

        gl.Disable(EnableCap.ScissorTest);
        gl.Disable(EnableCap.Blend);
        gl.DepthMask(true);
        gl.BindVertexArray(0);
    }

    private unsafe void EnsureResources()
    {
        if (_program != 0)
            return;

        var gl = _device.GL;
        _program = BuildProgram(gl);
        _viewportUniform = gl.GetUniformLocation(_program, "uViewport");
        _textureUniform = gl.GetUniformLocation(_program, "uTexture");
        _vertexArray = gl.GenVertexArray();
        _vertexBuffer = gl.GenBuffer();
        _indexBuffer = gl.GenBuffer();
        gl.BindVertexArray(_vertexArray);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _indexBuffer);
        var stride = (uint)(VertexStride * sizeof(float));
        gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, (void*)0);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, (void*)(2 * sizeof(float)));
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, stride, (void*)(4 * sizeof(float)));
        gl.EnableVertexAttribArray(2);
        gl.BindVertexArray(0);

        _whiteTexture = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, _whiteTexture);
        ReadOnlySpan<byte> white = [255, 255, 255, 255];
        fixed (byte* pixel = white)
        {
            gl.TexImage2D(
                TextureTarget.Texture2D,
                0,
                InternalFormat.Rgba8,
                1,
                1,
                0,
                PixelFormat.Rgba,
                PixelType.UnsignedByte,
                pixel);
        }
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        gl.BindTexture(TextureTarget.Texture2D, 0);
    }

    private static uint BuildProgram(GL gl)
    {
        var vertexSource = """
            #version 330 core
            layout(location = 0) in vec2 aPosition;
            layout(location = 1) in vec2 aUv;
            layout(location = 2) in vec4 aColor;
            uniform vec2 uViewport;
            out vec2 vUv;
            out vec4 vColor;
            void main() {
                vec2 ndc = vec2(aPosition.x / uViewport.x * 2.0 - 1.0,
                                1.0 - aPosition.y / uViewport.y * 2.0);
                gl_Position = vec4(ndc, 0.0, 1.0);
                vUv = aUv;
                vColor = aColor;
            }
            """;
        var fragmentSource = """
            #version 330 core
            in vec2 vUv;
            in vec4 vColor;
            uniform sampler2D uTexture;
            out vec4 oColor;
            void main() { oColor = texture(uTexture, vUv) * vColor; }
            """;
#if ANDROID
        vertexSource = vertexSource.Replace(
            "#version 330 core",
            "#version 300 es\nprecision highp float;",
            StringComparison.Ordinal);
        fragmentSource = fragmentSource.Replace(
            "#version 330 core",
            "#version 300 es\nprecision highp float;",
            StringComparison.Ordinal);
#endif
        var vertex = Compile(gl, ShaderType.VertexShader, vertexSource);
        var fragment = Compile(gl, ShaderType.FragmentShader, fragmentSource);
        var program = gl.CreateProgram();
        try
        {
            gl.AttachShader(program, vertex);
            gl.AttachShader(program, fragment);
            gl.LinkProgram(program);
            gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out var linked);
            if (linked == 0)
                throw new InvalidOperationException($"UI shader linking failed: {gl.GetProgramInfoLog(program)}");
            return program;
        }
        catch
        {
            gl.DeleteProgram(program);
            throw;
        }
        finally
        {
            gl.DeleteShader(vertex);
            gl.DeleteShader(fragment);
        }
    }

    private static uint Compile(GL gl, ShaderType type, string source)
    {
        var shader = gl.CreateShader(type);
        gl.ShaderSource(shader, source.TrimStart());
        gl.CompileShader(shader);
        gl.GetShader(shader, ShaderParameterName.CompileStatus, out var compiled);
        if (compiled != 0)
            return shader;
        var error = gl.GetShaderInfoLog(shader);
        gl.DeleteShader(shader);
        throw new InvalidOperationException($"UI {type} shader compilation failed: {error}");
    }

    private static Rect Intersect(Rect first, Rect second)
    {
        var left = Math.Max(first.Left, second.Left);
        var top = Math.Max(first.Top, second.Top);
        var right = Math.Min(first.Right, second.Right);
        var bottom = Math.Min(first.Bottom, second.Bottom);
        return new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        var gl = _device.GL;
        if (_whiteTexture != 0) gl.DeleteTexture(_whiteTexture);
        if (_indexBuffer != 0) gl.DeleteBuffer(_indexBuffer);
        if (_vertexBuffer != 0) gl.DeleteBuffer(_vertexBuffer);
        if (_vertexArray != 0) gl.DeleteVertexArray(_vertexArray);
        if (_program != 0) gl.DeleteProgram(_program);
        _whiteTexture = _indexBuffer = _vertexBuffer = _vertexArray = _program = 0;
    }

    private readonly record struct Batch(
        RenderTexture? Texture,
        TextureSamplerState? Sampler,
        Rect Clip,
        int IndexStart,
        int IndexCount);
}
