using System.Numerics;
using System.Xml.Linq;
using Facebook.Yoga;
using Vecxy.Assets;
using Vecxy.Diagnostics;
using Vecxy.Rendering;

namespace Vecxy.UI;

public sealed class UiDocument : IDisposable
{
    private readonly IAssetsManager _assets;
    private readonly ITextureResolver _textures;
    private readonly Config _yogaConfig;
    private readonly AssetRef<UiDocumentAsset> _source;
    private readonly List<AssetRef<UiStyleSheetAsset>> _styleAssets = [];
    private readonly Dictionary<string, AssetRef<UiDocumentAsset>> _componentAssets =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, ComponentTemplate> _componentTemplates =
        new(StringComparer.Ordinal);
    private readonly List<UiStyleSheet> _styleSheets = [];
    private readonly Dictionary<string, AssetRef<TextureAsset>> _imageAssets =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, AssetRef<UiSpriteAtlasAsset>> _spriteAtlases =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, AssetRef<UiFontAsset>> _fontAssets =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, UiKeyframes> _keyframes =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, UiElement> _idCache =
        new(StringComparer.Ordinal);
    private readonly List<UiElement> _activeAnimationElements = [];
    private int _sourceVersion;
    private int[] _styleVersions = [];
    private UiConfig _settings = new();
    private bool _resourceResolutionPending;
    private int _resolvedTreeVersion = int.MinValue;
    private int _resolvedPseudoState = int.MinValue;
    private int _styleResolutionVersion;
    private int _lastLayoutVersion = int.MinValue;
    private int _lastCanvasWidth;
    private int _lastCanvasHeight;
    private bool _layoutValid;
    private bool _animationSyncPending = true;
    private long _stylePasses;
    private long _layoutPasses;
    private long _animationTreeScans;
    private double _frameStyleMilliseconds;
    private int _frameStyledElements;
    private bool _disposed;

    public string Path => _source.Metadata.Path;
    public UiElement Root { get; private set; } = null!;
    public bool IsVisible { get; set; } = true;
    public event Action<UiDocument>? Reloaded;
    internal float LayoutScale { get; private set; } = 1.0f;
    internal int ActiveAnimationCount => _activeAnimationElements.Count;
    internal int StyleVersion => Root.StyleVersion;
    internal int LayoutVersion => Root.LayoutVersion;
    internal int VisualVersion => Root.VisualVersion;
    internal int HitTestVersion => HashCode.Combine(
        Root.LayoutVersion,
        Root.ScrollVersion,
        Root.HitTestVersion);
    internal long StylePasses => _stylePasses;
    internal long LayoutPasses => _layoutPasses;
    internal long AnimationTreeScans => _animationTreeScans;
    internal bool HasCurrentLayout =>
        _layoutValid &&
        _lastLayoutVersion == Root.LayoutVersion &&
        !_resourceResolutionPending;

    internal UiDocument(
        IAssetsManager assets,
        ITextureResolver textures,
        Config yogaConfig,
        AssetRef<UiDocumentAsset> source)
    {
        _assets = assets;
        _textures = textures;
        _yogaConfig = yogaConfig;
        _source = source;
        ReloadDocument();
    }

