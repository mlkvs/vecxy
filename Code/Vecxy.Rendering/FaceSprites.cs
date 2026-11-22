using System.Buffers;
using Vecxy.Assets;
using Vecxy.Prototypes;
using Vecxy.Scene;

namespace Vecxy.Rendering;

/// <summary>Composites independently switchable sprites onto a mesh's authored face coordinates.</summary>
[SingleComponent]
public sealed class FaceSprites : AComponent
{
    private sealed class Zone
    {
        public required ZoneDefinition Definition { get; init; }
        public required Dictionary<string, Sprite> Sprites { get; init; }
        public string? Current { get; set; }
    }

    private readonly record struct Crop(int X, int Y, int Width, int Height);
    private readonly record struct Sprite(AssetRef<TextureAsset> Texture, Crop? Region);

    /// <summary>Atlas rectangle in pixels, measured from the top-left corner.</summary>
    public sealed class SpriteRegion
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }

    public sealed class ZoneDefinition
    {
        public string Id { get; set; } = "";
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; } = 1;
        public float Height { get; set; } = 1;
        public string DefaultSprite { get; set; } = "";
        public Dictionary<string, string> Sprites { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, SpriteRegion> SpriteRegions { get; set; } = new(StringComparer.Ordinal);
    }

    private Dictionary<string, Zone> _zones = new(StringComparer.Ordinal);
    private Texture? _atlas;
    private int _atlasSize;
    private int _resolution = 256;
    private bool _dirty = true;
    private bool _nearest = true;

    public IReadOnlyCollection<string> Zones => _zones.Keys;
    public string? GetSprite(string zoneId) => GetZone(zoneId).Current;

    /// <summary>Selects a sprite by ID. Pass null to clear the zone.</summary>
    public void SetSprite(string zoneId, string? spriteId)
    {
        ObjectDisposedException.ThrowIf(IsDestroyed, this);
        var zone = GetZone(zoneId);
        if (spriteId is not null && !zone.Sprites.ContainsKey(spriteId))
            throw new KeyNotFoundException($"Face zone '{zoneId}' has no sprite '{spriteId}'.");
        if (zone.Current == spriteId)
            return;
        zone.Current = spriteId;
        _dirty = true;
    }

    public void Configure(IAssetsManager assets, IEnumerable<ZoneDefinition> definitions,
        int resolution = 256, bool nearest = true)
    {
        ObjectDisposedException.ThrowIf(IsDestroyed, this);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(definitions);
        if (resolution is < 64 or > 1024)
            throw new ArgumentOutOfRangeException(nameof(resolution));

        var next = new Dictionary<string, Zone>(StringComparer.Ordinal);
        try
        {
            foreach (var definition in definitions)
            {
                Validate(definition);
                var zone = new Zone
                {
                    Definition = new ZoneDefinition
                    {
                        Id = definition.Id, X = definition.X, Y = definition.Y,
                        Width = definition.Width, Height = definition.Height
                    },
                    Sprites = new(StringComparer.Ordinal),
                    Current = string.IsNullOrEmpty(definition.DefaultSprite) ? null : definition.DefaultSprite
                };
                if (!next.TryAdd(definition.Id, zone))
                    throw new ArgumentException($"Duplicate face zone '{definition.Id}'.", nameof(definitions));

                foreach (var (id, path) in definition.Sprites)
                {
                    if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(path))
                        throw new ArgumentException($"Face zone '{definition.Id}' has a sprite without an ID or texture.");
                    var texture = assets.Load<TextureAsset>(path);
                    try
                    {
                        if (texture.HasError)
                            throw new InvalidDataException($"Could not load face sprite '{path}' for zone '{definition.Id}'.");
                        var crop = definition.SpriteRegions.TryGetValue(id, out var region)
                            ? ReadCrop(region, texture.Value, id) : (Crop?)null;
                        zone.Sprites.Add(id, new Sprite(texture, crop));
                    }
                    catch
                    {
                        texture.Dispose();
                        throw;
                    }
                }
                if (zone.Current is not null && !zone.Sprites.ContainsKey(zone.Current))
                    throw new ArgumentException($"Unknown default sprite '{zone.Current}' in face zone '{definition.Id}'.");
            }
        }
        catch
        {
            Release(next);
            throw;
        }

        Release(_zones);
        _zones = next;
        _resolution = resolution;
        _nearest = nearest;
        _dirty = true;
    }

    internal TextureSamplerState Sampler => _nearest ? TextureSamplerState.PointClamp : TextureSamplerState.LinearClamp;

    internal Texture? ResolveAtlas(GraphicsDevice device)
    {
        if (!_dirty)
            return _atlas;

        if (!_zones.Values.Any(zone => zone.Current is not null))
        {
            _atlas?.Dispose();
            _atlas = null;
            _dirty = false;
            return null;
        }

        var length = _resolution * _resolution * 4;
        var pixels = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            var image = pixels.AsSpan(0, length);
            image.Clear();
            foreach (var zone in _zones.Values)
                if (zone.Current is { } current)
                    Composite(pixels, _resolution, zone.Definition, zone.Sprites[current]);

            if (_atlas is not null && _atlasSize == _resolution)
                _atlas.UpdatePixels(_resolution, _resolution, image);
            else
            {
                var next = new Texture(device, _resolution, _resolution, image);
                _atlas?.Dispose();
                _atlas = next;
                _atlasSize = _resolution;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(pixels);
        }

        _dirty = false;
        return _atlas;
    }

    private static void Composite(byte[] target, int size, ZoneDefinition zone, Sprite sprite)
    {
        var source = sprite.Texture.Value;
        var crop = sprite.Region ?? new Crop(0, 0, source.Width, source.Height);
        var left = Math.Clamp((int)MathF.Round(zone.X * size), 0, size - 1);
        var top = Math.Clamp((int)MathF.Round(zone.Y * size), 0, size - 1);
        var right = Math.Clamp((int)MathF.Round((zone.X + zone.Width) * size), left + 1, size);
        var bottom = Math.Clamp((int)MathF.Round((zone.Y + zone.Height) * size), top + 1, size);

        for (var y = top; y < bottom; y++)
        {
            var row = crop.Y + (int)((long)(y - top) * crop.Height / (bottom - top));
            for (var x = left; x < right; x++)
            {
                var column = crop.X + (int)((long)(x - left) * crop.Width / (right - left));
                var src = (row * source.Width + column) * 4;
                var dst = (y * size + x) * 4;
                var alpha = source.Pixels[src + 3];
                var retain = 255 - alpha;
                // Keep the atlas premultiplied so linear filtering and mips preserve transparent edges.
                for (var channel = 0; channel < 3; channel++)
                    target[dst + channel] = (byte)((source.Pixels[src + channel] * alpha +
                        target[dst + channel] * retain + 127) / 255);
                target[dst + 3] = (byte)(alpha + (target[dst + 3] * retain + 127) / 255);
            }
        }
    }

    private static void Validate(ZoneDefinition zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if (string.IsNullOrWhiteSpace(zone.Id) || !float.IsFinite(zone.X) || !float.IsFinite(zone.Y) ||
            !float.IsFinite(zone.Width) || !float.IsFinite(zone.Height) ||
            zone.X < 0 || zone.Y < 0 || zone.Width <= 0 || zone.Height <= 0 ||
            zone.X + zone.Width > 1.000001f || zone.Y + zone.Height > 1.000001f)
            throw new ArgumentException($"Face zone '{zone.Id}' requires an ID and a rectangle inside 0–1.");
        ArgumentNullException.ThrowIfNull(zone.Sprites);
        ArgumentNullException.ThrowIfNull(zone.SpriteRegions);
        foreach (var id in zone.SpriteRegions.Keys)
            if (!zone.Sprites.ContainsKey(id))
                throw new ArgumentException($"Atlas region references unknown sprite '{id}' in zone '{zone.Id}'.");
    }

    private static Crop ReadCrop(SpriteRegion region, TextureAsset texture, string id)
    {
        if (region is null || region.X < 0 || region.Y < 0 || region.Width <= 0 || region.Height <= 0 ||
            region.X > texture.Width - region.Width || region.Y > texture.Height - region.Height)
            throw new ArgumentException($"Sprite '{id}' has a region outside its source image.");
        return new Crop(region.X, region.Y, region.Width, region.Height);
    }

    private Zone GetZone(string id) => _zones.TryGetValue(id, out var zone)
        ? zone
        : throw new KeyNotFoundException($"Unknown face zone '{id}'.");

    private static void Release(Dictionary<string, Zone> zones)
    {
        foreach (var zone in zones.Values)
            foreach (var sprite in zone.Sprites.Values)
                sprite.Texture.Dispose();
        zones.Clear();
    }

    public override void OnDestroy()
    {
        _atlas?.Dispose();
        _atlas = null;
        Release(_zones);
    }

    public sealed class Prototype(IAssetsManager assets) : APrototype<FaceSprites, Prototype.Options>
    {
        public sealed class Options
        {
            public int Resolution { get; set; } = 256;
            public bool Nearest { get; set; } = true;
            public List<ZoneDefinition> Zones { get; set; } = [];
        }

        protected override FaceSprites Instantiate(IPrototypeContext context)
        {
            var scene = RequireContext<ScenePrototypeContext>(context);
            return (scene.Object ?? scene.CreateObject("Face Sprites")).AddComponent<FaceSprites>();
        }

        protected override void Configure(FaceSprites target, Options options) =>
            target.Configure(assets, options.Zones, options.Resolution, options.Nearest);
    }
}
