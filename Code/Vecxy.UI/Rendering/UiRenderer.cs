using System.Diagnostics;
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
    private readonly UiPerformanceStatistics _statistics;
    private readonly List<float> _vertices = [];
    private readonly List<uint> _indices = [];
    private readonly List<Batch> _batches = [];
    private readonly List<Vector2> _roundedPerimeter = new(25);
    private readonly List<Vector2> _roundedOuter = new(24);
    private readonly List<Vector2> _roundedInner = new(24);
    private readonly UiClipState?[] _roundedClipStack = new UiClipState[MaxRoundedClips];
    private Matrix3x2 _transform = Matrix3x2.Identity;
    private UiAxisClipState? _axisClip;
    private UiClipState? _roundedClip;
    private UiScrollState? _scrollState;
    private uint _program;
    private uint _whiteTexture;
    private uint _layerVertexArray;
    private uint _layerVertexBuffer;
    private int _layerQuadWidth;
    private int _layerQuadHeight;
    private readonly Dictionary<UiDocument, LayerCache> _documentLayers =
        new(ReferenceEqualityComparer.Instance);
    private int _viewportUniform;
    private int _textureUniform;
    private int _translationUniform;
    private int _roundedClipCountUniform;
    private readonly int[] _roundedClipBoundsUniforms = new int[MaxRoundedClips];
    private readonly int[] _roundedClipRadiusUniforms = new int[MaxRoundedClips];
    private readonly int[] _roundedClipMatrixAUniforms = new int[MaxRoundedClips];
    private readonly int[] _roundedClipMatrixBUniforms = new int[MaxRoundedClips];
    private int _visibleElements;
    private int _imageElements;
    private int _shadowDefinitions;
    private int _shadowLayers;
    private bool _shadowsEnabled = true;
    private bool _disposed;

    public UiRenderer(GraphicsDevice device, UiPerformanceStatistics statistics)
    {
        _device = device;
        _statistics = statistics;
    }

    public void Draw(IReadOnlyList<UiDocument> documents, int width, int height, UiConfig? settings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var renderStarted = Stopwatch.GetTimestamp();
        var allocatedBeforeRender = GC.GetAllocatedBytesForCurrentThread();
        double tessellationMilliseconds = 0;
        double uploadMilliseconds = 0;
        double layerDrawMilliseconds = 0;
        double compositeMilliseconds = 0;
        _shadowsEnabled = settings?.EnableShadows ?? true;
        EnsureResources();
        var gl = _device.GL;
        gl.GetInteger(GetPName.DrawFramebufferBinding, out var destinationFramebuffer);
        EnsureLayerQuad(width, height);
        RemoveUnusedLayerCaches(documents);

        foreach (var document in documents)
        {
            var documentStatistics = _statistics.GetDocument(document);
            documentStatistics.Visible = document.IsVisible;
            if (!document.IsVisible)
                continue;
            if (!_documentLayers.TryGetValue(document, out var layer))
            {
                layer = new LayerCache();
                _documentLayers.Add(document, layer);
            }
            EnsureLayerCache(layer, width, height, (uint)destinationFramebuffer);
            var geometrySignature = HashCode.Combine(width, height, document.GeometryVersion);
            var renderSignature = HashCode.Combine(geometrySignature, document.RenderVersion);
            documentStatistics.RebuiltThisFrame = geometrySignature != layer.GeometrySignature;
            if (geometrySignature != layer.GeometrySignature)
            {
                documentStatistics.Rebuilds++;
                documentStatistics.LastRebuildFrame = _statistics.Frame;
                var tessellationStarted = Stopwatch.GetTimestamp();
                _vertices.Clear();
                _indices.Clear();
                _batches.Clear();
                _visibleElements = 0;
                _imageElements = 0;
                _shadowDefinitions = 0;
                _shadowLayers = 0;
                _axisClip = null;
                _scrollState = null;
                PaintElement(
                    document,
                    document.Root,
                    new Rect(0, 0, width, height),
                    1.0f,
                    document.LayoutScale,
                    Vector2.Zero,
                    Matrix3x2.Identity,
                    null,
                    null);
                tessellationMilliseconds += Stopwatch.GetElapsedTime(tessellationStarted).TotalMilliseconds;
                layer.DrawBatches.Clear();
                layer.DrawBatches.AddRange(_batches);
                var uploadStarted = Stopwatch.GetTimestamp();
                UploadGeometry(layer);
                uploadMilliseconds += Stopwatch.GetElapsedTime(uploadStarted).TotalMilliseconds;
                layer.ContentBounds = CalculateGeometryBounds(width, height);
                layer.GeometrySignature = geometrySignature;
                UpdateLayerGeometryStatistics(document, layer);
            }
            else
                documentStatistics.CacheHits++;

            if (renderSignature != layer.RenderSignature)
            {
                var layerDrawStarted = Stopwatch.GetTimestamp();
                RenderLayerCache(
                    layer,
                    width,
                    height,
                    (uint)destinationFramebuffer,
                    layer.ContentBounds);
                layerDrawMilliseconds += Stopwatch.GetElapsedTime(layerDrawStarted).TotalMilliseconds;
                layer.RenderSignature = renderSignature;
            }

            if (layer.HasContent)
            {
                var compositeStarted = Stopwatch.GetTimestamp();
                DrawLayerCache(layer, width, height);
                compositeMilliseconds += Stopwatch.GetElapsedTime(compositeStarted).TotalMilliseconds;
            }
            UpdateDocumentStatistics(document, layer, documentStatistics);
            _statistics.Accumulate(documentStatistics);
        }
        _statistics.CompleteRender(
            documents,
            Stopwatch.GetElapsedTime(renderStarted).TotalMilliseconds,
            tessellationMilliseconds,
            uploadMilliseconds,
            layerDrawMilliseconds,
            compositeMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBeforeRender);
    }

    private void PaintElement(
        UiDocument document,
        UiElement element,
        Rect clip,
        float inheritedOpacity,
        float scale,
        Vector2 translation,
        Matrix3x2 parentTransform,
        UiAxisClipState? axisClip,
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
        if (element.TagName == "progress")
            bounds = bounds with { Width = bounds.Width * element.Progress };
        if (style.Display == "none" || style.Visibility == "hidden" ||
            bounds.Width <= 0 || bounds.Height <= 0)
            return;
        if (IsOutsideVirtualViewport(bounds, _scrollState, scale))
            return;
        _visibleElements++;
        _shadowDefinitions += style.BoxShadows.Count;

        var previousTransform = _transform;
        var previousAxisClip = _axisClip;
        var previousRoundedClip = _roundedClip;
        var previousScrollState = _scrollState;
        var renderTransform = element.RenderTransform with
        {
            Translation = element.RenderTransform.Translation * scale
        };
        _transform = renderTransform.ToMatrix(bounds) * parentTransform;
        _axisClip = axisClip;
        _roundedClip = roundedClip;

        var opacity = inheritedOpacity * element.RenderOpacity;
        if (_shadowsEnabled)
            PaintBoxShadows(style, bounds, opacity, scale, clip, false);
        var isRadialProgress = element.TagName == "radial-progress";
        if (!isRadialProgress)
        {
            var renderedBackground = element.RenderBackgroundColor;
            var background = renderedBackground with { W = renderedBackground.W * opacity };
            if (background.W > 0.001f)
                AddRoundedQuad(bounds, background, null, clip, style.BorderRadius * scale);
        }

        var image = document.ResolveImage(element);
        if (image is { } resolvedImage)
        {
            _imageElements++;
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

        if (_shadowsEnabled)
            PaintBoxShadows(style, bounds, opacity, scale, clip, true);

        if (isRadialProgress)
            PaintRadialProgress(element, style, bounds, opacity, scale, clip);
        else if (style.BorderWidth > 0.0f && style.BorderColor.W > 0.001f)
            AddBorder(bounds, style.BorderWidth * scale, style.BorderRadius * scale, style.BorderColor with { W = style.BorderColor.W * opacity }, clip);

        var clipsChildrenX = style.OverflowX is "hidden" or "scroll" or "auto";
        var clipsChildrenY = style.OverflowY is "hidden" or "scroll" or "auto";
        var transformedBounds = TransformBounds(bounds, _transform);
        var childClip = ClipAxes(clip, transformedBounds, clipsChildrenX, clipsChildrenY);
        var childAxisClip = axisClip;
        if (clipsChildrenX || clipsChildrenY)
        {
            childAxisClip = new UiAxisClipState(
                axisClip,
                transformedBounds,
                clipsChildrenX,
                clipsChildrenY,
                _scrollState);
        }
        var childRoundedClip = roundedClip;
        if ((clipsChildrenX || clipsChildrenY) && style.BorderRadius > 0.0f &&
            Matrix3x2.Invert(_transform, out var inverseTransform))
        {
            childRoundedClip = new UiClipState(
                roundedClip,
                bounds,
                style.BorderRadius * scale,
                inverseTransform,
                _scrollState);
            _roundedClip = childRoundedClip;
        }

        if (element.TagName == "text" && element.Text.Length > 0)
        {
            var renderedColor = element.RenderColor;
            var color = renderedColor with { W = renderedColor.W * opacity };
            var textBounds = TextContentBounds(document, element, style, bounds, scale);
            if (element.Font is { } font && document.ResolveFontTexture(element) is { } fontTexture)
                UiBitmapFont.Paint(this, element, font, fontTexture, element.Text, textBounds, style.FontSize * scale, color, clip, style.TextAlign, style.VerticalAlign, style.WhiteSpace is "normal" or "pre-wrap");
            else
                UiFallbackFont.Paint(this, element, element.Text, textBounds, style.FontSize * scale, color, clip, style.TextAlign, style.VerticalAlign, style.WhiteSpace is "normal" or "pre-wrap");
        }

        if (style.OverflowX is "scroll" or "auto" || style.OverflowY is "scroll" or "auto")
            _scrollState = new UiScrollState(previousScrollState, element, scale);

        var childTranslation = translation;
        foreach (var child in element.ChildrenInPaintOrder())
            PaintElement(
                document,
                child,
                childClip,
                opacity,
                scale,
                childTranslation,
                _transform,
                childAxisClip,
                childRoundedClip);

        // Scrollbars belong to the viewport, not to its translated contents.
        _scrollState = previousScrollState;
        PaintScrollbars(element, bounds, opacity, scale, clip);
        _transform = previousTransform;
        _axisClip = previousAxisClip;
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
        for (var index = style.BoxShadows.Count - 1; index >= 0; index--)
        {
            var shadow = style.BoxShadows[index];
            if (shadow.Inset != inset)
                continue;
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
                _shadowLayers += steps;
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
                _shadowLayers++;
                AddRoundedQuad(shadowBounds, color, null, clip, radius);
                continue;
            }

            var layers = Math.Clamp((int)MathF.Ceiling(blur * 0.5f), 4, 16);
            _shadowLayers += layers;
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

    private static Rect TextContentBounds(
        UiDocument document,
        UiElement element,
        UiComputedStyle style,
        Rect bounds,
        float scale)
    {
        var viewportWidth = document.Root.Bounds.Width;
        var viewportHeight = document.Root.Bounds.Height;
        var percentageReference = element.Bounds.Width;
        var left = style.BorderWidth + ResolveEdge(style.Padding.Left, percentageReference, viewportWidth, viewportHeight);
        var right = style.BorderWidth + ResolveEdge(style.Padding.Right, percentageReference, viewportWidth, viewportHeight);
        var top = style.BorderWidth + ResolveEdge(style.Padding.Top, percentageReference, viewportWidth, viewportHeight);
        var bottom = style.BorderWidth + ResolveEdge(style.Padding.Bottom, percentageReference, viewportWidth, viewportHeight);
        var horizontal = (left + right) * scale;
        var vertical = (top + bottom) * scale;
        return new Rect(
            bounds.X + left * scale,
            bounds.Y + top * scale,
            Math.Max(0.0f, bounds.Width - horizontal),
            Math.Max(0.0f, bounds.Height - vertical));
    }

    private static float ResolveEdge(
        UiLength value,
        float percentageReference,
        float viewportWidth,
        float viewportHeight) =>
        value.Unit switch
        {
            EUiLengthUnit.Auto => 0.0f,
            EUiLengthUnit.Percent => percentageReference * value.Value * 0.01f,
            _ => UiLayout.ResolvePoints(value, viewportWidth, viewportHeight)
        };

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

    private void PaintRadialProgress(
        UiElement element,
        UiComputedStyle style,
        Rect bounds,
        float opacity,
        float scale,
        Rect clip)
    {
        var thickness = Math.Max(1.0f, style.BorderWidth * scale);
        var radius = Math.Min(bounds.Width, bounds.Height) * 0.5f;
        thickness = Math.Min(thickness, radius);
        var center = new Vector2(
            bounds.X + bounds.Width * 0.5f,
            bounds.Y + bounds.Height * 0.5f);
        var track = style.BorderColor with { W = style.BorderColor.W * opacity };
        var progress = element.RenderColor with { W = element.RenderColor.W * opacity };

        if (track.W > 0.001f)
            AddArcRing(center, radius, thickness, -MathF.PI * 0.5f, MathF.PI * 1.5f, track, clip);
        if (progress.W > 0.001f && element.Progress > 0.001f)
        {
            AddArcRing(
                center,
                radius,
                thickness,
                -MathF.PI * 0.5f,
                -MathF.PI * 0.5f + MathF.Tau * element.Progress,
                progress,
                clip);
        }
    }

    private void AddArcRing(
        Vector2 center,
        float outerRadius,
        float thickness,
        float startRadians,
        float endRadians,
        Vector4 color,
        Rect clip)
    {
        var arcBounds = new Rect(
            center.X - outerRadius,
            center.Y - outerRadius,
            outerRadius * 2.0f,
            outerRadius * 2.0f);
        if (arcBounds.Width <= 0.0f || arcBounds.Height <= 0.0f || endRadians <= startRadians)
            return;

        var sweep = endRadians - startRadians;
        var segments = Math.Max(1, (int)MathF.Ceiling(64.0f * sweep / MathF.Tau));
        var innerRadius = Math.Max(0.0f, outerRadius - thickness);
        var firstVertex = (uint)(_vertices.Count / VertexStride);

        for (var index = 0; index <= segments; index++)
        {
            var angle = float.Lerp(startRadians, endRadians, index / (float)segments);
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var outer = center + direction * outerRadius;
            var inner = center + direction * innerRadius;
            AddVertex(outer.X, outer.Y, 0, 0, color);
            AddVertex(inner.X, inner.Y, 0, 0, color);
        }

        var indexStart = _indices.Count;
        for (var index = 0; index < segments; index++)
        {
            var outerCurrent = firstVertex + (uint)(index * 2);
            var innerCurrent = outerCurrent + 1;
            var outerNext = outerCurrent + 2;
            var innerNext = outerCurrent + 3;
            _indices.Add(outerCurrent);
            _indices.Add(innerCurrent);
            _indices.Add(innerNext);
            _indices.Add(innerNext);
            _indices.Add(outerNext);
            _indices.Add(outerCurrent);
        }

        AddBatch(null, null, clip, indexStart, segments * 6);
    }

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
        if (bounds.Width <= 0.0f || bounds.Height <= 0.0f)
            return;

        const int segmentsPerCorner = 6;
        var firstVertex = (uint)(_vertices.Count / VertexStride);
        var center = new Vector2(bounds.X + bounds.Width * 0.5f, bounds.Y + bounds.Height * 0.5f);
        AddMappedVertex(center.X, center.Y, bounds, uv, color);
        var perimeter = _roundedPerimeter;
        perimeter.Clear();
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
        if (width <= 0.0f || bounds.Width <= 0.0f || bounds.Height <= 0.0f)
            return;

        const int segmentsPerCorner = 6;
        var outer = _roundedOuter;
        outer.Clear();
        FillRoundedPerimeter(outer, bounds, radius, segmentsPerCorner);
        var innerBounds = Expand(bounds, -width);
        var innerRadius = Math.Max(0.0f, radius - width);
        var inner = _roundedInner;
        inner.Clear();
        if (innerBounds.Width <= 0.0f || innerBounds.Height <= 0.0f)
        {
            var center = new Vector2(bounds.X + bounds.Width * 0.5f, bounds.Y + bounds.Height * 0.5f);
            for (var index = 0; index < outer.Count; index++)
                inner.Add(center);
        }
        else
        {
            FillRoundedPerimeter(inner, innerBounds, innerRadius, segmentsPerCorner);
        }
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

    private static void FillRoundedPerimeter(
        List<Vector2> result,
        Rect bounds,
        float radius,
        int segmentsPerCorner)
    {
        radius = ClampRadius(bounds, radius);
        AddCorner(result, new Vector2(bounds.Right - radius, bounds.Top + radius), -90.0f, radius, segmentsPerCorner);
        AddCorner(result, new Vector2(bounds.Right - radius, bounds.Bottom - radius), 0.0f, radius, segmentsPerCorner);
        AddCorner(result, new Vector2(bounds.Left + radius, bounds.Bottom - radius), 90.0f, radius, segmentsPerCorner);
        AddCorner(result, new Vector2(bounds.Left + radius, bounds.Top + radius), 180.0f, radius, segmentsPerCorner);
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
        if (bounds.Width <= 0 || bounds.Height <= 0)
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
            ReferenceEquals(_batches[^1].AxisClip, _axisClip) &&
            ReferenceEquals(_batches[^1].RoundedClip, _roundedClip) &&
            ReferenceEquals(_batches[^1].ScrollState, _scrollState))
        {
            _batches[^1] = _batches[^1] with { IndexCount = _batches[^1].IndexCount + indexCount };
        }
        else
        {
            _batches.Add(new Batch(
                texture,
                sampler,
                clip,
                _axisClip,
                _roundedClip,
                _scrollState,
                indexStart,
                indexCount));
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

    private unsafe void UploadGeometry(LayerCache layer)
    {
        var gl = _device.GL;
        EnsureGeometryResources(layer);
        gl.BindVertexArray(layer.VertexArray);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, layer.VertexBuffer);
        var vertexSpan = CollectionsMarshal.AsSpan(_vertices);
        var requiredVertexBytes = checked((nuint)(_vertices.Count * sizeof(float)));
        EnsureBufferCapacity(
            gl,
            BufferTargetARB.ArrayBuffer,
            requiredVertexBytes,
            ref layer.VertexBufferCapacity);
        fixed (float* vertices = vertexSpan)
        {
            if (requiredVertexBytes > 0)
                gl.BufferSubData(
                BufferTargetARB.ArrayBuffer,
                0,
                requiredVertexBytes,
                vertices);
        }

        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, layer.IndexBuffer);
        var indexSpan = CollectionsMarshal.AsSpan(_indices);
        var requiredIndexBytes = checked((nuint)(_indices.Count * sizeof(uint)));
        EnsureBufferCapacity(
            gl,
            BufferTargetARB.ElementArrayBuffer,
            requiredIndexBytes,
            ref layer.IndexBufferCapacity);
        fixed (uint* indices = indexSpan)
        {
            if (requiredIndexBytes > 0)
                gl.BufferSubData(
                BufferTargetARB.ElementArrayBuffer,
                0,
                requiredIndexBytes,
                indices);
        }
        layer.IndexCount = _indices.Count;
    }

    private unsafe void DrawUploadedGeometry(
        LayerCache layer,
        int width,
        int height,
        bool transparentTarget)
    {
        var gl = _device.GL;
        gl.BindVertexArray(layer.VertexArray);
        gl.UseProgram(_program);
        gl.Uniform2(_viewportUniform, (float)width, (float)height);
        gl.Uniform1(_textureUniform, 0);
        gl.Disable(EnableCap.DepthTest);
        gl.DepthMask(false);
        gl.Enable(EnableCap.Blend);
        if (transparentTarget)
        {
            gl.BlendFuncSeparate(
                BlendingFactor.SrcAlpha,
                BlendingFactor.OneMinusSrcAlpha,
                BlendingFactor.One,
                BlendingFactor.OneMinusSrcAlpha);
        }
        else
        {
            gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        }
        gl.Enable(EnableCap.ScissorTest);

        foreach (var batch in layer.DrawBatches)
        {
            ApplyRoundedClips(gl, batch.RoundedClip);
            var translation = ResolveScrollTranslation(batch.ScrollState);
            gl.Uniform2(_translationUniform, translation.X, translation.Y);
            var resolvedClip = ResolveAxisClip(batch.AxisClip, width, height);
            var x = Math.Clamp((int)MathF.Floor(resolvedClip.X), 0, width);
            var y = Math.Clamp((int)MathF.Floor(height - resolvedClip.Bottom), 0, height);
            var right = Math.Clamp((int)MathF.Ceiling(resolvedClip.Right), 0, width);
            var top = Math.Clamp((int)MathF.Ceiling(height - resolvedClip.Top), 0, height);
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

    private unsafe void EnsureGeometryResources(LayerCache layer)
    {
        if (layer.VertexArray != 0)
            return;
        var gl = _device.GL;
        layer.VertexArray = gl.GenVertexArray();
        layer.VertexBuffer = gl.GenBuffer();
        layer.IndexBuffer = gl.GenBuffer();
        gl.BindVertexArray(layer.VertexArray);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, layer.VertexBuffer);
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, layer.IndexBuffer);
        var stride = (uint)(VertexStride * sizeof(float));
        gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, (void*)0);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, (void*)(2 * sizeof(float)));
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, stride, (void*)(4 * sizeof(float)));
        gl.EnableVertexAttribArray(2);
        gl.BindVertexArray(0);
    }

    private static Vector2 ResolveScrollTranslation(UiScrollState? state)
    {
        var translation = Vector2.Zero;
        for (; state is not null; state = state.Parent)
            translation -= state.Element.ScrollOffset * state.Scale;
        return translation;
    }

    private static bool IsOutsideVirtualViewport(
        Rect staticBounds,
        UiScrollState? scrollState,
        float scale)
    {
        var translatedBounds = staticBounds;
        var contentTranslation = ResolveScrollTranslation(scrollState);
        translatedBounds = translatedBounds with
        {
            X = translatedBounds.X + contentTranslation.X,
            Y = translatedBounds.Y + contentTranslation.Y
        };

        for (var state = scrollState; state is not null; state = state.Parent)
        {
            if (!state.Element.UsesVirtualization)
                continue;

            var viewport = Scale(state.Element.Bounds, state.Scale);
            var outerTranslation = ResolveScrollTranslation(state.Parent);
            viewport = viewport with
            {
                X = viewport.X + outerTranslation.X,
                Y = viewport.Y + outerTranslation.Y
            };
            var overscanX = viewport.Width;
            var overscanY = viewport.Height;
            var clipsX = state.Element.ComputedStyle.OverflowX is "scroll" or "auto";
            var clipsY = state.Element.ComputedStyle.OverflowY is "scroll" or "auto";
            if (clipsX &&
                (translatedBounds.Right < viewport.Left - overscanX ||
                 translatedBounds.Left > viewport.Right + overscanX))
                return true;
            if (clipsY &&
                (translatedBounds.Bottom < viewport.Top - overscanY ||
                 translatedBounds.Top > viewport.Bottom + overscanY))
                return true;
        }
        return false;
    }

    private static Rect ResolveAxisClip(UiAxisClipState? state, int width, int height)
    {
        var clip = new Rect(0, 0, width, height);
        for (; state is not null; state = state.Parent)
        {
            var bounds = state.Bounds;
            var translation = ResolveScrollTranslation(state.ScrollState);
            bounds = bounds with { X = bounds.X + translation.X, Y = bounds.Y + translation.Y };
            clip = ClipAxes(clip, bounds, state.ClipX, state.ClipY);
        }
        return clip;
    }

    private unsafe void EnsureLayerCache(
        LayerCache layer,
        int width,
        int height,
        uint destinationFramebuffer)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (layer.Framebuffer != 0 && layer.Width == width && layer.Height == height)
            return;

        var gl = _device.GL;
        layer.Dispose(gl);

        layer.Framebuffer = gl.GenFramebuffer();
        layer.Texture = gl.GenTexture();
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, layer.Framebuffer);
        gl.BindTexture(TextureTarget.Texture2D, layer.Texture);
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
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        gl.FramebufferTexture2D(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D,
            layer.Texture,
            0);
        var status = gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, destinationFramebuffer);
        gl.Viewport(0, 0, (uint)width, (uint)height);
        if (status != GLEnum.FramebufferComplete)
            throw new InvalidOperationException($"UI layer framebuffer is incomplete: {status}.");

        gl.BindFramebuffer(FramebufferTarget.Framebuffer, layer.Framebuffer);
        gl.Viewport(0, 0, (uint)width, (uint)height);
        gl.Disable(EnableCap.ScissorTest);
        gl.ClearColor(0.0f, 0.0f, 0.0f, 0.0f);
        gl.Clear(ClearBufferMask.ColorBufferBit);
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, destinationFramebuffer);
        gl.Viewport(0, 0, (uint)width, (uint)height);

        layer.Width = width;
        layer.Height = height;
        layer.HasContent = false;
        layer.GeometrySignature = int.MinValue;
        layer.RenderSignature = int.MinValue;
    }

    private void RenderLayerCache(
        LayerCache layer,
        int width,
        int height,
        uint destinationFramebuffer,
        Rect contentBounds)
    {
        var gl = _device.GL;
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, layer.Framebuffer);
        gl.Viewport(0, 0, (uint)width, (uint)height);
        gl.Enable(EnableCap.ScissorTest);
        SetScissor(gl, Union(layer.ContentBounds, contentBounds), width, height);
        gl.ClearColor(0.0f, 0.0f, 0.0f, 0.0f);
        gl.Clear(ClearBufferMask.ColorBufferBit);
        if (layer.IndexCount > 0)
            DrawUploadedGeometry(layer, width, height, transparentTarget: true);
        layer.HasContent = layer.IndexCount > 0;
        layer.ContentBounds = contentBounds;
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, destinationFramebuffer);
        gl.Viewport(0, 0, (uint)width, (uint)height);
    }

    private unsafe void DrawLayerCache(LayerCache layer, int width, int height)
    {
        var gl = _device.GL;
        gl.BindVertexArray(_layerVertexArray);
        gl.UseProgram(_program);
        gl.Uniform2(_viewportUniform, (float)width, (float)height);
        gl.Uniform1(_textureUniform, 0);
        gl.Uniform2(_translationUniform, 0.0f, 0.0f);
        gl.Uniform1(_roundedClipCountUniform, 0);
        gl.ActiveTexture(TextureUnit.Texture0);
        gl.BindTexture(TextureTarget.Texture2D, layer.Texture);
        gl.Disable(EnableCap.DepthTest);
        gl.DepthMask(false);
        gl.Enable(EnableCap.Blend);
        gl.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
        gl.Enable(EnableCap.ScissorTest);
        SetScissor(gl, layer.ContentBounds, width, height);
        gl.DrawArrays(PrimitiveType.Triangles, 0, 6);
        gl.Disable(EnableCap.ScissorTest);
        gl.Disable(EnableCap.Blend);
        gl.DepthMask(true);
        gl.BindVertexArray(0);
    }

    private void EnsureLayerQuad(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (_layerQuadWidth == width && _layerQuadHeight == height)
            return;
        UploadLayerQuad(width, height);
        _layerQuadWidth = width;
        _layerQuadHeight = height;
    }

    private unsafe void UploadLayerQuad(int width, int height)
    {
        var vertices = new float[]
        {
            0, 0, 0, 1, 1, 1, 1, 1,
            0, height, 0, 0, 1, 1, 1, 1,
            width, height, 1, 0, 1, 1, 1, 1,
            0, 0, 0, 1, 1, 1, 1, 1,
            width, height, 1, 0, 1, 1, 1, 1,
            width, 0, 1, 1, 1, 1, 1, 1
        };
        var gl = _device.GL;
        gl.BindVertexArray(_layerVertexArray);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _layerVertexBuffer);
        fixed (float* data = vertices)
        {
            gl.BufferData(
                BufferTargetARB.ArrayBuffer,
                (nuint)(vertices.Length * sizeof(float)),
                data,
                BufferUsageARB.StaticDraw);
        }
        gl.BindVertexArray(0);
    }

    private void RemoveUnusedLayerCaches(IReadOnlyList<UiDocument> documents)
    {
        if (_documentLayers.Count <= documents.Count)
            return;
        var gl = _device.GL;
        foreach (var document in _documentLayers.Keys
                     .Where(candidate => !documents.Contains(candidate))
                     .ToArray())
        {
            _documentLayers[document].Dispose(gl);
            _documentLayers.Remove(document);
        }
    }

    private void UpdateLayerGeometryStatistics(UiDocument document, LayerCache layer)
    {
        var elements = 0;
        var interactiveElements = 0;
        var textElements = 0;
        foreach (var element in document.Root.DescendantsAndSelf())
        {
            elements++;
            if (element.IsHitTestInteractive)
                interactiveElements++;
            if (element.TagName == "text")
                textElements++;
        }

        var textureSwitches = 0;
        RenderTexture? previousTexture = null;
        var hasPrevious = false;
        var roundedClipBatches = 0;
        foreach (var batch in _batches)
        {
            if (hasPrevious && !ReferenceEquals(previousTexture, batch.Texture))
                textureSwitches++;
            previousTexture = batch.Texture;
            hasPrevious = true;
            if (batch.RoundedClip is not null)
                roundedClipBatches++;
        }

        layer.Elements = elements;
        layer.VisibleElements = _visibleElements;
        layer.InteractiveElements = interactiveElements;
        layer.TextElements = textElements;
        layer.ImageElements = _imageElements;
        layer.ShadowDefinitions = _shadowDefinitions;
        layer.ShadowLayers = _shadowLayers;
        layer.Vertices = _vertices.Count / VertexStride;
        layer.Indices = _indices.Count;
        layer.Batches = _batches.Count;
        layer.TextureSwitches = textureSwitches;
        layer.RoundedClipBatches = roundedClipBatches;
        layer.LastUploadBytes = checked((long)_vertices.Count * sizeof(float) +
                                        (long)_indices.Count * sizeof(uint));
    }

    private static void UpdateDocumentStatistics(
        UiDocument document,
        LayerCache layer,
        UiDocumentStatistics statistics)
    {
        statistics.Elements = layer.Elements;
        statistics.VisibleElements = layer.VisibleElements;
        statistics.InteractiveElements = layer.InteractiveElements;
        statistics.TextElements = layer.TextElements;
        statistics.ImageElements = layer.ImageElements;
        statistics.ShadowDefinitions = layer.ShadowDefinitions;
        statistics.ShadowLayers = layer.ShadowLayers;
        statistics.ActiveAnimations = document.ActiveAnimationCount;
        statistics.Vertices = layer.Vertices;
        statistics.Indices = layer.Indices;
        statistics.Batches = layer.Batches;
        statistics.TextureSwitches = layer.TextureSwitches;
        statistics.RoundedClipBatches = layer.RoundedClipBatches;
        statistics.UpdateVersions(
            document.StyleVersion,
            document.LayoutVersion,
            document.VisualVersion);
        statistics.StylePasses = document.StylePasses;
        statistics.LayoutPasses = document.LayoutPasses;
        statistics.AnimationTreeScans = document.AnimationTreeScans;
        statistics.LayerWidth = layer.Width;
        statistics.LayerHeight = layer.Height;
        statistics.LayerBytes = checked((long)layer.Width * layer.Height * 4);
        statistics.ContentPixels = checked((long)MathF.Ceiling(layer.ContentBounds.Width) *
                                           (long)MathF.Ceiling(layer.ContentBounds.Height));
        statistics.UploadBytes = statistics.RebuiltThisFrame ? layer.LastUploadBytes : 0;
    }

    private Rect CalculateGeometryBounds(int width, int height)
    {
        if (_vertices.Count < VertexStride)
            return new Rect(0, 0, 0, 0);
        var left = float.PositiveInfinity;
        var top = float.PositiveInfinity;
        var right = float.NegativeInfinity;
        var bottom = float.NegativeInfinity;
        for (var index = 0; index < _vertices.Count; index += VertexStride)
        {
            var x = _vertices[index];
            var y = _vertices[index + 1];
            left = Math.Min(left, x);
            top = Math.Min(top, y);
            right = Math.Max(right, x);
            bottom = Math.Max(bottom, y);
        }
        left = Math.Clamp(left, 0, width);
        top = Math.Clamp(top, 0, height);
        right = Math.Clamp(right, 0, width);
        bottom = Math.Clamp(bottom, 0, height);
        return new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    private static Rect Union(Rect first, Rect second)
    {
        if (first.Width <= 0 || first.Height <= 0)
            return second;
        if (second.Width <= 0 || second.Height <= 0)
            return first;
        var left = Math.Min(first.Left, second.Left);
        var top = Math.Min(first.Top, second.Top);
        var right = Math.Max(first.Right, second.Right);
        var bottom = Math.Max(first.Bottom, second.Bottom);
        return new Rect(left, top, right - left, bottom - top);
    }

    private static void SetScissor(GL gl, Rect bounds, int width, int height)
    {
        var x = Math.Clamp((int)MathF.Floor(bounds.X), 0, width);
        var y = Math.Clamp((int)MathF.Floor(height - bounds.Bottom), 0, height);
        var right = Math.Clamp((int)MathF.Ceiling(bounds.Right), 0, width);
        var top = Math.Clamp((int)MathF.Ceiling(height - bounds.Top), 0, height);
        gl.Scissor(x, y, (uint)Math.Max(0, right - x), (uint)Math.Max(0, top - y));
    }

    private static unsafe void EnsureBufferCapacity(
        GL gl,
        BufferTargetARB target,
        nuint required,
        ref nuint capacity)
    {
        if (required <= capacity)
            return;
        capacity = 4096;
        while (capacity < required)
            capacity *= 2;
        gl.BufferData(target, capacity, null, BufferUsageARB.DynamicDraw);
    }

    private unsafe void EnsureResources()
    {
        if (_program != 0)
            return;

        var gl = _device.GL;
        _program = BuildProgram(gl);
        _viewportUniform = gl.GetUniformLocation(_program, "uViewport");
        _textureUniform = gl.GetUniformLocation(_program, "uTexture");
        _translationUniform = gl.GetUniformLocation(_program, "uTranslation");
        _roundedClipCountUniform = gl.GetUniformLocation(_program, "uRoundedClipCount");
        for (var index = 0; index < MaxRoundedClips; index++)
        {
            _roundedClipBoundsUniforms[index] = gl.GetUniformLocation(_program, $"uRoundedClipBounds[{index}]");
            _roundedClipRadiusUniforms[index] = gl.GetUniformLocation(_program, $"uRoundedClipRadius[{index}]");
            _roundedClipMatrixAUniforms[index] = gl.GetUniformLocation(_program, $"uRoundedClipMatrixA[{index}]");
            _roundedClipMatrixBUniforms[index] = gl.GetUniformLocation(_program, $"uRoundedClipMatrixB[{index}]");
        }
        var stride = (uint)(VertexStride * sizeof(float));
        _layerVertexArray = gl.GenVertexArray();
        _layerVertexBuffer = gl.GenBuffer();
        gl.BindVertexArray(_layerVertexArray);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _layerVertexBuffer);
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
            uniform vec2 uTranslation;
            out vec2 vUv;
            out vec4 vColor;
            void main() {
                vec2 position = aPosition + uTranslation;
                vec2 ndc = vec2(position.x / uViewport.x * 2.0 - 1.0,
                                1.0 - position.y / uViewport.y * 2.0);
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
        var count = 0;
        for (; state is not null && count < MaxRoundedClips; state = state.Parent)
            _roundedClipStack[count++] = state;
        gl.Uniform1(_roundedClipCountUniform, count);
        for (var index = 0; index < count; index++)
        {
            var item = _roundedClipStack[count - index - 1]!;
            var translation = ResolveScrollTranslation(item.ScrollState);
            var inverseTransform = Matrix3x2.CreateTranslation(-translation) * item.InverseTransform;
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
                inverseTransform.M11,
                inverseTransform.M21,
                inverseTransform.M31,
                inverseTransform.M12);
            gl.Uniform2(
                _roundedClipMatrixBUniforms[index],
                inverseTransform.M22,
                inverseTransform.M32);
        }
        Array.Clear(_roundedClipStack, 0, count);
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
        foreach (var layer in _documentLayers.Values)
            layer.Dispose(gl);
        _documentLayers.Clear();
        if (_layerVertexBuffer != 0) gl.DeleteBuffer(_layerVertexBuffer);
        if (_layerVertexArray != 0) gl.DeleteVertexArray(_layerVertexArray);
        if (_program != 0) gl.DeleteProgram(_program);
        _layerVertexBuffer = _layerVertexArray = 0;
        _whiteTexture = _program = 0;
    }

    private readonly record struct Batch(
        RenderTexture? Texture,
        TextureSamplerState? Sampler,
        Rect Clip,
        UiAxisClipState? AxisClip,
        UiClipState? RoundedClip,
        UiScrollState? ScrollState,
        int IndexStart,
        int IndexCount);

    private sealed class LayerCache
    {
        public uint Framebuffer;
        public uint Texture;
        public int Width;
        public int Height;
        public uint VertexArray;
        public uint VertexBuffer;
        public uint IndexBuffer;
        public nuint VertexBufferCapacity;
        public nuint IndexBufferCapacity;
        public int IndexCount;
        public int GeometrySignature = int.MinValue;
        public int RenderSignature = int.MinValue;
        public List<Batch> DrawBatches { get; } = [];
        public bool HasContent;
        public Rect ContentBounds;
        public int Elements;
        public int VisibleElements;
        public int InteractiveElements;
        public int TextElements;
        public int ImageElements;
        public int ShadowDefinitions;
        public int ShadowLayers;
        public int Vertices;
        public int Indices;
        public int Batches;
        public int TextureSwitches;
        public int RoundedClipBatches;
        public long LastUploadBytes;

        public void Dispose(GL gl)
        {
            if (Texture != 0) gl.DeleteTexture(Texture);
            if (Framebuffer != 0) gl.DeleteFramebuffer(Framebuffer);
            if (IndexBuffer != 0) gl.DeleteBuffer(IndexBuffer);
            if (VertexBuffer != 0) gl.DeleteBuffer(VertexBuffer);
            if (VertexArray != 0) gl.DeleteVertexArray(VertexArray);
            Framebuffer = Texture = 0;
            VertexArray = VertexBuffer = IndexBuffer = 0;
            VertexBufferCapacity = IndexBufferCapacity = 0;
            IndexCount = 0;
            DrawBatches.Clear();
            Width = Height = 0;
            GeometrySignature = int.MinValue;
            RenderSignature = int.MinValue;
            HasContent = false;
            ContentBounds = default;
            Elements = VisibleElements = InteractiveElements = 0;
            TextElements = ImageElements = 0;
            ShadowDefinitions = ShadowLayers = 0;
            Vertices = Indices = Batches = 0;
            TextureSwitches = RoundedClipBatches = 0;
            LastUploadBytes = 0;
        }
    }

    private sealed record UiClipState(
        UiClipState? Parent,
        Rect Bounds,
        float Radius,
        Matrix3x2 InverseTransform,
        UiScrollState? ScrollState);

    private sealed record UiAxisClipState(
        UiAxisClipState? Parent,
        Rect Bounds,
        bool ClipX,
        bool ClipY,
        UiScrollState? ScrollState);

    private sealed record UiScrollState(
        UiScrollState? Parent,
        UiElement Element,
        float Scale);
}
