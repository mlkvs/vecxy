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
    private const int MaxRoundedClips = 8;
    private readonly GraphicsDevice _device;
    private readonly List<float> _vertices = [];
    private readonly List<uint> _indices = [];
    private readonly List<Batch> _batches = [];
    private Matrix3x2 _transform = Matrix3x2.Identity;
    private UiClipState? _roundedClip;
    private uint _program;
    private uint _vertexArray;
    private uint _vertexBuffer;
    private uint _indexBuffer;
    private uint _whiteTexture;
    private int _viewportUniform;
    private int _textureUniform;
    private int _roundedClipCountUniform;
    private readonly int[] _roundedClipBoundsUniforms = new int[MaxRoundedClips];
    private readonly int[] _roundedClipRadiusUniforms = new int[MaxRoundedClips];
    private readonly int[] _roundedClipMatrixAUniforms = new int[MaxRoundedClips];
    private readonly int[] _roundedClipMatrixBUniforms = new int[MaxRoundedClips];
    private bool _disposed;

    public UiRenderer(GraphicsDevice device)
    {
        _device = device;
    }

    public void Draw(IReadOnlyList<UiDocument> documents, int width, int height, UiConfig? settings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureResources();
        _vertices.Clear();
        _indices.Clear();
        _batches.Clear();

        var viewport = new Rect(0, 0, width, height);
        foreach (var document in documents.Where(document => document.IsVisible))
        {
            document.Layout(width, height, settings);
            PaintElement(
                document,
                document.Root,
                viewport,
                1.0f,
                document.LayoutScale,
                Vector2.Zero,
                Matrix3x2.Identity,
                null);
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
        float scale,
        Vector2 translation,
        Matrix3x2 parentTransform,
        UiClipState? roundedClip)
    {
        var style = element.ComputedStyle;
        var bounds = Scale(
            new Rect(
                element.Bounds.X + translation.X,
                element.Bounds.Y + translation.Y,
                element.Bounds.Width,
                element.Bounds.Height),
            scale);
        if (style.Display == "none" || style.Visibility == "hidden" ||
            bounds.Width <= 0 || bounds.Height <= 0)
            return;

        var previousTransform = _transform;
        var previousRoundedClip = _roundedClip;
        var renderTransform = element.RenderTransform with
        {
            Translation = element.RenderTransform.Translation * scale
        };
        _transform = renderTransform.ToMatrix(bounds) * parentTransform;
        _roundedClip = roundedClip;

        var opacity = inheritedOpacity * element.RenderOpacity;
        PaintBoxShadows(style, bounds, opacity, scale, clip, false);
        var renderedBackground = element.RenderBackgroundColor;
        var background = renderedBackground with { W = renderedBackground.W * opacity };
        if (background.W > 0.001f)
            AddRoundedQuad(bounds, background, null, clip, style.BorderRadius * scale);

        var image = document.ResolveImage(element);
        if (image is { } resolvedImage)
        {
            var (imageBounds, imageUv) = FitImage(
                bounds,
                resolvedImage.Uv,
                resolvedImage.Size,
                element.TagName == "image" ? style.ObjectFit : style.BackgroundSize,
                element.TagName == "image" ? "center" : style.BackgroundPosition);
            AddRoundedTextured(
                imageBounds,
                Vector4.One with { W = opacity },
                resolvedImage.Texture,
                imageUv,
                clip,
                style.BorderRadius * scale);
        }

        PaintBoxShadows(style, bounds, opacity, scale, clip, true);

        if (style.BorderWidth > 0.0f && style.BorderColor.W > 0.001f)
            AddBorder(bounds, style.BorderWidth * scale, style.BorderRadius * scale, style.BorderColor with { W = style.BorderColor.W * opacity }, clip);

        var clipsChildrenX = style.OverflowX is "hidden" or "scroll" or "auto";
        var clipsChildrenY = style.OverflowY is "hidden" or "scroll" or "auto";
        var transformedBounds = TransformBounds(bounds, _transform);
        var childClip = ClipAxes(clip, transformedBounds, clipsChildrenX, clipsChildrenY);
        var childRoundedClip = roundedClip;
        if ((clipsChildrenX || clipsChildrenY) && style.BorderRadius > 0.0f &&
            Matrix3x2.Invert(_transform, out var inverseTransform))
        {
            childRoundedClip = new UiClipState(
                roundedClip,
                bounds,
                style.BorderRadius * scale,
                inverseTransform);
            _roundedClip = childRoundedClip;
        }

        if (element.TagName == "text" && element.Text.Length > 0)
        {
            var renderedColor = element.RenderColor;
            var color = renderedColor with { W = renderedColor.W * opacity };
            if (element.Font is { } font && document.ResolveFontTexture(element) is { } fontTexture)
                UiBitmapFont.Paint(this, font, fontTexture, element.Text, bounds, style.FontSize * scale, color, clip, style.TextAlign, style.VerticalAlign);
            else
                UiFallbackFont.Paint(this, element.Text, bounds, style.FontSize * scale, color, clip, style.TextAlign, style.VerticalAlign);
        }

        var childTranslation = translation - element.ScrollOffset;
        foreach (var child in element.Children
                     .Select((value, index) => (value, index))
                     .OrderBy(item => item.value.ComputedStyle.ZIndex)
                     .ThenBy(item => item.index)
                     .Select(item => item.value))
            PaintElement(
                document,
                child,
                childClip,
                opacity,
                scale,
                childTranslation,
                _transform,
                childRoundedClip);

        PaintScrollbars(element, bounds, opacity, scale, clip);
        _transform = previousTransform;
        _roundedClip = previousRoundedClip;
    }

    private void PaintBoxShadows(
        UiComputedStyle style,
        Rect bounds,
        float opacity,
        float scale,
        Rect clip,
        bool inset)
    {
        foreach (var shadow in style.BoxShadows.Where(shadow => shadow.Inset == inset).Reverse())
        {
            var color = shadow.Color with { W = shadow.Color.W * opacity };
            if (color.W <= 0.001f)
                continue;
            var offset = new Vector2(shadow.OffsetX * scale, shadow.OffsetY * scale);
            var spread = shadow.SpreadRadius * scale;
            var blur = shadow.BlurRadius * scale;
            var radius = Math.Max(0.0f, style.BorderRadius * scale + spread);

            if (shadow.Inset)
            {
                var steps = blur > 0.0f ? Math.Clamp((int)MathF.Ceiling(blur * 0.5f), 3, 12) : 1;
                for (var step = steps; step >= 1; step--)
                {
                    var amount = step / (float)steps;
                    var width = Math.Max(1.0f, spread + blur * amount);
                    var layer = color with { W = color.W / steps };
                    AddBorder(
                        new Rect(bounds.X + offset.X, bounds.Y + offset.Y, bounds.Width, bounds.Height),
                        width,
                        style.BorderRadius * scale,
                        layer,
                        clip);
                }
                continue;
            }

            var shadowBounds = Expand(
                new Rect(bounds.X + offset.X, bounds.Y + offset.Y, bounds.Width, bounds.Height),
                spread);
            if (blur <= 0.0f)
            {
                AddRoundedQuad(shadowBounds, color, null, clip, radius);
                continue;
            }

            var layers = Math.Clamp((int)MathF.Ceiling(blur * 0.5f), 4, 16);
            for (var layerIndex = layers; layerIndex >= 1; layerIndex--)
            {
                var amount = layerIndex / (float)layers;
                var expansion = blur * amount;
                var alphaWeight = (1.0f - amount * 0.72f) / layers;
                AddRoundedQuad(
                    Expand(shadowBounds, expansion),
                    color with { W = color.W * alphaWeight },
                    null,
                    clip,
                    radius + expansion);
            }
        }
    }

    private void PaintScrollbars(
        UiElement element,
        Rect bounds,
        float opacity,
        float scale,
        Rect clip)
    {
        var style = element.ComputedStyle;
        var width = Math.Max(
            1.0f,
            UiLayout.ResolvePoints(
                style.ScrollbarWidth,
                element.Bounds.Width,
                element.Bounds.Height) * scale);

        if (element.CanScrollVertically)
        {
            var track = new Rect(bounds.Right - width, bounds.Top, width, bounds.Height);
            AddSolid(
                track,
                style.ScrollbarTrackColor with { W = style.ScrollbarTrackColor.W * opacity },
                clip);
            var ratio = Math.Clamp(element.Bounds.Height / element.ScrollExtent.Y, 0.05f, 1.0f);
            var thumbHeight = track.Height * ratio;
            var progress = element.ScrollOffset.Y /
                           Math.Max(0.001f, element.ScrollExtent.Y - element.Bounds.Height);
            AddSolid(
                new Rect(
                    track.X,
                    track.Y + (track.Height - thumbHeight) * progress,
                    width,
                    thumbHeight),
                style.ScrollbarColor with { W = style.ScrollbarColor.W * opacity },
                clip);
        }

        if (element.CanScrollHorizontally)
        {
            var track = new Rect(bounds.Left, bounds.Bottom - width, bounds.Width, width);
            AddSolid(
                track,
                style.ScrollbarTrackColor with { W = style.ScrollbarTrackColor.W * opacity },
                clip);
            var ratio = Math.Clamp(element.Bounds.Width / element.ScrollExtent.X, 0.05f, 1.0f);
            var thumbWidth = track.Width * ratio;
            var progress = element.ScrollOffset.X /
                           Math.Max(0.001f, element.ScrollExtent.X - element.Bounds.Width);
            AddSolid(
                new Rect(
                    track.X + (track.Width - thumbWidth) * progress,
                    track.Y,
                    thumbWidth,
                    width),
                style.ScrollbarColor with { W = style.ScrollbarColor.W * opacity },
                clip);
        }
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
        string objectFit,
        string position)
    {
        if (objectFit == "fill" || sourceSize.X <= 0.0f || sourceSize.Y <= 0.0f ||
            bounds.Width <= 0.0f || bounds.Height <= 0.0f)
            return (bounds, uv);

        var sourceAspect = sourceSize.X / sourceSize.Y;
        var targetAspect = bounds.Width / bounds.Height;
        var alignment = ParseImagePosition(position);
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
                    bounds.X + (bounds.Width - width) * alignment.X,
                    bounds.Y + (bounds.Height - height) * alignment.Y,
                    width,
                    height),
                uv);
        }

        if (objectFit != "cover")
            return (bounds, uv);

        if (sourceAspect > targetAspect)
        {
            var visible = targetAspect / sourceAspect;
            var crop = (1.0f - visible) * (uv.Z - uv.X);
            uv.X += crop * alignment.X;
            uv.Z -= crop * (1.0f - alignment.X);
        }
        else
        {
            var visible = sourceAspect / targetAspect;
            var crop = (1.0f - visible) * (uv.W - uv.Y);
            uv.Y += crop * alignment.Y;
            uv.W -= crop * (1.0f - alignment.Y);
        }

        return (bounds, uv);
    }

    private static Vector2 ParseImagePosition(string source)
    {
        var parts = source.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return new Vector2(0.5f);

        var horizontal = 0.5f;
        var vertical = 0.5f;
        for (var index = 0; index < Math.Min(parts.Length, 2); index++)
        {
            var part = parts[index];
            switch (part)
            {
                case "left": horizontal = 0.0f; break;
                case "right": horizontal = 1.0f; break;
                case "top": vertical = 0.0f; break;
                case "bottom": vertical = 1.0f; break;
                case "center": break;
                default:
                    if (!part.EndsWith('%') ||
                        !float.TryParse(
                            part[..^1],
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out var percentage))
                        break;
                    if (index == 0)
                        horizontal = Math.Clamp(percentage * 0.01f, 0.0f, 1.0f);
                    else
                        vertical = Math.Clamp(percentage * 0.01f, 0.0f, 1.0f);
                    break;
            }
        }
        return new Vector2(horizontal, vertical);
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

    private void AddRoundedTextured(
        Rect bounds,
        Vector4 color,
        RenderTexture texture,
        Vector4 uv,
        Rect clip,
        float radius)
    {
        if (radius <= 0.01f)
        {
            AddTextured(bounds, color, texture, uv, clip);
            return;
        }
        AddRoundedGeometry(bounds, color, texture, uv, clip, radius, TextureSamplerState.LinearClamp);
    }

    private void AddBorder(Rect bounds, float width, float radius, Vector4 color, Rect clip)
    {
        width = Math.Min(width, Math.Min(bounds.Width, bounds.Height) * 0.5f);
        radius = ClampRadius(bounds, radius);
        if (radius > 0.01f)
        {
            AddRoundedBorderGeometry(bounds, width, radius, color, clip);
            return;
        }
        AddSolid(new Rect(bounds.X, bounds.Y, bounds.Width, width), color, clip);
        AddSolid(new Rect(bounds.X, bounds.Bottom - width, bounds.Width, width), color, clip);
        AddSolid(new Rect(bounds.X, bounds.Y + width, width, Math.Max(0, bounds.Height - width * 2)), color, clip);
        AddSolid(new Rect(bounds.Right - width, bounds.Y + width, width, Math.Max(0, bounds.Height - width * 2)), color, clip);
    }

    private void AddRoundedQuad(
        Rect bounds,
        Vector4 color,
        RenderTexture? texture,
        Rect clip,
        float radius,
        Vector4? uv = null,
        TextureSamplerState? sampler = null)
    {
        radius = ClampRadius(bounds, radius);
        if (radius <= 0.01f)
        {
            AddQuad(bounds, color, texture, clip, false, uv, sampler);
            return;
        }
        AddRoundedGeometry(bounds, color, texture, uv ?? new Vector4(0, 0, 1, 1), clip, radius, sampler);
    }

    private void AddRoundedGeometry(
        Rect bounds,
        Vector4 color,
        RenderTexture? texture,
        Vector4 uv,
        Rect clip,
        float radius,
        TextureSamplerState? sampler)
    {
        clip = Intersect(clip, TransformBounds(bounds, _transform));
        if (clip.Width <= 0.0f || clip.Height <= 0.0f)
            return;

        const int segmentsPerCorner = 6;
        var firstVertex = (uint)(_vertices.Count / VertexStride);
        var center = new Vector2(bounds.X + bounds.Width * 0.5f, bounds.Y + bounds.Height * 0.5f);
        AddMappedVertex(center.X, center.Y, bounds, uv, color);
        var perimeter = new List<Vector2>(segmentsPerCorner * 4 + 1);
        AddCorner(perimeter, new Vector2(bounds.Right - radius, bounds.Top + radius), -90.0f, radius, segmentsPerCorner);
        AddCorner(perimeter, new Vector2(bounds.Right - radius, bounds.Bottom - radius), 0.0f, radius, segmentsPerCorner);
        AddCorner(perimeter, new Vector2(bounds.Left + radius, bounds.Bottom - radius), 90.0f, radius, segmentsPerCorner);
        AddCorner(perimeter, new Vector2(bounds.Left + radius, bounds.Top + radius), 180.0f, radius, segmentsPerCorner);
        perimeter.Add(perimeter[0]);
        foreach (var point in perimeter)
            AddMappedVertex(point.X, point.Y, bounds, uv, color);

        var indexStart = _indices.Count;
        for (var index = 0; index < perimeter.Count - 1; index++)
        {
            _indices.Add(firstVertex);
            _indices.Add(firstVertex + (uint)index + 1);
            _indices.Add(firstVertex + (uint)index + 2);
        }
        AddBatch(texture, sampler, clip, indexStart, (perimeter.Count - 1) * 3);
    }

    private void AddRoundedBorderGeometry(
        Rect bounds,
        float width,
        float radius,
        Vector4 color,
        Rect clip)
    {
        clip = Intersect(clip, TransformBounds(bounds, _transform));
        if (width <= 0.0f || clip.Width <= 0.0f || clip.Height <= 0.0f)
            return;

        const int segmentsPerCorner = 6;
        var outer = RoundedPerimeter(bounds, radius, segmentsPerCorner);
        var innerBounds = Expand(bounds, -width);
        var innerRadius = Math.Max(0.0f, radius - width);
        var inner = innerBounds.Width <= 0.0f || innerBounds.Height <= 0.0f
            ? Enumerable.Repeat(new Vector2(bounds.X + bounds.Width * 0.5f, bounds.Y + bounds.Height * 0.5f), outer.Count).ToList()
            : RoundedPerimeter(innerBounds, innerRadius, segmentsPerCorner);
        var firstVertex = (uint)(_vertices.Count / VertexStride);
        for (var index = 0; index < outer.Count; index++)
        {
            AddVertex(outer[index].X, outer[index].Y, 0, 0, color);
            AddVertex(inner[index].X, inner[index].Y, 0, 0, color);
        }

        var indexStart = _indices.Count;
        for (var index = 0; index < outer.Count; index++)
        {
            var next = (index + 1) % outer.Count;
            var outerCurrent = firstVertex + (uint)(index * 2);
            var innerCurrent = outerCurrent + 1;
            var outerNext = firstVertex + (uint)(next * 2);
            var innerNext = outerNext + 1;
            _indices.Add(outerCurrent);
            _indices.Add(innerCurrent);
            _indices.Add(innerNext);
            _indices.Add(innerNext);
            _indices.Add(outerNext);
            _indices.Add(outerCurrent);
        }
        AddBatch(null, null, clip, indexStart, outer.Count * 6);
    }

    private static List<Vector2> RoundedPerimeter(Rect bounds, float radius, int segmentsPerCorner)
    {
        radius = ClampRadius(bounds, radius);
        var result = new List<Vector2>(segmentsPerCorner * 4);
        AddCorner(result, new Vector2(bounds.Right - radius, bounds.Top + radius), -90.0f, radius, segmentsPerCorner);
        AddCorner(result, new Vector2(bounds.Right - radius, bounds.Bottom - radius), 0.0f, radius, segmentsPerCorner);
        AddCorner(result, new Vector2(bounds.Left + radius, bounds.Bottom - radius), 90.0f, radius, segmentsPerCorner);
        AddCorner(result, new Vector2(bounds.Left + radius, bounds.Top + radius), 180.0f, radius, segmentsPerCorner);
        return result;
    }

    private static void AddCorner(List<Vector2> points, Vector2 center, float startDegrees, float radius, int segments)
    {
        for (var index = 0; index < segments; index++)
        {
            var radians = (startDegrees + index * 90.0f / (segments - 1)) * MathF.PI / 180.0f;
            points.Add(center + new Vector2(MathF.Cos(radians), MathF.Sin(radians)) * radius);
        }
    }

    private void AddMappedVertex(float x, float y, Rect bounds, Vector4 uv, Vector4 color)
    {
        var horizontal = bounds.Width <= 0.0f ? 0.0f : (x - bounds.X) / bounds.Width;
        var vertical = bounds.Height <= 0.0f ? 0.0f : (y - bounds.Y) / bounds.Height;
        AddVertex(x, y, float.Lerp(uv.X, uv.Z, horizontal), float.Lerp(uv.Y, uv.W, vertical), color);
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
        clip = Intersect(clip, TransformBounds(bounds, _transform));
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

        AddBatch(texture, sampler, clip, indexStart, 6);
    }

    private void AddBatch(
        RenderTexture? texture,
        TextureSamplerState? sampler,
        Rect clip,
        int indexStart,
        int indexCount)
    {
        if (_batches.Count > 0 &&
            ReferenceEquals(_batches[^1].Texture, texture) &&
            _batches[^1].Clip == clip &&
            _batches[^1].Sampler == sampler &&
            ReferenceEquals(_batches[^1].RoundedClip, _roundedClip))
        {
            _batches[^1] = _batches[^1] with { IndexCount = _batches[^1].IndexCount + indexCount };
        }
        else
        {
            _batches.Add(new Batch(texture, sampler, clip, _roundedClip, indexStart, indexCount));
        }
    }

    private void AddVertex(float x, float y, float u, float v, Vector4 color)
    {
        var position = Vector2.Transform(new Vector2(x, y), _transform);
        _vertices.Add(position.X);
        _vertices.Add(position.Y);
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
            ApplyRoundedClips(gl, batch.RoundedClip);
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
        _roundedClipCountUniform = gl.GetUniformLocation(_program, "uRoundedClipCount");
        for (var index = 0; index < MaxRoundedClips; index++)
        {
            _roundedClipBoundsUniforms[index] = gl.GetUniformLocation(_program, $"uRoundedClipBounds[{index}]");
            _roundedClipRadiusUniforms[index] = gl.GetUniformLocation(_program, $"uRoundedClipRadius[{index}]");
            _roundedClipMatrixAUniforms[index] = gl.GetUniformLocation(_program, $"uRoundedClipMatrixA[{index}]");
            _roundedClipMatrixBUniforms[index] = gl.GetUniformLocation(_program, $"uRoundedClipMatrixB[{index}]");
        }
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
            uniform vec2 uViewport;
            uniform int uRoundedClipCount;
            uniform vec4 uRoundedClipBounds[8];
            uniform float uRoundedClipRadius[8];
            uniform vec4 uRoundedClipMatrixA[8];
            uniform vec2 uRoundedClipMatrixB[8];
            out vec4 oColor;
            void main() {
                vec2 screenPoint = vec2(gl_FragCoord.x, uViewport.y - gl_FragCoord.y);
                for (int i = 0; i < uRoundedClipCount; i++) {
                    vec4 a = uRoundedClipMatrixA[i];
                    vec2 b = uRoundedClipMatrixB[i];
                    vec2 point = vec2(
                        screenPoint.x * a.x + screenPoint.y * a.y + a.z,
                        screenPoint.x * a.w + screenPoint.y * b.x + b.y);
                    vec4 bounds = uRoundedClipBounds[i];
                    vec2 halfSize = bounds.zw * 0.5;
                    vec2 center = bounds.xy + halfSize;
                    float radius = uRoundedClipRadius[i];
                    radius = min(radius, min(halfSize.x, halfSize.y));
                    vec2 q = abs(point - center) - (halfSize - vec2(radius));
                    float distance = length(max(q, vec2(0.0))) + min(max(q.x, q.y), 0.0) - radius;
                    if (distance > 0.0)
                        discard;
                }
                oColor = texture(uTexture, vUv) * vColor;
            }
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

    private void ApplyRoundedClips(GL gl, UiClipState? state)
    {
        var clips = new List<UiClipState>(MaxRoundedClips);
        for (; state is not null && clips.Count < MaxRoundedClips; state = state.Parent)
            clips.Add(state);
        clips.Reverse();
        gl.Uniform1(_roundedClipCountUniform, clips.Count);
        for (var index = 0; index < clips.Count; index++)
        {
            var item = clips[index];
            var radius = ClampRadius(item.Bounds, item.Radius);
            gl.Uniform4(
                _roundedClipBoundsUniforms[index],
                item.Bounds.X,
                item.Bounds.Y,
                item.Bounds.Width,
                item.Bounds.Height);
            gl.Uniform1(_roundedClipRadiusUniforms[index], radius);
            gl.Uniform4(
                _roundedClipMatrixAUniforms[index],
                item.InverseTransform.M11,
                item.InverseTransform.M21,
                item.InverseTransform.M31,
                item.InverseTransform.M12);
            gl.Uniform2(
                _roundedClipMatrixBUniforms[index],
                item.InverseTransform.M22,
                item.InverseTransform.M32);
        }
    }

    private static Rect Expand(Rect bounds, float amount) =>
        new(
            bounds.X - amount,
            bounds.Y - amount,
            Math.Max(0.0f, bounds.Width + amount * 2.0f),
            Math.Max(0.0f, bounds.Height + amount * 2.0f));

    private static float ClampRadius(Rect bounds, float radius) =>
        Math.Clamp(radius, 0.0f, Math.Max(0.0f, Math.Min(bounds.Width, bounds.Height) * 0.5f));

    private static Rect TransformBounds(Rect bounds, Matrix3x2 transform)
    {
        var first = Vector2.Transform(new Vector2(bounds.Left, bounds.Top), transform);
        var second = Vector2.Transform(new Vector2(bounds.Right, bounds.Top), transform);
        var third = Vector2.Transform(new Vector2(bounds.Right, bounds.Bottom), transform);
        var fourth = Vector2.Transform(new Vector2(bounds.Left, bounds.Bottom), transform);
        var left = Math.Min(Math.Min(first.X, second.X), Math.Min(third.X, fourth.X));
        var top = Math.Min(Math.Min(first.Y, second.Y), Math.Min(third.Y, fourth.Y));
        var right = Math.Max(Math.Max(first.X, second.X), Math.Max(third.X, fourth.X));
        var bottom = Math.Max(Math.Max(first.Y, second.Y), Math.Max(third.Y, fourth.Y));
        return new Rect(left, top, right - left, bottom - top);
    }

    private static Rect ClipAxes(Rect clip, Rect bounds, bool horizontal, bool vertical)
    {
        var left = horizontal ? Math.Max(clip.Left, bounds.Left) : clip.Left;
        var right = horizontal ? Math.Min(clip.Right, bounds.Right) : clip.Right;
        var top = vertical ? Math.Max(clip.Top, bounds.Top) : clip.Top;
        var bottom = vertical ? Math.Min(clip.Bottom, bounds.Bottom) : clip.Bottom;
        return new Rect(
            left,
            top,
            Math.Max(0.0f, right - left),
            Math.Max(0.0f, bottom - top));
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
        UiClipState? RoundedClip,
        int IndexStart,
        int IndexCount);

    private sealed record UiClipState(
        UiClipState? Parent,
        Rect Bounds,
        float Radius,
        Matrix3x2 InverseTransform);
}