    public UiElement? Query(string selector)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);

        if (selector[0] == '#')
        {
            var id = selector[1..];
            if (_idCache.TryGetValue(id, out var cached) && IsAttached(cached))
                return cached;
            var found = Root.DescendantsAndSelf().FirstOrDefault(element => element.Id == id);
            if (found is not null)
                _idCache[id] = found;
            else
                _idCache.Remove(id);
            return found;
        }
        if (selector[0] == '.')
            return Root.DescendantsAndSelf().FirstOrDefault(element => element.Classes.Contains(selector[1..]));
        return Root.DescendantsAndSelf().FirstOrDefault(element =>
            string.Equals(element.TagName, selector, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<UiElement> QueryAll(string selector)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);
        return Root.DescendantsAndSelf()
            .Where(element => MatchesSimpleSelector(element, selector))
            .ToArray();
    }

    public IReadOnlyList<T> QueryAll<T>(string selector) where T : UiElement =>
        QueryAll(selector).OfType<T>().ToArray();

    public T? Query<T>(string selector) where T : UiElement => Query(selector) as T;

    public T GetElementById<T>(string id) where T : UiElement
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return Query<T>($"#{id}") ?? throw new InvalidDataException(
            $"Required {typeof(T).Name} with id '{id}' is missing from {Path}.");
    }

    public UiElement Instantiate(
        string componentPath,
        UiElement parent,
        IReadOnlyDictionary<string, string>? parameters = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(componentPath);
        ArgumentNullException.ThrowIfNull(parent);

        var path = ResolveRelativePath(Path, componentPath);
        if (!_componentAssets.TryGetValue(path, out var component))
        {
            component = _assets.Load<UiDocumentAsset>(path);
            _componentAssets.Add(path, component);
        }
        if (!_componentTemplates.TryGetValue(path, out var template) ||
            template.Version != component.Version)
        {
            var parsed = XDocument.Parse(
                component.Value.Source,
                LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
            var sourceRoot = parsed.Root ?? throw new InvalidDataException(
                $"UI component has no root element: {path}");
            template = new ComponentTemplate(component.Version, sourceRoot);
            _componentTemplates[path] = template;
        }
        var instance = ParseElement(template.Root, parameters);
        parent.Add(instance);
        // Resolution is intentionally deferred until the next UI update. Building a
        // component tree must result in one style/layout pass, not one pass per instance.
        _resourceResolutionPending = true;
        return instance;
    }

    public T Instantiate<T>(
        string componentPath,
        UiElement parent,
        IReadOnlyDictionary<string, string>? parameters = null) where T : UiElement =>
        Instantiate(componentPath, parent, parameters) as T ??
        throw new InvalidDataException($"Component root is not a {typeof(T).Name}: {componentPath}");

    public UiElement CreateElement(
        string tagName,
        IReadOnlyDictionary<string, string>? attributes = null,
        string? text = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(tagName);
        _resourceResolutionPending = true;
        return CreateTypedElement(tagName, attributes ?? new Dictionary<string, string>(), text);
    }

    public UiPanel CreatePanel(IReadOnlyDictionary<string, string>? attributes = null) =>
        (UiPanel)CreateElement("panel", attributes);

    public UiText CreateText(string text = "", IReadOnlyDictionary<string, string>? attributes = null) =>
        (UiText)CreateElement("text", attributes, text);

    public UiButton CreateButton(string label = "", IReadOnlyDictionary<string, string>? attributes = null)
    {
        var button = (UiButton)CreateElement("button", attributes);
        if (label.Length > 0)
            button.Add(CreateText(label));
        return button;
    }

    public UiImage CreateImage(string source, IReadOnlyDictionary<string, string>? attributes = null)
    {
        var values = new Dictionary<string, string>(attributes ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase)
        {
            ["src"] = source
        };
        return (UiImage)CreateElement("image", values);
    }

    internal void Refresh()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_source.Version != _sourceVersion)
        {
            ReloadDocument();
            return;
        }

        var stylesChanged = _styleAssets.Count != _styleVersions.Length;
        for (var index = 0; !stylesChanged && index < _styleAssets.Count; index++)
            stylesChanged = _styleAssets[index].Version != _styleVersions[index];

        if (stylesChanged)
        {
            ReloadStyles();
            return;
        }

        var (treeVersion, pseudoState) = ComputeStyleState();
        var treeChanged = treeVersion != _resolvedTreeVersion;
        var pseudoChanged = pseudoState != _resolvedPseudoState;
        if (treeChanged || pseudoChanged)
        {
            var styleStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            _frameStyledElements += UiStyleResolver.Resolve(Root, _styleSheets);
            _frameStyleMilliseconds += System.Diagnostics.Stopwatch
                .GetElapsedTime(styleStarted).TotalMilliseconds;
            _stylePasses++;
            unchecked { _styleResolutionVersion++; }
            // Mutations already carry their precise layout dirty bit. Pseudo
            // selectors remain conservative because they may target layout fields.
            if (pseudoChanged)
                _layoutValid = false;
            _animationSyncPending = true;
        }
        if (treeChanged || _resourceResolutionPending)
        {
            ResolveFonts();
            ResolveIntrinsicImages();
            _resourceResolutionPending = false;
        }
        _resolvedTreeVersion = treeVersion;
        _resolvedPseudoState = pseudoState;
    }

    internal UiDocumentUpdateMetrics Layout(int width, int height, UiConfig? settings = null)
    {
        _frameStyleMilliseconds = 0.0;
        _frameStyledElements = 0;
        _settings = settings ?? new UiConfig();
        var refreshStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        Refresh();
        var refreshMilliseconds = System.Diagnostics.Stopwatch
            .GetElapsedTime(refreshStarted).TotalMilliseconds;
        var canvas = UiCanvas.Resolve(Root, width, height, _settings);
        LayoutScale = canvas.Scale;
        if (_layoutValid &&
            _lastLayoutVersion == Root.LayoutVersion &&
            _lastCanvasWidth == canvas.Width &&
            _lastCanvasHeight == canvas.Height)
            return new UiDocumentUpdateMetrics(
                refreshMilliseconds,
                _frameStyleMilliseconds,
                _frameStyledElements,
                false,
                default);
        var layout = UiLayout.Calculate(Root, canvas.Width, canvas.Height, _settings.EnableShadows);
        _layoutPasses++;
        _lastLayoutVersion = Root.LayoutVersion;
        _lastCanvasWidth = canvas.Width;
        _lastCanvasHeight = canvas.Height;
        _layoutValid = true;
        _animationSyncPending = true;
        return new UiDocumentUpdateMetrics(
            refreshMilliseconds,
            _frameStyleMilliseconds,
            _frameStyledElements,
            true,
            layout);
    }

    internal void UpdateAnimations(
        float deltaTime,
        int width,
        int height,
        UiConfig settings)
    {
        _settings = settings;
        var canvas = UiCanvas.Resolve(Root, width, height, settings);
        if (_animationSyncPending)
        {
            _animationTreeScans++;
            _activeAnimationElements.Clear();
            // Animation callbacks may remove their element, so only the occasional
            // full synchronization takes a stable snapshot of the tree.
            foreach (var element in Root.DescendantsAndSelf().ToArray())
            {
                // Hidden retained surfaces still need one synchronization pass.
                // Otherwise their transition runtime is initialized only after
                // they become visible and the very first opacity transition
                // jumps straight to its target value.
                if (!element.IsRendered &&
                    !element.AnimationRuntime.IsActive &&
                    element.ComputedStyle.Transitions.Count == 0 &&
                    element.ComputedStyle.Animation == UiAnimationDefinition.None)
                    continue;
                UpdateAnimationElement(element, deltaTime, canvas.Width, canvas.Height);
                if (element.AnimationRuntime.IsActive && IsAttached(element))
                    _activeAnimationElements.Add(element);
            }
            _animationSyncPending = false;
            return;
        }

        for (var index = _activeAnimationElements.Count - 1; index >= 0; index--)
        {
            var element = _activeAnimationElements[index];
            if (!IsAttached(element))
            {
                _activeAnimationElements.RemoveAt(index);
                continue;
            }
            UpdateAnimationElement(element, deltaTime, canvas.Width, canvas.Height);
            if (!element.AnimationRuntime.IsActive || !IsAttached(element))
                _activeAnimationElements.RemoveAt(index);
        }
    }

    /// <summary>
    /// Restarts an element's resolved CSS animation without changing its classes or
    /// forcing a synchronization scan of the document tree.
    /// </summary>
    public void RestartAnimation(UiElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (!IsAttached(element))
            throw new InvalidOperationException("The UI element does not belong to this document.");

        element.AnimationRuntime.Restart(element);
        if (element.AnimationRuntime.IsActive && !_activeAnimationElements.Contains(element))
            _activeAnimationElements.Add(element);
        element.InvalidateComposite();
    }

    private void UpdateAnimationElement(UiElement element, float deltaTime, float width, float height)
    {
        var previousTransform = element.RenderTransform;
        var changes = element.AnimationRuntime.Update(element, _keyframes, deltaTime, width, height);
        if ((changes & UiAnimationChange.Paint) != 0)
            element.InvalidateVisual();
        if ((changes & UiAnimationChange.Composite) != 0)
            element.InvalidateComposite();
        if (previousTransform != element.RenderTransform && element.HasInteractiveSubtree())
            element.InvalidateHitTest();
    }

    internal int GeometryVersion => HashCode.Combine(
        _sourceVersion,
        Root.LayoutVersion,
        Root.VisualVersion,
        IsVisible);

    internal int RenderVersion => HashCode.Combine(
        GeometryVersion,
        Root.ScrollVersion,
        Root.CompositeVersion);

    internal Vector2 ToLayoutPoint(Vector2 outputPoint) =>
        outputPoint / Math.Max(0.0001f, LayoutScale);

    internal UiResolvedImage? ResolveImage(UiElement element)
    {
        if (element is UiImage { Texture: { } runtimeTexture })
        {
            return new UiResolvedImage(
                runtimeTexture,
                new Vector4(0, 1, 1, 0),
                new Vector2(element.Bounds.Width, element.Bounds.Height));
        }

        if (element.Attributes.GetValueOrDefault("sprite") is { Length: > 0 } spriteSource)
        {
            var separator = spriteSource.LastIndexOf('#');
            if (separator <= 0 || separator == spriteSource.Length - 1)
                return null;
            return ResolveSprite(spriteSource[..separator], spriteSource[(separator + 1)..]);
        }

        if (TryParseSprite(element.ComputedStyle.BackgroundImage, out var atlasName, out var spriteName))
            return ResolveSprite(atlasName, spriteName);

        var source = element.Attributes.GetValueOrDefault("src");
        if (string.IsNullOrWhiteSpace(source))
            source = ParseUrl(element.ComputedStyle.BackgroundImage);
        if (string.IsNullOrWhiteSpace(source))
            return null;

        // Individual source files remain convenient for authors and configs, but
        // configured atlases are the runtime representation. Resolve by stable file
        // stem so existing content automatically benefits from atlas batching.
        var inferredSprite = System.IO.Path.GetFileNameWithoutExtension(source);
        if (!string.IsNullOrWhiteSpace(inferredSprite))
        {
            foreach (var configuredAtlas in _settings.SpriteAtlases.Keys)
                if (ResolveSprite(configuredAtlas, inferredSprite) is { } packed)
                    return packed;
        }

        var path = ResolveRelativePath(Path, source);
        var asset = GetImageAsset(path);
        if (asset is null)
            return null;

        return asset.HasError
            ? null
            : new UiResolvedImage(
                _textures.Resolve(asset),
                new Vector4(0, 0, 1, 1),
                new Vector2(asset.Value.Width, asset.Value.Height));
    }

    private UiResolvedImage? ResolveSprite(string atlasName, string spriteName)
    {
        var atlasSource = _settings.SpriteAtlases.GetValueOrDefault(atlasName) ?? atlasName;
        var atlasPath = ResolveRelativePath(Path, atlasSource);
        if (!_spriteAtlases.TryGetValue(atlasPath, out var atlas))
        {
            try
            {
                atlas = _assets.Load<UiSpriteAtlasAsset>(atlasPath);
                _spriteAtlases.Add(atlasPath, atlas);
            }
            catch (Exception exception)
            {
                Logger.Error(exception, $"Could not load UI sprite atlas: {atlasPath}");
                return null;
            }
        }

        if (atlas.HasError || !atlas.Value.Sprites.TryGetValue(spriteName, out var sprite))
            return null;
        var atlasValue = atlas.Value;
        var textureAsset = atlasValue.EmbeddedTexture ?? atlasValue.TextureReference?.Value;
        if (textureAsset is null)
            return null;
        var width = Math.Max(1, textureAsset.Width);
        var height = Math.Max(1, textureAsset.Height);
        return new UiResolvedImage(
            atlasValue.EmbeddedTexture is { } embedded
                ? _textures.Resolve(embedded)
                : _textures.Resolve(atlasValue.TextureReference!),
            new Vector4(
                sprite.X / (float)width,
                sprite.Y / (float)height,
                (sprite.X + sprite.Width) / (float)width,
                (sprite.Y + sprite.Height) / (float)height),
            new Vector2(sprite.Width, sprite.Height));
    }

    private static bool TryParseSprite(string? source, out string atlas, out string sprite)
    {
        atlas = string.Empty;
        sprite = string.Empty;
        if (string.IsNullOrWhiteSpace(source))
            return false;
        var match = System.Text.RegularExpressions.Regex.Match(
            source,
            "^sprite\\(\\s*['\\\"]?([^,'\\\")]+)['\\\"]?\\s*,\\s*['\\\"]?([^'\\\")]+)['\\\"]?\\s*\\)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success)
            return false;
        atlas = match.Groups[1].Value.Trim();
        sprite = match.Groups[2].Value.Trim();
        return atlas.Length > 0 && sprite.Length > 0;
    }

    private static string? ParseUrl(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return null;
        var match = System.Text.RegularExpressions.Regex.Match(
            source,
            "url\\(\\s*['\\\"]?([^'\\\"\\)]+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private AssetRef<TextureAsset>? GetImageAsset(string path)
    {
        if (_imageAssets.TryGetValue(path, out var asset))
            return asset;

        try
        {
            asset = _assets.Load<TextureAsset>(path);
            _imageAssets.Add(path, asset);
            return asset;
        }
        catch (Exception exception)
        {
            Logger.Error(exception, $"Could not load UI image: {path}");
            return null;
        }
    }

    internal Texture? ResolveFontTexture(UiElement element)
    {
        if (element.Font is not { } font)
            return null;
        if (font.EmbeddedTexture is { } embeddedTexture)
            return _textures.Resolve(embeddedTexture);
        return font.TextureReference is { HasError: false } textureReference
            ? _textures.Resolve(textureReference)
            : null;
    }

    private void ReloadDocument()
    {
        var wasLoaded = Root is not null;
        var parsed = XDocument.Parse(
            _source.Value.Source,
            LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
        var sourceRoot = parsed.Root ?? throw new InvalidDataException(
            $"UI document has no root element: {Path}");

        var replacement = ParseElement(sourceRoot);
        Root?.ReleaseLayout();
        Root = replacement;
        _idCache.Clear();
        _activeAnimationElements.Clear();
        _animationSyncPending = true;
        _layoutValid = false;
        _sourceVersion = _source.Version;

        foreach (var style in _styleAssets)
            style.Dispose();
        _styleAssets.Clear();

        var styleSources = sourceRoot.Attribute("styles")?.Value ??
                           sourceRoot.Attribute("stylesheet")?.Value ??
                           string.Empty;
        foreach (var styleSource in styleSources.Split(
                     [',', ';'],
                     StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var path = ResolveRelativePath(Path, styleSource);
            _styleAssets.Add(_assets.Load<UiStyleSheetAsset>(path));
        }

        ReloadStyles();
        if (wasLoaded)
            Reloaded?.Invoke(this);
    }

    private UiElement ParseElement(
        XElement source,
        IReadOnlyDictionary<string, string>? parameters = null)
    {
        var attributes = source.Attributes().ToDictionary(
            attribute => attribute.Name.LocalName,
            attribute => ApplyParameters(attribute.Value, parameters),
            StringComparer.OrdinalIgnoreCase);
        var tagName = source.Name.LocalName.ToLowerInvariant();
        var directText = ApplyParameters(
            string.Concat(source.Nodes().OfType<XText>().Select(text => text.Value)).Trim(),
            parameters);
        var element = CreateTypedElement(tagName, attributes, tagName == "text" ? directText : null);

        foreach (var child in source.Elements())
            element.Add(ParseElement(child, parameters));

        if (tagName != "text" && directText.Length > 0)
        {
            element.Add(CreateTypedElement("text", new Dictionary<string, string>(), directText));
        }

        return element;
    }

    private UiElement CreateTypedElement(
        string tagName,
        IReadOnlyDictionary<string, string> attributes,
        string? text = null) => tagName.ToLowerInvariant() switch
    {
        "panel" => new UiPanel(_yogaConfig, attributes, text),
        "text" => new UiText(_yogaConfig, attributes, text),
        "button" => new UiButton(_yogaConfig, attributes, text),
        "image" => new UiImage(_yogaConfig, attributes, text),
        "progress" => new UiProgress(_yogaConfig, attributes, text),
        "radial-progress" => new UiRadialProgress(_yogaConfig, attributes, text),
        _ => new UiElement(_yogaConfig, tagName, attributes, text)
    };

    private static bool MatchesSimpleSelector(UiElement element, string selector)
    {
        if (selector[0] == '#')
            return element.Id == selector[1..];
        if (selector[0] == '.')
            return element.Classes.Contains(selector[1..]);
        return string.Equals(element.TagName, selector, StringComparison.OrdinalIgnoreCase);
    }

    private static string ApplyParameters(
        string source,
        IReadOnlyDictionary<string, string>? parameters)
    {
        if (parameters is null)
            return source;
        foreach (var (name, value) in parameters)
            source = source.Replace(
                $"{{{{{name}}}}}",
                value,
                StringComparison.Ordinal);
        return source;
    }

    private void ReloadStyles()
    {
        _styleSheets.Clear();
        _keyframes.Clear();
        foreach (var font in _fontAssets.Values)
            font.Dispose();
        _fontAssets.Clear();

        foreach (var style in _styleAssets)
        {
            try
            {
                var parsed = UiStyleSheet.Parse(style.Value.Source);
                _styleSheets.Add(parsed);
                foreach (var (name, animation) in parsed.Keyframes)
                    _keyframes[name] = animation;
                foreach (var face in parsed.FontFaces)
                {
                    var path = ResolveRelativePath(style.Metadata.Path, face.Source);
                    if (!_fontAssets.ContainsKey(face.Family))
                        _fontAssets.Add(face.Family, _assets.Load<UiFontAsset>(path));
                }
            }
            catch (Exception exception)
            {
                Logger.Error(exception, $"Could not parse UI stylesheet: {style.Metadata.Path}");
            }
        }

        _styleVersions = _styleAssets.Select(style => style.Version).ToArray();
        var styleStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        _frameStyledElements += UiStyleResolver.Resolve(
            Root,
            _styleSheets,
            forceFullResolution: true);
        _frameStyleMilliseconds += System.Diagnostics.Stopwatch
            .GetElapsedTime(styleStarted).TotalMilliseconds;
        unchecked { _styleResolutionVersion++; }
        Root.InvalidateVisual();
        ResolveFonts();
        ResolveIntrinsicImages();
        _resourceResolutionPending = false;
        CaptureResolvedStyleState();
        _layoutValid = false;
        _animationSyncPending = true;
    }

    private bool IsAttached(UiElement element)
    {
        for (var current = element; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, Root))
                return true;
        }
        return false;
    }

    private void CaptureResolvedStyleState()
    {
        (_resolvedTreeVersion, _resolvedPseudoState) = ComputeStyleState();
    }

    private (int TreeVersion, int PseudoState) ComputeStyleState()
        => (Root.StyleVersion, Root.PseudoVersion);

    private void ResolveIntrinsicImages()
    {
        foreach (var element in Root.DescendantsAndSelf().Where(element => element.TagName == "image"))
        {
            if (ResolveImage(element) is { } resolved)
            {
                element.IntrinsicSize = resolved.Size;
                continue;
            }

            var textureSource = element.Attributes.GetValueOrDefault("src");
            if (string.IsNullOrWhiteSpace(textureSource))
                continue;
            var path = ResolveRelativePath(Path, textureSource);
            if (!_imageAssets.TryGetValue(path, out var image))
            {
                try
                {
                    image = _assets.Load<TextureAsset>(path);
                    _imageAssets.Add(path, image);
                }
                catch
                {
                    continue;
                }
            }

            if (!image.HasError)
                element.IntrinsicSize = new Vector2(image.Value.Width, image.Value.Height);
        }
    }

    private void ResolveFonts()
    {
        foreach (var element in Root.DescendantsAndSelf())
        {
            element.Font = _fontAssets.TryGetValue(element.ComputedStyle.FontFamily, out var font) &&
                           !font.HasError
                ? font.Value
                : null;
        }
    }

    private static string ResolveRelativePath(string ownerPath, string relativePath)
    {
        relativePath = relativePath.Replace('\\', '/');
        var fromAssetsRoot = relativePath.StartsWith('/') ||
                             relativePath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase);
        relativePath = relativePath.TrimStart('/');
        if (relativePath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            relativePath = relativePath[7..];
        if (fromAssetsRoot)
            return relativePath;
        var directory = System.IO.Path.GetDirectoryName(ownerPath)?.Replace('\\', '/') ?? string.Empty;
        var combined = System.IO.Path.Combine(directory, relativePath).Replace('\\', '/');
        var parts = new List<string>();
        foreach (var part in combined.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".")
                continue;
            if (part == ".." && parts.Count > 0)
                parts.RemoveAt(parts.Count - 1);
            else if (part != "..")
                parts.Add(part);
        }
        return string.Join('/', parts);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Root?.ReleaseLayout();
        foreach (var style in _styleAssets)
            style.Dispose();
        foreach (var component in _componentAssets.Values)
            component.Dispose();
        foreach (var image in _imageAssets.Values)
            image.Dispose();
        foreach (var atlas in _spriteAtlases.Values)
            atlas.Dispose();
        foreach (var font in _fontAssets.Values)
            font.Dispose();
        _styleAssets.Clear();
        _componentAssets.Clear();
        _componentTemplates.Clear();
        _imageAssets.Clear();
        _spriteAtlases.Clear();
        _fontAssets.Clear();
        _source.Dispose();
    }

    private sealed record ComponentTemplate(int Version, XElement Root);
}

internal readonly record struct UiDocumentUpdateMetrics(
    double RefreshMilliseconds,
    double StyleMilliseconds,
    int StyledElements,
    bool LayoutPerformed,
    UiLayoutMetrics Layout);

internal readonly record struct UiResolvedImage(
    Texture Texture,
    Vector4 Uv,
    Vector2 Size);
