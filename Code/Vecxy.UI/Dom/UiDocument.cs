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
    private readonly List<UiStyleSheet> _styleSheets = [];
    private readonly Dictionary<string, AssetRef<TextureAsset>> _imageAssets =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, AssetRef<UiSpriteAtlasAsset>> _spriteAtlases =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, AssetRef<UiFontAsset>> _fontAssets =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, UiKeyframes> _keyframes =
        new(StringComparer.Ordinal);
    private int _sourceVersion;
    private int[] _styleVersions = [];
    private UiConfig _settings = new();
    private bool _disposed;

    public string Path => _source.Metadata.Path;
    public UiElement Root { get; private set; } = null!;
    public bool IsVisible { get; set; } = true;
    public event Action<UiDocument>? Reloaded;
    internal float LayoutScale { get; private set; } = 1.0f;

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
            return Root.DescendantsAndSelf().FirstOrDefault(element => element.Id == selector[1..]);
        if (selector[0] == '.')
            return Root.DescendantsAndSelf().FirstOrDefault(element => element.Classes.Contains(selector[1..]));
        return Root.DescendantsAndSelf().FirstOrDefault(element =>
            string.Equals(element.TagName, selector, StringComparison.OrdinalIgnoreCase));
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
            ReloadStyles();

        UiStyleResolver.Resolve(Root, _styleSheets);
        ResolveFonts();
        ResolveIntrinsicImages();
    }

    internal void Layout(int width, int height, UiConfig? settings = null)
    {
        _settings = settings ?? new UiConfig();
        Refresh();
        var canvas = UiCanvas.Resolve(Root, width, height, _settings);
        LayoutScale = canvas.Scale;
        UiLayout.Calculate(Root, canvas.Width, canvas.Height);
    }

    internal void UpdateAnimations(
        float deltaTime,
        int width,
        int height,
        UiConfig settings)
    {
        _settings = settings;
        Refresh();
        var canvas = UiCanvas.Resolve(Root, width, height, settings);
        // Animation callbacks are allowed to remove their element from the DOM.
        foreach (var element in Root.DescendantsAndSelf().ToArray())
        {
            element.AnimationRuntime.Update(
                element,
                _keyframes,
                deltaTime,
                canvas.Width,
                canvas.Height);
        }
    }

    internal Vector2 ToLayoutPoint(Vector2 outputPoint) =>
        outputPoint / Math.Max(0.0001f, LayoutScale);

    internal UiResolvedImage? ResolveImage(UiElement element)
    {
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
        var textureAsset = atlas.Value.Texture;
        var width = Math.Max(1, textureAsset.Value.Width);
        var height = Math.Max(1, textureAsset.Value.Height);
        return new UiResolvedImage(
            _textures.Resolve(textureAsset),
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

    private UiElement ParseElement(XElement source)
    {
        var attributes = source.Attributes().ToDictionary(
            attribute => attribute.Name.LocalName,
            attribute => attribute.Value,
            StringComparer.OrdinalIgnoreCase);
        var tagName = source.Name.LocalName.ToLowerInvariant();
        var directText = string.Concat(source.Nodes().OfType<XText>().Select(text => text.Value)).Trim();
        var element = new UiElement(
            _yogaConfig,
            tagName,
            attributes,
            tagName == "text" ? directText : null);

        foreach (var child in source.Elements())
            element.Add(ParseElement(child));

        if (tagName != "text" && directText.Length > 0)
        {
            element.Add(new UiElement(
                _yogaConfig,
                "text",
                new Dictionary<string, string>(),
                directText));
        }

        return element;
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
        UiStyleResolver.Resolve(Root, _styleSheets);
        ResolveFonts();
        ResolveIntrinsicImages();
    }

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
        foreach (var image in _imageAssets.Values)
            image.Dispose();
        foreach (var atlas in _spriteAtlases.Values)
            atlas.Dispose();
        foreach (var font in _fontAssets.Values)
            font.Dispose();
        _styleAssets.Clear();
        _imageAssets.Clear();
        _spriteAtlases.Clear();
        _fontAssets.Clear();
        _source.Dispose();
    }
}

internal readonly record struct UiResolvedImage(
    Texture Texture,
    Vector4 Uv,
    Vector2 Size);
