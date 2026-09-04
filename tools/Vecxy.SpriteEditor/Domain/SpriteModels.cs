using System.Text.Json.Serialization;

namespace Vecxy.SpriteEditor;

public sealed class SpriteProject
{
    public string Root { get; }
    public string AssetsDirectory { get; }
    public IReadOnlyList<string> Images { get; }
    public IReadOnlyList<string> Atlases { get; }

    private SpriteProject(string root, string assets, IReadOnlyList<string> images, IReadOnlyList<string> atlases) =>
        (Root, AssetsDirectory, Images, Atlases) = (root, assets, images, atlases);

    public static SpriteProject Open(string folder)
    {
        var root = Path.GetFullPath(folder);
        var assets = Path.GetFileName(root).Equals("Assets", StringComparison.OrdinalIgnoreCase)
            ? root : Path.Combine(root, "Assets");
        if (!Directory.Exists(assets)) throw new DirectoryNotFoundException($"Assets folder was not found: {assets}");
        var images = Directory.EnumerateFiles(assets, "*", SearchOption.AllDirectories)
            .Where(path => new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tga" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var atlases = Directory.EnumerateFiles(assets, "*.atlas", SearchOption.AllDirectories)
            .Order(StringComparer.OrdinalIgnoreCase).ToArray();
        return new SpriteProject(root, assets, images, atlases);
    }

    public string Relative(string path) => Path.GetRelativePath(AssetsDirectory, path).Replace('\\', '/');
}

public sealed class SpriteAtlas
{
    public string Texture { get; set; } = "";
    public Dictionary<string, SpriteSlice> Sprites { get; set; } = new(StringComparer.Ordinal);
    [JsonIgnore] public string? FilePath { get; set; }
}

public sealed class SpriteSlice
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public float PivotX { get; set; } = .5f;
    public float PivotY { get; set; } = .5f;
}
