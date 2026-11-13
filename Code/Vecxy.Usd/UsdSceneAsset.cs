using System.Text.Json;
using Vecxy.Assets;

namespace Vecxy.Usd;

/// <summary>A cooked OpenUSD stage. USD composition happens in the offline OpenUSD tool.</summary>
public sealed class UsdSceneAsset : IDisposable
{
    public UsdSceneDocument Document { get; }
    public AssetRef<ModelAsset> Model { get; }

    internal UsdSceneAsset(UsdSceneDocument document, AssetRef<ModelAsset> model)
    {
        Document = document;
        Model = model;
    }

    public void Dispose() => Model.Dispose();
}

public sealed class UsdSceneDocument
{
    public int Format { get; set; }
    public string Source { get; set; } = "";
    public string Model { get; set; } = "";
    public List<UsdPrimRecord> Prims { get; set; } = [];

    public static UsdSceneDocument Parse(string json)
    {
        var value = JsonSerializer.Deserialize<UsdSceneDocument>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidDataException("USD scene document is empty.");
        if (value.Format != 1 || string.IsNullOrWhiteSpace(value.Model) ||
            Path.IsPathRooted(value.Model) || value.Model.Split('/', '\\').Contains(".."))
            throw new InvalidDataException("USD scene requires format 1 and an asset-relative model path.");
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var prim in value.Prims)
        {
            if (!prim.Path.StartsWith('/') || !paths.Add(prim.Path) || prim.Node < 0 ||
                !float.IsFinite(prim.Intensity) || prim.Intensity < 0 ||
                !float.IsFinite(prim.Range) || prim.Range < 0 ||
                prim.ColliderSize is { } size && (size.Length != 3 || size.Any(v => !float.IsFinite(v) || v <= 0)))
                throw new InvalidDataException($"Invalid USD prim record '{prim.Path}'.");
            if (prim.Color.Length != 3 || prim.Color.Any(v => !float.IsFinite(v) || v < 0))
                throw new InvalidDataException($"Invalid USD light color at '{prim.Path}'.");
        }
        return value;
    }
}

public sealed class UsdPrimRecord
{
    public string Path { get; set; } = "";
    public int Node { get; set; }
    public string Role { get; set; } = "";
    public string Prototype { get; set; } = "";
    public float[]? ColliderSize { get; set; }
    public string Light { get; set; } = "";
    public float[] Color { get; set; } = [1, 1, 1];
    public float Intensity { get; set; }
    public float Range { get; set; }
}

public sealed class UsdSceneAssetImporter : IAssetImporter<UsdSceneAsset>
{
    public IReadOnlyCollection<string> Extensions { get; } = [".usd", ".usda", ".usdc", ".usdz"];

    public UsdSceneAsset Import(AssetMetadata metadata, AssetImportContext context)
    {
        var path = metadata.Path + ".usdscene";
        UsdSceneDocument document;
        try
        {
            document = UsdSceneDocument.Parse(context.ReadAllText(path));
        }
        catch (FileNotFoundException exception)
        {
            throw new InvalidDataException($"USD stage '{metadata.Path}' has not been cooked. Run Vecxy.Usd/Tools/cook_usd.py first.", exception);
        }
        var model = context.Load<ModelAsset>(document.Model);
        try
        {
            foreach (var prim in document.Prims)
                if (prim.Node >= model.Value.Nodes.Count)
                    throw new InvalidDataException($"USD node index is out of range at '{prim.Path}'.");
            return new UsdSceneAsset(document, model);
        }
        catch
        {
            model.Dispose();
            throw;
        }
    }
}
