using System.Text.Json;

namespace Vecxy.SpriteEditor;

public sealed class AtlasRepository
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    public SpriteAtlas Load(string path)
    {
        var atlas = JsonSerializer.Deserialize<SpriteAtlas>(File.ReadAllText(path), Json)
                    ?? throw new InvalidDataException($"Atlas is empty: {path}");
        atlas.FilePath = Path.GetFullPath(path);
        return atlas;
    }

    public void Save(SpriteAtlas atlas, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        atlas.FilePath = Path.GetFullPath(path);
        File.WriteAllText(path, JsonSerializer.Serialize(atlas, Json) + "\n");
    }
}
