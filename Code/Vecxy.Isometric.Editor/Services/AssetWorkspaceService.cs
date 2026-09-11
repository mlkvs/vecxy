using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vecxy.Isometric.Editor;

public sealed class AssetWorkspaceService
{
    private static readonly Dictionary<EEditorAssetKind, string> Extensions = new()
    {
        [EEditorAssetKind.Surface] = ".vsurface",
        [EEditorAssetKind.Wall] = ".vwall",
        [EEditorAssetKind.Object] = ".vobject",
        [EEditorAssetKind.Opening] = ".vopening",
        [EEditorAssetKind.Connection] = ".vconnection"
    };

    public JsonSerializerOptions Json { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public string? AssetsDirectory { get; private set; }
    public IReadOnlyList<IsometricAssetDefinition> Definitions { get; private set; } = [];
    public IReadOnlyList<string> Worlds { get; private set; } = [];

    public void Open(string folder)
    {
        var full = Path.GetFullPath(folder);
        var assets = Path.GetFileName(full).Equals("Assets", StringComparison.OrdinalIgnoreCase)
            ? full : Path.Combine(full, "Assets");
        if (!Directory.Exists(assets))
            throw new DirectoryNotFoundException($"Assets folder was not found: {assets}");
        AssetsDirectory = assets;
        Refresh();
    }

    public void Refresh()
    {
        if (AssetsDirectory is null) return;
        var definitions = new List<IsometricAssetDefinition>();
        foreach (var path in Directory.EnumerateFiles(AssetsDirectory, "*", SearchOption.AllDirectories)
                     .Where(path => Extensions.Values.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)))
        {
            try
            {
                var definition = JsonSerializer.Deserialize<IsometricAssetDefinition>(File.ReadAllText(path), Json);
                if (definition is null) continue;
                definition.FilePath = path;
                definitions.Add(definition);
            }
            catch
            {
                // Invalid assets stay on disk and are reported when explicitly opened.
            }
        }
        Definitions = definitions.OrderBy(x => x.Kind).ThenBy(x => x.Category).ThenBy(x => x.Name).ToArray();
        Worlds = Directory.EnumerateFiles(AssetsDirectory, "*.vworld", SearchOption.AllDirectories)
            .Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public IsometricAssetDefinition CreateDefinition(EEditorAssetKind kind, string name)
    {
        EnsureOpen();
        var safeName = SafeFileName(name);
        var folder = Path.Combine(AssetsDirectory!, "Isometric", kind + "s");
        Directory.CreateDirectory(folder);
        var definition = new IsometricAssetDefinition
        {
            Kind = kind,
            Name = name,
            Category = kind.ToString(),
            Blocking = kind is EEditorAssetKind.Wall or EEditorAssetKind.Object,
            Slots = kind switch
            {
                EEditorAssetKind.Object =>
                [new EditorSlotDefinition { Id = "surface", Type = "SupportSurface", Capacity = 8 },
                 new EditorSlotDefinition { Id = "inside", Type = "Container", Capacity = 16 }],
                EEditorAssetKind.Wall =>
                [new EditorSlotDefinition { Id = "wall", Type = "AttachmentSurface", Capacity = 8 },
                 new EditorSlotDefinition { Id = "opening", Type = "Opening", Capacity = 8 }],
                _ => []
            }
        };
        definition.FilePath = UniquePath(folder, safeName, Extensions[kind]);
        SaveDefinition(definition);
        Refresh();
        return Definitions.Single(x => x.AssetId == definition.AssetId);
    }

    public void SaveDefinition(IsometricAssetDefinition definition)
    {
        EnsureInsideAssets(definition.FilePath);
        AtomicWrite(definition.FilePath, JsonSerializer.Serialize(definition, Json));
    }

    public WorldDocument CreateWorld(string name)
    {
        EnsureOpen();
        var folder = Path.Combine(AssetsDirectory!, "Isometric", "Worlds", SafeFileName(name));
        Directory.CreateDirectory(folder);
        var world = new IsometricWorldAsset { Name = name, FilePath = UniquePath(folder, SafeFileName(name), ".vworld") };
        var document = new WorldDocument(world, Json);
        SaveWorld(document);
        return document;
    }

    public WorldDocument OpenWorld(string path)
    {
        EnsureInsideAssets(path);
        var world = JsonSerializer.Deserialize<IsometricWorldAsset>(File.ReadAllText(path), Json) ??
                    throw new InvalidDataException($"Invalid world: {path}");
        world.FilePath = Path.GetFullPath(path);
        return new WorldDocument(world, Json);
    }

    public void SaveWorld(WorldDocument document)
    {
        EnsureInsideAssets(document.World.FilePath);
        AtomicWrite(document.World.FilePath, JsonSerializer.Serialize(document.World, Json));
        document.MarkSaved();
        Refresh();
    }

    public string Relative(string path) => AssetsDirectory is null
        ? path : Path.GetRelativePath(AssetsDirectory, path).Replace('\\', '/');

    private void EnsureOpen()
    {
        if (AssetsDirectory is null) throw new InvalidOperationException("Open an Assets folder first.");
    }

    private void EnsureInsideAssets(string path)
    {
        EnsureOpen();
        var full = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(AssetsDirectory!, full);
        if (relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}"))
            throw new InvalidOperationException("The file must be inside the opened Assets folder.");
    }

    private static void AtomicWrite(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, contents);
        File.Move(temporary, path, true);
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var result = new string(value.Trim().Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(result) ? "NewAsset" : result;
    }

    private static string UniquePath(string folder, string name, string extension)
    {
        var path = Path.Combine(folder, name + extension);
        for (var index = 2; File.Exists(path); index++) path = Path.Combine(folder, $"{name}{index}{extension}");
        return path;
    }
}
