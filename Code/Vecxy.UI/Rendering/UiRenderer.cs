using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Silk.NET.OpenGL;
using Vecxy.Assets;
using Vecxy.Kernel;
using Vecxy.Rendering;
using RenderTexture = Vecxy.Rendering.Texture;

namespace Vecxy.UI;

internal sealed class UiRenderer : IDisposable
{
    private const int VertexStride = 9;
    private const int MaxRoundedClips = 8;
    private const int MaxTextureSlots = 16;
    private readonly GraphicsDevice _device;
    private readonly UiPerformanceStatistics _statistics;
    private readonly RenderTexture _primitiveAtlas;
    private readonly List<float> _vertices = [];
    private readonly List<uint> _indices = [];
    private readonly List<Batch> _batches = [];
    private readonly List<Vector2> _roundedPerimeter = new(25);
    private readonly List<Vector2> _roundedOuter = new(24);
    private readonly List<Vector2> _roundedInner = new(24);
    private readonly UiClipState?[] _roundedClipStack = new UiClipState[MaxRoundedClips];
    private readonly ConditionalWeakTable<UiElement, ElementPaintCache> _paintCaches = new();
    private readonly Dictionary<RenderTexture, int> _textureSlots = new(ReferenceEqualityComparer.Instance);
    private readonly List<RenderTexture> _geometryTextures = [];
    private Matrix3x2 _transform = Matrix3x2.Identity;
    private UiAxisClipState? _axisClip;
    private UiClipState? _roundedClip;
    private UiScrollState? _scrollState;
    private UiElement? _paintElement;
    private float _paintScale = 1.0f;
    private uint _program;
    private uint _whiteTexture;
    private uint _layerVertexArray;
    private uint _layerVertexBuffer;
    private int _layerQuadWidth;
    private int _layerQuadHeight;
    private readonly Dictionary<UiDocument, LayerCache> _documentLayers =
        new(ReferenceEqualityComparer.Instance);
    private int _viewportUniform;
    private readonly int[] _textureUniforms = new int[MaxTextureSlots];
    private int _translationUniform;
    private int _transformAUniform;
    private int _transformBUniform;
    private int _opacityUniform;
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
    private bool _forceBatchBoundary;
    private bool _disposed;

    public UiRenderer(
        GraphicsDevice device,
        ITextureResolver textures,
        UiPerformanceStatistics statistics)
    {
        _device = device;
        _statistics = statistics;
        _primitiveAtlas = textures.Resolve(CreatePrimitiveAtlas());
    }

    private static TextureAsset CreatePrimitiveAtlas()
    {
        const int width = 96;
        const int height = 48;
        var pixels = new byte[width * height * 4];

        // 32x32 white rounded-rectangle mask at (0, 0), radius 8.
        WriteRoundedMask(pixels, width, 0, 0, 32, 8.0f, 0.0f);
        // 48x48 soft shadow mask at (40, 0). The central opaque area is hidden
        // behind the panel; the outer alpha ramp is the reusable blurred edge.
        WriteRoundedMask(pixels, width, 40, 0, 48, 10.0f, 8.0f);
        return TextureAsset.FromRgba(width, height, pixels);
    }

