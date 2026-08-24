using System.Text.Json;

namespace Vecxy.Assets;

public sealed class AssetManifest
{
    public List<AssetManifestEntry> Assets { get; init; } = [];

    public static AssetManifest Load(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<AssetManifest>(stream, SerializerOptions)
               ?? throw new InvalidDataException($"Asset manifest is empty: {path}");
    }

    public static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
}

public sealed class AssetManifestEntry
{
    public Guid Id { get; init; }
    public string Source { get; init; } = "Game";
    public required string Path { get; init; }
    public required string Type { get; init; }
    public string? Name { get; init; }
    public string? Hash { get; init; }
    public List<Guid> Dependencies { get; init; } = [];
}
