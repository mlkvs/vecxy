using System.Text.Json;
using Vecxy.Assets;

namespace Vecxy.UI;

public sealed class UiSpriteAtlasAsset : IDisposable
{
    internal AssetRef<TextureAsset> Texture { get; }
    public IReadOnlyDictionary<string, UiSprite> Sprites { get; }

    internal UiSpriteAtlasAsset(
        AssetRef<TextureAsset> texture,
        IReadOnlyDictionary<string, UiSprite> sprites)
    {
        Texture = texture;
        Sprites = sprites;
    }

    public void Dispose() => Texture.Dispose();
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
        if (string.IsNullOrWhiteSpace(descriptor.Texture))
            throw new InvalidDataException($"Sprite atlas has no texture: {metadata.Path}");

        var directory = Path.GetDirectoryName(metadata.Path) ?? string.Empty;
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

    private sealed class Descriptor
    {
        public string Texture { get; init; } = string.Empty;
        public Dictionary<string, SpriteDescriptor> Sprites { get; init; } = [];
    }

    private sealed class SpriteDescriptor
    {
        public int X { get; init; }
        public int Y { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
    }
}