    private static void WriteRoundedMask(
        byte[] pixels,
        int atlasWidth,
        int offsetX,
        int offsetY,
        int size,
        float radius,
        float blur)
    {
        var inset = blur + 0.5f;
        var half = size * 0.5f - inset;
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var point = new Vector2(x + 0.5f - size * 0.5f, y + 0.5f - size * 0.5f);
            var q = Vector2.Abs(point) - new Vector2(Math.Max(0.0f, half - radius));
            var distance = MathF.Min(MathF.Max(q.X, q.Y), 0.0f) +
                           new Vector2(MathF.Max(q.X, 0.0f), MathF.Max(q.Y, 0.0f)).Length() - radius;
            var coverage = blur <= 0.0f
                ? Math.Clamp(0.5f - distance, 0.0f, 1.0f)
                : Math.Clamp(1.0f - Math.Max(0.0f, distance) / Math.Max(0.001f, blur), 0.0f, 1.0f);
            // Smooth the alpha ramp to avoid banding when the mask is enlarged.
            coverage = coverage * coverage * (3.0f - 2.0f * coverage);
            var index = ((offsetY + y) * atlasWidth + offsetX + x) * 4;
            pixels[index] = pixels[index + 1] = pixels[index + 2] = 255;
            pixels[index + 3] = (byte)Math.Clamp((int)MathF.Round(coverage * 255.0f), 0, 255);
        }
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
            EnsureLayerCache(layer, width, height);
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
                _textureSlots.Clear();
                _geometryTextures.Clear();
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
                CompactDrawBatches();
                tessellationMilliseconds += Stopwatch.GetElapsedTime(tessellationStarted).TotalMilliseconds;
                layer.DrawBatches.Clear();
                layer.DrawBatches.AddRange(_batches);
                layer.Textures.Clear();
                layer.Textures.AddRange(_geometryTextures);
                var uploadStarted = Stopwatch.GetTimestamp();
                UploadGeometry(layer);
                uploadMilliseconds += Stopwatch.GetElapsedTime(uploadStarted).TotalMilliseconds;
                layer.ContentBounds = CalculateGeometryBounds(width, height);
                layer.GeometrySignature = geometrySignature;
                UpdateLayerGeometryStatistics(document, layer);
            }
            else
                documentStatistics.CacheHits++;

            // UI geometry is retained in the document buffers and rendered directly
            // into the destination framebuffer. The previous implementation allocated
            // and cleared a full-resolution RGBA framebuffer for every document, then
            // composited it back. Apart from the memory/bandwidth cost, that made a
            // small changing label or animation flash an entire UI layer.
            if (layer.IndexCount > 0)
            {
                var layerDrawStarted = Stopwatch.GetTimestamp();
                gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)destinationFramebuffer);
                gl.Viewport(0, 0, (uint)width, (uint)height);
                DrawUploadedGeometry(layer, width, height, transparentTarget: false);
                layerDrawMilliseconds += Stopwatch.GetElapsedTime(layerDrawStarted).TotalMilliseconds;
            }
            layer.HasContent = layer.IndexCount > 0;
            layer.RenderSignature = renderSignature;
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
        var fullBounds = Scale(
            new Rect(
                element.Bounds.X + translation.X,
                element.Bounds.Y + translation.Y,
                element.Bounds.Width,
                element.Bounds.Height),
            scale);
        var bounds = fullBounds;
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
        var previousPaintElement = _paintElement;
        var previousPaintScale = _paintScale;
        _paintElement = element;
        _paintScale = scale;
        _transform = parentTransform;
        _axisClip = axisClip;
        _roundedClip = roundedClip;

        // Opacity and transforms are composite properties. They are applied from
        // the live element state while drawing and never baked into geometry.
        var opacity = inheritedOpacity;
        var elementCache = _paintCaches.GetOrCreateValue(element);
        var paintSignature = HashCode.Combine(
            element.LocalVisualVersion,
            element.ComputedStyleVersion,
            element.BoundsVersion,
            element.Progress,
            scale,
            _shadowsEnabled);
        var isRadialProgress = element.TagName == "radial-progress";
        if (!TryAppendCached(elementCache.Background, paintSignature, clip))
        {
            var capture = BeginPaintCapture();
            if (_shadowsEnabled)
                PaintBoxShadows(style, bounds, opacity, scale, clip, false);
            if (!isRadialProgress)
            {
                var renderedBackground = element.RenderBackgroundColor;
                var background = renderedBackground with { W = renderedBackground.W * opacity };
                if (background.W > 0.001f)
                    AddRoundedQuad(bounds, background, null, clip, style.BorderRadius * scale);
            }

            var renderedRadialImage = false;
            var image = document.ResolveImage(element);
            if (image is { } resolvedImage)
            {
                _imageElements++;
                if (isRadialProgress)
                {
                    PaintRadialImage(element, bounds, resolvedImage, opacity, clip);
                    renderedRadialImage = true;
                }
                else
                {
                var backgroundSlice = element.TagName == "image"
                    ? 0.0f
                    : UiLayout.ResolvePoints(
                        style.BackgroundSlice,
                        resolvedImage.Size.X,
                        resolvedImage.Size.Y);
                if (backgroundSlice > 0.0f)
                {
                    AddNineSlice(
                        bounds,
                        Vector4.One with { W = opacity },
                        resolvedImage.Texture,
                        resolvedImage.Uv,
                        Math.Min(resolvedImage.Size.X, resolvedImage.Size.Y),
                        backgroundSlice,
                        backgroundSlice * scale,
                        clip);
                }
                else
                {
                    var (imageBounds, imageUv) = element.TagName == "progress"
                        ? (bounds, resolvedImage.Uv with
                        {
                            Z = float.Lerp(resolvedImage.Uv.X, resolvedImage.Uv.Z, element.Progress)
                        })
                        : FitImage(
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
                }
            }

            if (_shadowsEnabled)
                PaintBoxShadows(style, bounds, opacity, scale, clip, true);

            if (isRadialProgress && !renderedRadialImage)
                PaintRadialProgress(element, style, bounds, opacity, scale, clip);
            else if (style.BorderWidth > 0.0f && style.BorderColor.W > 0.001f)
                AddBorder(bounds, style.BorderWidth * scale, style.BorderRadius * scale, style.BorderColor with { W = style.BorderColor.W * opacity }, clip);
            EndPaintCapture(elementCache.Background, paintSignature, capture);
        }

        var clipsChildrenX = style.OverflowX is "hidden" or "scroll" or "auto";
        var clipsChildrenY = style.OverflowY is "hidden" or "scroll" or "auto";
        var transformedBounds = bounds;
        var childClip = ClipAxes(clip, transformedBounds, clipsChildrenX, clipsChildrenY);
        var childAxisClip = axisClip;
        if (clipsChildrenX || clipsChildrenY)
        {
            childAxisClip = new UiAxisClipState(
                axisClip,
                transformedBounds,
                clipsChildrenX,
                clipsChildrenY,
                element,
                scale,
                _scrollState);
        }
        var childRoundedClip = roundedClip;
        if ((clipsChildrenX || clipsChildrenY) && style.BorderRadius > 0.0f)
        {
            childRoundedClip = new UiClipState(
                roundedClip,
                bounds,
                style.BorderRadius * scale,
                element,
                scale,
                _scrollState);
            _roundedClip = childRoundedClip;
        }

        if (element.TagName == "text" && element.Text.Length > 0)
        {
            var textSignature = HashCode.Combine(paintSignature, element.Text);
            if (!TryAppendCached(elementCache.Text, textSignature, clip))
            {
                var capture = BeginPaintCapture();
                var renderedColor = element.RenderColor;
                var color = renderedColor with { W = renderedColor.W * opacity };
                var textBounds = TextContentBounds(document, element, style, bounds, scale);
                var fontSize = style.FontSize * scale;
                var minimumFontSize = style.MinFontSize * scale;
                var wrap = style.WhiteSpace is "normal" or "pre-wrap";
                if (element.Font is { } font && document.ResolveFontTexture(element) is { } fontTexture)
                {
                    if (style.TextFit == "shrink")
                    {
                        var measured = UiBitmapFont.Measure(
                            element,
                            font,
                            element.Text,
                            fontSize,
                            wrap ? textBounds.Width : float.PositiveInfinity);
                        fontSize = UiTextFit.Shrink(fontSize, minimumFontSize, measured, textBounds);
                    }
                    UiBitmapFont.Paint(this, element, font, fontTexture, element.Text, textBounds, fontSize, color, clip, style.TextAlign, style.VerticalAlign, wrap);
                }
                else
                {
                    if (style.TextFit == "shrink")
                    {
                        var measured = UiFallbackFont.Measure(
                            element,
                            element.Text,
                            fontSize,
                            wrap ? textBounds.Width : float.PositiveInfinity);
                        fontSize = UiTextFit.Shrink(fontSize, minimumFontSize, measured, textBounds);
                    }
                    UiFallbackFont.Paint(this, element, element.Text, textBounds, fontSize, color, clip, style.TextAlign, style.VerticalAlign, wrap);
                }
                EndPaintCapture(elementCache.Text, textSignature, capture);
            }
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
        _paintElement = element;
        _paintScale = scale;
        PaintScrollbars(element, bounds, opacity, scale, clip);
        _transform = previousTransform;
        _axisClip = previousAxisClip;
        _roundedClip = previousRoundedClip;
        _paintElement = previousPaintElement;
        _paintScale = previousPaintScale;
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

            // A blurred shadow is a single reusable 9-slice alpha mask from the
            // primitive atlas. The legacy renderer emitted up to sixteen expanded
            // rounded meshes for one shadow, multiplying vertices and overdraw.
            _shadowLayers++;
            var expansion = Math.Max(1.0f, blur);
            AddNineSlice(
                Expand(shadowBounds, expansion),
                color,
                _primitiveAtlas,
                new Vector4(40.0f / 96.0f, 0.0f, 88.0f / 96.0f, 1.0f),
                48.0f,
                16.0f,
                Math.Max(1.0f, radius + expansion),
                clip);
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
            var clockwiseDepletion = element.Attributes.TryGetValue(
                "clockwise-depletion",
                out var depletionValue) &&
                !depletionValue.Equals("false", StringComparison.OrdinalIgnoreCase);
            var start = clockwiseDepletion
                ? -MathF.PI * 0.5f + MathF.Tau * (1.0f - element.Progress)
                : -MathF.PI * 0.5f;
            var end = clockwiseDepletion
                ? MathF.PI * 1.5f
                : -MathF.PI * 0.5f + MathF.Tau * element.Progress;
            AddArcRing(
                center,
                radius,
                thickness,
                start,
                end,
                progress,
                clip);
        }
    }

    private void PaintRadialImage(
        UiElement element,
        Rect bounds,
        UiResolvedImage image,
        float opacity,
        Rect clip)
    {
        var amount = Math.Clamp(element.Progress, 0.0f, 1.0f);
        if (amount <= 0.001f)
            return;

        var (imageBounds, uv) = FitImage(bounds, image.Uv, image.Size, "contain", "center");
        var center = new Vector2(
            imageBounds.X + imageBounds.Width * 0.5f,
            imageBounds.Y + imageBounds.Height * 0.5f);
        var radius = Math.Min(imageBounds.Width, imageBounds.Height) * 0.5f;
        var clockwiseDepletion = element.Attributes.TryGetValue(
            "clockwise-depletion",
            out var depletionValue) &&
            !depletionValue.Equals("false", StringComparison.OrdinalIgnoreCase);
        var start = clockwiseDepletion
            ? -MathF.PI * 0.5f + MathF.Tau * (1.0f - amount)
            : -MathF.PI * 0.5f;
        var end = clockwiseDepletion
            ? MathF.PI * 1.5f
            : -MathF.PI * 0.5f + MathF.Tau * amount;
        var segments = Math.Max(1, (int)MathF.Ceiling(64.0f * amount));
        var color = Vector4.One with { W = opacity };
        var firstVertex = (uint)(_vertices.Count / VertexStride);
        AddMappedVertex(center.X, center.Y, imageBounds, uv, color);
        for (var index = 0; index <= segments; index++)
        {
            var angle = float.Lerp(start, end, index / (float)segments);
            AddMappedVertex(
                center.X + MathF.Cos(angle) * radius,
                center.Y + MathF.Sin(angle) * radius,
                imageBounds,
                uv,
                color);
        }

        var indexStart = _indices.Count;
        for (var index = 0; index < segments; index++)
        {
            _indices.Add(firstVertex);
            _indices.Add(firstVertex + (uint)index + 1);
            _indices.Add(firstVertex + (uint)index + 2);
        }
        AddBatch(image.Texture, TextureSamplerState.LinearClamp, clip, indexStart, segments * 3);
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
        if (texture is null && uv is null)
        {
            AddNineSlice(
                bounds,
                color,
                _primitiveAtlas,
                new Vector4(0.0f, 0.0f, 32.0f / 96.0f, 32.0f / 48.0f),
                32.0f,
                8.0f,
                radius,
                clip);
            return;
        }
        AddRoundedGeometry(bounds, color, texture, uv ?? new Vector4(0, 0, 1, 1), clip, radius, sampler);
    }

    private void AddNineSlice(
        Rect bounds,
        Vector4 color,
        RenderTexture texture,
        Vector4 uv,
        float sourceSize,
        float sourceBorder,
        float destinationBorder,
        Rect clip)
    {
        if (bounds.Width <= 0.0f || bounds.Height <= 0.0f)
            return;
        var borderX = Math.Min(Math.Max(0.0f, destinationBorder), bounds.Width * 0.5f);
        var borderY = Math.Min(Math.Max(0.0f, destinationBorder), bounds.Height * 0.5f);
        Span<float> xs = [bounds.Left, bounds.Left + borderX, bounds.Right - borderX, bounds.Right];
        Span<float> ys = [bounds.Top, bounds.Top + borderY, bounds.Bottom - borderY, bounds.Bottom];
        var sourceRatio = sourceBorder / Math.Max(1.0f, sourceSize);
        var uBorder = (uv.Z - uv.X) * sourceRatio;
        var vBorder = (uv.W - uv.Y) * sourceRatio;
        Span<float> us = [uv.X, uv.X + uBorder, uv.Z - uBorder, uv.Z];
        Span<float> vs = [uv.Y, uv.Y + vBorder, uv.W - vBorder, uv.W];

        var firstVertex = (uint)(_vertices.Count / VertexStride);
        for (var y = 0; y < 4; y++)
        for (var x = 0; x < 4; x++)
            AddVertex(xs[x], ys[y], us[x], vs[y], color);

        var indexStart = _indices.Count;
        for (var y = 0; y < 3; y++)
        for (var x = 0; x < 3; x++)
        {
            var topLeft = firstVertex + (uint)(y * 4 + x);
            var bottomLeft = topLeft + 4;
            _indices.Add(topLeft);
            _indices.Add(bottomLeft);
            _indices.Add(bottomLeft + 1);
            _indices.Add(bottomLeft + 1);
            _indices.Add(topLeft + 1);
            _indices.Add(topLeft);
        }
        AddBatch(texture, TextureSamplerState.LinearClamp, clip, indexStart, 54);
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
        var compositeElement = FindCompositeElement(_paintElement);
        var textureSlot = ResolveTextureSlot(texture);
        for (var index = indexStart; index < indexStart + indexCount; index++)
        {
            var vertex = checked((int)_indices[index]);
            _vertices[vertex * VertexStride + 8] = textureSlot;
        }
        AddDrawBatch(texture, sampler, clip, indexStart, indexCount, compositeElement);
    }

    private void AddDrawBatch(
        RenderTexture? texture,
        TextureSamplerState? sampler,
        Rect clip,
        int indexStart,
        int indexCount,
        UiElement? compositeElement)
    {
        if (!_forceBatchBoundary &&
            _batches.Count > 0 &&
            _batches[^1].Clip == clip &&
            ReferenceEquals(_batches[^1].AxisClip, _axisClip) &&
            ReferenceEquals(_batches[^1].RoundedClip, _roundedClip) &&
            ReferenceEquals(_batches[^1].ScrollState, _scrollState) &&
            ReferenceEquals(_batches[^1].Element, compositeElement))
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
                compositeElement,
                _paintScale,
                indexStart,
                indexCount));
        }
        _forceBatchBoundary = false;
    }

    private int ResolveTextureSlot(RenderTexture? texture)
    {
        if (texture is null)
            return 0;
        if (_textureSlots.TryGetValue(texture, out var slot))
            return slot;
        slot = _geometryTextures.Count + 1;
        if (slot >= MaxTextureSlots)
            throw new InvalidOperationException(
                $"A UI document uses more than {MaxTextureSlots - 1} texture atlases. " +
                "Pack source images into configured .atlas assets.");
        _textureSlots.Add(texture, slot);
        _geometryTextures.Add(texture);
        return slot;
    }

    private void CompactDrawBatches()
    {
        if (_batches.Count < 2)
            return;

        var write = 0;
        for (var read = 1; read < _batches.Count; read++)
        {
            var previous = _batches[write];
            var current = _batches[read];
            if (previous.IndexStart + previous.IndexCount == current.IndexStart &&
                previous.Clip == current.Clip &&
                ReferenceEquals(previous.AxisClip, current.AxisClip) &&
                ReferenceEquals(previous.RoundedClip, current.RoundedClip) &&
                ReferenceEquals(previous.ScrollState, current.ScrollState) &&
                ReferenceEquals(previous.Element, current.Element))
            {
                _batches[write] = previous with
                {
                    IndexCount = previous.IndexCount + current.IndexCount
                };
                continue;
            }

            write++;
            if (write != read)
                _batches[write] = current;
        }

        if (write + 1 < _batches.Count)
            _batches.RemoveRange(write + 1, _batches.Count - write - 1);
    }

    private PaintCapture BeginPaintCapture()
    {
        _forceBatchBoundary = true;
        return new PaintCapture(
            _vertices.Count,
            _indices.Count,
            _batches.Count,
            _vertices.Count / VertexStride,
            _imageElements,
            _shadowLayers);
    }

    private bool TryAppendCached(PaintCacheEntry cache, int signature, Rect clip)
    {
        if (!cache.HasValue || cache.Signature != signature)
            return false;
        var baseVertex = (uint)(_vertices.Count / VertexStride);
        var indexStart = _indices.Count;
        for (var index = 0; index < cache.Vertices.Length; index++)
        {
            if (index % VertexStride != 8)
            {
                _vertices.Add(cache.Vertices[index]);
                continue;
            }
            var oldSlot = (int)(cache.Vertices[index] + 0.5f);
            _vertices.Add(oldSlot <= 0
                ? 0.0f
                : ResolveTextureSlot(cache.Textures[oldSlot - 1]));
        }
        foreach (var index in cache.Indices)
            _indices.Add(baseVertex + index);
        _forceBatchBoundary = true;
        var compositeElement = FindCompositeElement(_paintElement);
        foreach (var batch in cache.Batches)
            AddDrawBatch(
                batch.Texture,
                batch.Sampler,
                clip,
                indexStart + batch.IndexStart,
                batch.IndexCount,
                compositeElement);
        _imageElements += cache.ImageElements;
        _shadowLayers += cache.ShadowLayers;
        _forceBatchBoundary = false;
        return true;
    }

    private void EndPaintCapture(
        PaintCacheEntry cache,
        int signature,
        PaintCapture capture)
    {
        cache.HasValue = true;
        cache.Signature = signature;
        cache.ImageElements = _imageElements - capture.ImageElementStart;
        cache.ShadowLayers = _shadowLayers - capture.ShadowLayerStart;
        cache.Textures = _geometryTextures.ToArray();
        cache.Vertices = CollectionsMarshal.AsSpan(_vertices)[capture.VertexStart..].ToArray();
        var sourceIndices = CollectionsMarshal.AsSpan(_indices)[capture.IndexStart..];
        cache.Indices = new uint[sourceIndices.Length];
        for (var index = 0; index < sourceIndices.Length; index++)
            cache.Indices[index] = sourceIndices[index] - (uint)capture.BaseVertex;
        var batchCount = _batches.Count - capture.BatchStart;
        cache.Batches = new CachedPaintBatch[batchCount];
        for (var index = 0; index < batchCount; index++)
        {
            var batch = _batches[capture.BatchStart + index];
            cache.Batches[index] = new CachedPaintBatch(
                batch.Texture,
                batch.Sampler,
                batch.IndexStart - capture.IndexStart,
                batch.IndexCount);
        }
        _forceBatchBoundary = false;
    }

    private static UiElement? FindCompositeElement(UiElement? element)
    {
        for (var current = element; current is not null; current = current.Parent)
        {
            if (current.RenderOpacity != 1.0f ||
                current.RenderTransform != UiTransform.Identity ||
                current.AnimationRuntime.IsActive ||
                current.ComputedStyle.Animation != UiAnimationDefinition.None ||
                current.ComputedStyle.Transitions.Count > 0)
                return current;
        }
        return null;
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
        _vertices.Add(0.0f);
    }

    private unsafe void UploadGeometry(LayerCache layer)
    {
        var gl = _device.GL;
        EnsureGeometryResources(layer);
        gl.BindVertexArray(layer.VertexArray);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, layer.VertexBuffer);
        var vertexSpan = CollectionsMarshal.AsSpan(_vertices);
        var previousVertices = layer.VertexData.AsSpan(0, layer.VertexDataLength);
        var requiredVertexBytes = checked((nuint)(_vertices.Count * sizeof(float)));
        var vertexReallocated = EnsureBufferCapacity(
            gl,
            BufferTargetARB.ArrayBuffer,
            requiredVertexBytes,
            ref layer.VertexBufferCapacity);
        var vertexStart = vertexReallocated ? 0 : FirstDifference(previousVertices, vertexSpan);
        var vertexEnd = vertexStart < 0
            ? -1
            : vertexReallocated
                ? vertexSpan.Length
                : previousVertices.Length != vertexSpan.Length
                    ? vertexSpan.Length
                    : LastDifference(previousVertices, vertexSpan, vertexStart);
        var uploadedBytes = 0L;
        if (vertexStart >= 0 && vertexEnd > vertexStart)
        {
            fixed (float* vertices = vertexSpan)
                gl.BufferSubData(
                    BufferTargetARB.ArrayBuffer,
                    checked((nint)(vertexStart * sizeof(float))),
                    checked((nuint)((vertexEnd - vertexStart) * sizeof(float))),
                    vertices + vertexStart);
            uploadedBytes += checked((long)(vertexEnd - vertexStart) * sizeof(float));
        }
        EnsureCpuCapacity(ref layer.VertexData, vertexSpan.Length);
        vertexSpan.CopyTo(layer.VertexData);
        layer.VertexDataLength = vertexSpan.Length;

        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, layer.IndexBuffer);
        var indexSpan = CollectionsMarshal.AsSpan(_indices);
        var previousIndices = layer.IndexData.AsSpan(0, layer.IndexDataLength);
        var requiredIndexBytes = checked((nuint)(_indices.Count * sizeof(uint)));
        var indexReallocated = EnsureBufferCapacity(
            gl,
            BufferTargetARB.ElementArrayBuffer,
            requiredIndexBytes,
            ref layer.IndexBufferCapacity);
        var indexStart = indexReallocated ? 0 : FirstDifference(previousIndices, indexSpan);
        var indexEnd = indexStart < 0
            ? -1
            : indexReallocated
                ? indexSpan.Length
                : previousIndices.Length != indexSpan.Length
                    ? indexSpan.Length
                    : LastDifference(previousIndices, indexSpan, indexStart);
        if (indexStart >= 0 && indexEnd > indexStart)
        {
            fixed (uint* indices = indexSpan)
                gl.BufferSubData(
                    BufferTargetARB.ElementArrayBuffer,
                    checked((nint)(indexStart * sizeof(uint))),
                    checked((nuint)((indexEnd - indexStart) * sizeof(uint))),
                    indices + indexStart);
            uploadedBytes += checked((long)(indexEnd - indexStart) * sizeof(uint));
        }
        EnsureCpuCapacity(ref layer.IndexData, indexSpan.Length);
        indexSpan.CopyTo(layer.IndexData);
        layer.IndexDataLength = indexSpan.Length;
        layer.LastUploadBytes = uploadedBytes;
        layer.IndexCount = _indices.Count;
    }

    private static int FirstDifference<T>(ReadOnlySpan<T> previous, ReadOnlySpan<T> current)
        where T : IEquatable<T>
    {
        var shared = Math.Min(previous.Length, current.Length);
        for (var index = 0; index < shared; index++)
            if (!previous[index].Equals(current[index]))
                return index;
        return previous.Length == current.Length ? -1 : shared;
    }

    private static int LastDifference<T>(ReadOnlySpan<T> previous, ReadOnlySpan<T> current, int first)
        where T : IEquatable<T>
    {
        var previousIndex = previous.Length - 1;
        var currentIndex = current.Length - 1;
        while (previousIndex >= first && currentIndex >= first &&
               previous[previousIndex].Equals(current[currentIndex]))
        {
            previousIndex--;
            currentIndex--;
        }
        return currentIndex + 1;
    }

    private static void EnsureCpuCapacity<T>(ref T[] values, int required)
    {
        if (values.Length >= required)
            return;
        var capacity = Math.Max(16, values.Length);
        while (capacity < required)
            capacity *= 2;
        values = new T[capacity];
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
        for (var slot = 0; slot < MaxTextureSlots; slot++)
            gl.Uniform1(_textureUniforms[slot], slot);
        gl.ActiveTexture(TextureUnit.Texture0);
        gl.BindTexture(TextureTarget.Texture2D, _whiteTexture);
        for (var index = 0; index < layer.Textures.Count; index++)
            layer.Textures[index].Bind((uint)(index + 1), TextureSamplerState.LinearClamp);
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
            var transform = ResolveRenderTransform(batch.Element, batch.Scale);
            gl.Uniform4(
                _transformAUniform,
                transform.M11,
                transform.M21,
                transform.M31,
                transform.M12);
            gl.Uniform2(_transformBUniform, transform.M22, transform.M32);
            gl.Uniform1(_opacityUniform, ResolveRenderOpacity(batch.Element));
            var resolvedClip = ResolveAxisClip(batch.AxisClip, width, height);
            var x = Math.Clamp((int)MathF.Floor(resolvedClip.X), 0, width);
            var y = Math.Clamp((int)MathF.Floor(height - resolvedClip.Bottom), 0, height);
            var right = Math.Clamp((int)MathF.Ceiling(resolvedClip.Right), 0, width);
            var top = Math.Clamp((int)MathF.Ceiling(height - resolvedClip.Top), 0, height);
            gl.Scissor(x, y, (uint)Math.Max(0, right - x), (uint)Math.Max(0, top - y));

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
        gl.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, stride, (void*)(8 * sizeof(float)));
        gl.EnableVertexAttribArray(3);
        gl.BindVertexArray(0);
    }

    private static Vector2 ResolveScrollTranslation(UiScrollState? state)
    {
        var translation = Vector2.Zero;
        for (; state is not null; state = state.Parent)
            translation -= state.Element.ScrollOffset * state.Scale;
        return translation;
    }

    private static Matrix3x2 ResolveRenderTransform(UiElement? element, float scale)
    {
        var result = Matrix3x2.Identity;
        for (var current = element; current is not null; current = current.Parent)
        {
            var transform = current.RenderTransform with
            {
                Translation = current.RenderTransform.Translation * scale
            };
            result *= transform.ToMatrix(Scale(current.Bounds, scale));
        }
        return result;
    }

    private static float ResolveRenderOpacity(UiElement? element)
    {
        var opacity = 1.0f;
        for (var current = element; current is not null; current = current.Parent)
            opacity *= current.RenderOpacity;
        return Math.Clamp(opacity, 0.0f, 1.0f);
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
            var bounds = TransformBounds(
                state.Bounds,
                ResolveRenderTransform(state.Element, state.Scale));
            var translation = ResolveScrollTranslation(state.ScrollState);
            bounds = bounds with { X = bounds.X + translation.X, Y = bounds.Y + translation.Y };
            clip = ClipAxes(clip, bounds, state.ClipX, state.ClipY);
        }
        return clip;
    }

    private static void EnsureLayerCache(LayerCache layer, int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (layer.Width == width && layer.Height == height)
            return;

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
        for (var slot = 0; slot < MaxTextureSlots; slot++)
            gl.Uniform1(_textureUniforms[slot], slot);
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
        var roundedClipBatches = 0;
        foreach (var batch in _batches)
        {
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
        // Documents render directly and no longer own a full-resolution RGBA layer.
        statistics.LayerBytes = 0;
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

    private static unsafe bool EnsureBufferCapacity(
        GL gl,
        BufferTargetARB target,
        nuint required,
        ref nuint capacity)
    {
        if (required <= capacity)
            return false;
        capacity = 4096;
        while (capacity < required)
            capacity *= 2;
        gl.BufferData(target, capacity, null, BufferUsageARB.DynamicDraw);
        return true;
    }

    private unsafe void EnsureResources()
    {
        if (_program != 0)
            return;

        var gl = _device.GL;
        _program = BuildProgram(gl);
        _viewportUniform = gl.GetUniformLocation(_program, "uViewport");
        for (var slot = 0; slot < MaxTextureSlots; slot++)
            _textureUniforms[slot] = gl.GetUniformLocation(_program, $"uTextures[{slot}]");
        _translationUniform = gl.GetUniformLocation(_program, "uTranslation");
        _transformAUniform = gl.GetUniformLocation(_program, "uTransformA");
        _transformBUniform = gl.GetUniformLocation(_program, "uTransformB");
        _opacityUniform = gl.GetUniformLocation(_program, "uOpacity");
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
        gl.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, stride, (void*)(8 * sizeof(float)));
        gl.EnableVertexAttribArray(3);
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
            layout(location = 3) in float aTextureSlot;
            uniform vec2 uViewport;
            uniform vec2 uTranslation;
            uniform vec4 uTransformA;
            uniform vec2 uTransformB;
            uniform float uOpacity;
            out vec2 vUv;
            out vec4 vColor;
            flat out int vTextureSlot;
            void main() {
                vec2 position = vec2(
                    aPosition.x * uTransformA.x + aPosition.y * uTransformA.y + uTransformA.z,
                    aPosition.x * uTransformA.w + aPosition.y * uTransformB.x + uTransformB.y);
                position += uTranslation;
                vec2 ndc = vec2(position.x / uViewport.x * 2.0 - 1.0,
                                1.0 - position.y / uViewport.y * 2.0);
                gl_Position = vec4(ndc, 0.0, 1.0);
                vUv = aUv;
                vColor = vec4(aColor.rgb, aColor.a * uOpacity);
                vTextureSlot = int(aTextureSlot + 0.5);
            }
            """;
        var fragmentSource = """
            #version 330 core
            in vec2 vUv;
            in vec4 vColor;
            flat in int vTextureSlot;
            uniform sampler2D uTextures[16];
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
                vec4 sampled;
                switch (vTextureSlot) {
                    case 1: sampled = texture(uTextures[1], vUv); break;
                    case 2: sampled = texture(uTextures[2], vUv); break;
                    case 3: sampled = texture(uTextures[3], vUv); break;
                    case 4: sampled = texture(uTextures[4], vUv); break;
                    case 5: sampled = texture(uTextures[5], vUv); break;
                    case 6: sampled = texture(uTextures[6], vUv); break;
                    case 7: sampled = texture(uTextures[7], vUv); break;
                    case 8: sampled = texture(uTextures[8], vUv); break;
                    case 9: sampled = texture(uTextures[9], vUv); break;
                    case 10: sampled = texture(uTextures[10], vUv); break;
                    case 11: sampled = texture(uTextures[11], vUv); break;
                    case 12: sampled = texture(uTextures[12], vUv); break;
                    case 13: sampled = texture(uTextures[13], vUv); break;
                    case 14: sampled = texture(uTextures[14], vUv); break;
                    case 15: sampled = texture(uTextures[15], vUv); break;
                    default: sampled = texture(uTextures[0], vUv); break;
                }
                oColor = sampled * vColor;
            }
            """;
#if ANDROID
        vertexSource = vertexSource.Replace(
            "#version 330 core",
            "#version 300 es\nprecision highp float;\nprecision highp int;",
            StringComparison.Ordinal);
        fragmentSource = fragmentSource.Replace(
            "#version 330 core",
            "#version 300 es\nprecision highp float;\nprecision highp int;",
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
            var renderTransform = ResolveRenderTransform(item.Element, item.Scale);
            if (!Matrix3x2.Invert(renderTransform, out var inverseRenderTransform))
                inverseRenderTransform = Matrix3x2.Identity;
            var inverseTransform = Matrix3x2.CreateTranslation(-translation) * inverseRenderTransform;
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
        UiElement? Element,
        float Scale,
        int IndexStart,
        int IndexCount);

    private readonly record struct PaintCapture(
        int VertexStart,
        int IndexStart,
        int BatchStart,
        int BaseVertex,
        int ImageElementStart,
        int ShadowLayerStart);

    private readonly record struct CachedPaintBatch(
        RenderTexture? Texture,
        TextureSamplerState? Sampler,
        int IndexStart,
        int IndexCount);

    private sealed class PaintCacheEntry
    {
        public bool HasValue;
        public int Signature;
        public float[] Vertices = [];
        public uint[] Indices = [];
        public CachedPaintBatch[] Batches = [];
        public RenderTexture[] Textures = [];
        public int ImageElements;
        public int ShadowLayers;
    }

    private sealed class ElementPaintCache
    {
        public PaintCacheEntry Background { get; } = new();
        public PaintCacheEntry Text { get; } = new();
    }

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
        public List<RenderTexture> Textures { get; } = [];
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
        public float[] VertexData = [];
        public uint[] IndexData = [];
        public int VertexDataLength;
        public int IndexDataLength;

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
            Textures.Clear();
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
            VertexData = [];
            IndexData = [];
            VertexDataLength = IndexDataLength = 0;
        }
    }

    private sealed record UiClipState(
        UiClipState? Parent,
        Rect Bounds,
        float Radius,
        UiElement Element,
        float Scale,
        UiScrollState? ScrollState);

    private sealed record UiAxisClipState(
        UiAxisClipState? Parent,
        Rect Bounds,
        bool ClipX,
        bool ClipY,
        UiElement Element,
        float Scale,
        UiScrollState? ScrollState);

    private sealed record UiScrollState(
        UiScrollState? Parent,
        UiElement Element,
        float Scale);
}
