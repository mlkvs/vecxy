using System.Text.Json;

namespace Vecxy.SpriteEditor;

public sealed class RecentFiles
{
    private const int Capacity = 8;
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".vecxy", "state", "sprite-editor-recent.json");
    private readonly List<string> _items;

    public RecentFiles()
    {
        try
        {
            _items = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_path)) ?? [];
            _items.RemoveAll(path => !File.Exists(path));
        }
        catch
        {
            _items = [];
        }
    }

    public IReadOnlyList<string> Items => _items;

    public void Add(string path)
    {
        path = Path.GetFullPath(path);
        _items.RemoveAll(item => item.Equals(path, StringComparison.OrdinalIgnoreCase));
        _items.Insert(0, path);
        if (_items.Count > Capacity) _items.RemoveRange(Capacity, _items.Count - Capacity);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(_items, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }
}
