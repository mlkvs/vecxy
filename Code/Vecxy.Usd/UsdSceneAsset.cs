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
    public string UpAxis { get; set; } = "Y";
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
            if (prim.Collider == "box" && prim.ColliderSize is null)
                throw new InvalidDataException($"Box collider size is missing at '{prim.Path}'.");
            if (prim.ColliderCenter.Length != 3 || prim.ColliderCenter.Any(v => !float.IsFinite(v)) ||
                !float.IsFinite(prim.ColliderRadius) || prim.ColliderRadius <= 0 ||
                !float.IsFinite(prim.ColliderHeight) || prim.ColliderHeight < 0 ||
                !float.IsFinite(prim.Friction) || prim.Friction < 0 ||
                !float.IsFinite(prim.Restitution) || prim.Restitution is < 0 or > 1 ||
                prim.Collider is not ("" or "box" or "sphere" or "capsule") ||
                prim.ColliderAxis is not ("X" or "Y" or "Z") ||
                prim.Tint is { } tint && (tint.Length != 4 || tint.Any(v => !float.IsFinite(v) || v < 0)))
                throw new InvalidDataException($"Invalid USD component settings at '{prim.Path}'.");
            if (prim.Components.Any(c => string.IsNullOrWhiteSpace(c.Type) || c.Data.ValueKind != JsonValueKind.Object) ||
                prim.Components.Select(c => c.Key).Distinct(StringComparer.Ordinal).Count() != prim.Components.Count)
                throw new InvalidDataException($"Invalid USD gameplay components at '{prim.Path}'.");
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
    public string Collider { get; set; } = "";
    public float[]? ColliderSize { get; set; }
    public float[] ColliderCenter { get; set; } = [0, 0, 0];
    public float ColliderRadius { get; set; } = 0.35f;
    public float ColliderHeight { get; set; } = 1.1f;
    public string ColliderAxis { get; set; } = "Y";
    public float Friction { get; set; } = 0.5f;
    public float Restitution { get; set; }
    public bool Trigger { get; set; }
    public float[]? Tint { get; set; }
    public List<UsdComponentRecord> Components { get; set; } = [];
    public string Light { get; set; } = "";
    public float[] Color { get; set; } = [1, 1, 1];
    public float Intensity { get; set; }
    public float Range { get; set; }
}

public sealed class UsdComponentRecord
{
    public string Key { get; set; } = "";
    public string Type { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public JsonElement Data { get; set; }
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
