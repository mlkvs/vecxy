using System.Numerics;
using System.Text.Json;
using Vecxy.Assets;
using Vecxy.Rendering;
using Vecxy.Usd;

void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    Console.WriteLine($"PASS: {message}");
}
void Invalid(string json, string message)
{
    try { UsdSceneDocument.Parse(json); }
    catch (InvalidDataException) { Console.WriteLine($"PASS: {message}"); return; }
    throw new Exception(message);
}
Invalid("""{"format":1,"model":"x.glb","prims":[{"path":"/A","node":0,"collider":"capsule","colliderRadius":-1}]}""", "Invalid capsule radius rejected");
Invalid("""{"format":1,"model":"x.glb","prims":[{"path":"/A","node":0,"collider":"box"}]}""", "Box without dimensions rejected");
Invalid("{}", "Missing model and version rejected");
Invalid("""{"format":2,"model":"x.glb"}""", "Unsupported document version rejected");
Invalid("""{"format":1,"model":"../x.glb"}""", "Escaping asset path rejected");
Invalid("""{"format":1,"model":"x.glb","prims":[{"path":"/A","node":0},{"path":"/A","node":1}]}""", "Duplicate prim paths rejected");
Invalid("""{"format":1,"model":"x.glb","prims":[{"path":"/A","node":0,"colliderSize":[1,-1,1]}]}""", "Negative collider dimensions rejected");

if (args.Length == 0)
{
    Console.WriteLine("Pass a game Assets directory to also verify a cooked USD scene.");
    return;
}
var root = Path.GetFullPath(args[0]);
using var assets = new AssetsModule(new AssetsModule.Options { AssetsDirectory = root, HotReloadEnabled = false });
assets.OnInitialize();
assets.RegisterImporter(new UsdSceneAssetImporter());
using var stage = assets.Load<UsdSceneAsset>("Scenes/Sandbox.usda");
var document = stage.Value.Document;
var source = stage.Value.Model.Value;
Check(document.Prims.Count > 0 && source.Meshes.Count > 0, "USD importer loads cooked stage and render model");
var washers = document.Prims.Where(x => x.Role == "washer").ToArray();
Check(washers.Length == 2, "Two washer instances loaded");
Check(washers.Select(x => x.Prototype).Distinct().Count() == 1 && washers[0].Prototype.Length > 0,
    "All washers share one USD prototype");
Check(document.Prims.Count(x => x.Role == "spawn") == 1,
    "Spawn is an authored USD marker");
Check(document.Prims.Count(x => x.Light.Length > 0) == 2, "Two authored lights loaded");
Check(document.Prims.Count(x => x.ColliderSize is not null || x.Components.Any(c => c.Enabled && c.Type == "Vecxy.Physics.BoxCollider")) == 3, "Floor and two prototype colliders loaded");
var meshes = new List<int[]>();
foreach (var washer in washers)
{
    var children = document.Prims.Where(x => x.Path.StartsWith(washer.Path + "/", StringComparison.Ordinal))
        .Select(x => source.Nodes[x.Node].MeshIndex).OfType<int>().Order().ToArray();
    meshes.Add(children);
}
Check(meshes[0].Length > 0 && meshes.All(x => x.SequenceEqual(meshes[0])),
    "Washer nodes reuse identical render mesh indices between both placed instances");
Check(source.Materials.Count >= 8, "Clean PreviewSurface materials survived cooking");
Check(source.Materials.Any(x => x.BaseColor.W < 1), "Transparent glass survived cooking");
var spawn = document.Prims.Single(x => x.Role == "spawn");
var node = source.Nodes[spawn.Node];
Check(MathF.Abs(node.LocalTransform.Translation.X) < .001f, "USD spawn position preserved");
Console.WriteLine("All Vecxy.Usd checks passed.");
