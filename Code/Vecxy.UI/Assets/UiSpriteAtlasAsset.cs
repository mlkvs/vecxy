using System.Text.Json;
using Vecxy.Assets;

namespace Vecxy.UI;

public sealed class UiSpriteAtlasAsset : IDisposable
{
    internal AssetRef<TextureAsset>? TextureReference { get; }
    internal TextureAsset? EmbeddedTexture { get; }
    public IReadOnlyDictionary<string, UiSprite> Sprites { get; }

    internal UiSpriteAtlasAsset(
        AssetRef<TextureAsset> texture,
        IReadOnlyDictionary<string, UiSprite> sprites)
    {
        TextureReference = texture;
        Sprites = sprites;
    }

    internal UiSpriteAtlasAsset(
        TextureAsset texture,
        IReadOnlyDictionary<string, UiSprite> sprites)
    {
        EmbeddedTexture = texture;
        Sprites = sprites;
    }

    public void Dispose() => TextureReference?.Dispose();
}

public readonly record struct UiSprite(int X, int Y, int Width, int Height);

public sealed class UiSpriteAtlasAssetImporter : IAssetImporter<UiSpriteAtlasAsset>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public IReadOnlyCollection<string> Extensions { get; } = [".atlas"];

    public UiSpriteAtlasAsset Import(AssetMetadata metadata, AssetImportContext context)
    {
        var descriptor = JsonSerializer.Deserialize<Descriptor>(
            context.ReadAllText(metadata.Path),
            JsonOptions) ?? throw new InvalidDataException($"Sprite atlas is empty: {metadata.Path}");
        var directory = Path.GetDirectoryName(metadata.Path) ?? string.Empty;
        if (descriptor.Sources.Count > 0)
            return PackSources(metadata.Path, directory, descriptor, context);
        if (string.IsNullOrWhiteSpace(descriptor.Texture))
            throw new InvalidDataException($"Sprite atlas has no texture or sources: {metadata.Path}");
        var texturePath = Path.Combine(directory, descriptor.Texture).Replace('\\', '/');
        var texture = context.Load<TextureAsset>(texturePath);
        try
        {
            var sprites = descriptor.Sprites.ToDictionary(
                pair => pair.Key,
                pair => new UiSprite(
                    pair.Value.X,
                    pair.Value.Y,
                    pair.Value.Width,
                    pair.Value.Height),
                StringComparer.Ordinal);
            if (sprites.Values.Any(sprite => sprite.Width <= 0 || sprite.Height <= 0))
                throw new InvalidDataException($"Sprite atlas contains an empty sprite: {metadata.Path}");
            return new UiSpriteAtlasAsset(texture, sprites);
        }
        catch
        {
            texture.Dispose();
            throw;
        }
    }

    private static UiSpriteAtlasAsset PackSources(
        string atlasPath,
        string directory,
        Descriptor descriptor,
        AssetImportContext context)
    {
        var padding = Math.Clamp(descriptor.Padding, 1, 16);
        var width = Math.Clamp(descriptor.Width, 64, 4096);
        var loaded = new List<(string Name, AssetRef<TextureAsset> Asset)>();
        try
        {
            foreach (var (name, source) in descriptor.Sources)
            {
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(source))
                    throw new InvalidDataException($"Sprite atlas contains an empty source: {atlasPath}");
                var path = Path.Combine(directory, source).Replace('\\', '/');
                loaded.Add((name, context.Load<TextureAsset>(path)));
            }

            var ordered = loaded
                .OrderByDescending(item => item.Asset.Value.Height)
                .ThenByDescending(item => item.Asset.Value.Width)
                .ToArray();
            var placements = new List<(string Name, TextureAsset Texture, int X, int Y)>();
            var x = padding;
            var y = padding;
            var rowHeight = 0;
            foreach (var item in ordered)
            {
                var texture = item.Asset.Value;
                if (texture.Width + padding * 2 > width)
                    throw new InvalidDataException($"Sprite '{item.Name}' is wider than atlas {atlasPath}.");
                if (x + texture.Width + padding > width)
                {
                    x = padding;
                    y += rowHeight + padding * 2;
                    rowHeight = 0;
                }
                placements.Add((item.Name, texture, x, y));
                x += texture.Width + padding * 2;
                rowHeight = Math.Max(rowHeight, texture.Height);
            }
            var requiredHeight = y + rowHeight + padding;
            var height = 64;
            while (height < requiredHeight && height < 4096)
                height *= 2;
            if (height < requiredHeight)
                throw new InvalidDataException($"Sprite atlas is taller than 4096 pixels: {atlasPath}");

            var pixels = new byte[checked(width * height * 4)];
            var sprites = new Dictionary<string, UiSprite>(StringComparer.Ordinal);
            foreach (var placement in placements)
            {
                CopyWithExtrusion(
                    placement.Texture,
                    pixels,
                    width,
                    placement.X,
                    placement.Y,
                    padding);
                sprites.Add(
                    placement.Name,
                    new UiSprite(
                        placement.X,
                        placement.Y,
                        placement.Texture.Width,
                        placement.Texture.Height));
            }
            return new UiSpriteAtlasAsset(TextureAsset.FromRgba(width, height, pixels), sprites);
        }
        finally
        {
            foreach (var item in loaded)
                item.Asset.Dispose();
        }
    }

    private static void CopyWithExtrusion(
        TextureAsset source,
        byte[] destination,
        int destinationWidth,
        int destinationX,
        int destinationY,
        int padding)
    {
        for (var y = -padding; y < source.Height + padding; y++)
        for (var x = -padding; x < source.Width + padding; x++)
        {
            var sourceX = Math.Clamp(x, 0, source.Width - 1);
            var sourceY = Math.Clamp(y, 0, source.Height - 1);
            var sourceIndex = (sourceY * source.Width + sourceX) * 4;
            var targetIndex = ((destinationY + y) * destinationWidth + destinationX + x) * 4;
            destination[targetIndex] = source.Pixels[sourceIndex];
            destination[targetIndex + 1] = source.Pixels[sourceIndex + 1];
            destination[targetIndex + 2] = source.Pixels[sourceIndex + 2];
            destination[targetIndex + 3] = source.Pixels[sourceIndex + 3];
        }
    }

    private sealed class Descriptor
    {
        public string Texture { get; init; } = string.Empty;
        public Dictionary<string, SpriteDescriptor> Sprites { get; init; } = [];
        public Dictionary<string, string> Sources { get; init; } = [];
        public int Width { get; init; } = 1024;
        public int Padding { get; init; } = 2;
    }

    private sealed class SpriteDescriptor
    {
        public int X { get; init; }
        public int Y { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
    }
}
