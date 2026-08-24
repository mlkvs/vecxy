using Vecxy.AssetPipeline;

var root = Path.Combine(Path.GetTempPath(), "vecxy-asset-tests", Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(Path.Combine(root, "Assets", "Textures"));
    var original = Path.Combine(root, "Assets", "Textures", "player.png");
    File.WriteAllBytes(original, [1, 2, 3, 4]);

    var first = AssetPipeline.Scan(root);
    Assert(first.Assets.Count == 1, "manifest generation");
    var id = first.Assets.Single().Id;

    var generated = AssetPipeline.GenerateSource(first);
    Assert(generated.Contains("public static TextureHandle Player", StringComparison.Ordinal), "generated property");
    Assert(generated.Contains(id.ToString("D"), StringComparison.Ordinal), "generated stable ID");

    var renamed = Path.Combine(root, "Assets", "Textures", "hero.png");
    File.Move(original, renamed);
    var second = AssetPipeline.Scan(root);
    Assert(second.Assets.Single().Id == id, "rename keeps ID");
    Assert(second.Assets.Single().Path == "Textures/hero.png", "rename updates path");
    Assert(AssetPipeline.GenerateSource(second).Contains("TextureHandle Player", StringComparison.Ordinal), "rename keeps generated symbol");

    File.WriteAllText(Path.Combine(root, "Player.cs"), "class Player { object Texture => Assets.Textures.Player; }");
    var references = AssetPipeline.Analyze(root);
    File.Delete(renamed);
    Directory.CreateDirectory(Path.Combine(root, "Assets", "Configs"));
    File.WriteAllText(Path.Combine(root, "Assets", "Configs", "game-balance.yaml"), "speed: 10");
    var third = AssetPipeline.Scan(root);
    Assert(third.Assets.Single(x => x.Path == "Textures/hero.png").Id == id, "deleted manifest entry retained");
    var config = third.Assets.Single(x => x.Path == "Configs/game-balance.yaml");
    Assert(config.Type == "Config", "config included in manifest");
    Assert(AssetPipeline.GenerateSource(third).Contains("ConfigHandle GameBalance", StringComparison.Ordinal), "config handle generated");
    var missing = AssetPipeline.Validate(root, references);
    Assert(missing.Count == 1, "missing asset detected");
    Assert(missing[0].References.Single().Line == 1, "missing asset reference retained");

    var engineProject = Path.Combine(root, "Engine", "Vecxy.Engine");
    Directory.CreateDirectory(Path.Combine(engineProject, "Assets", "Shaders"));
    File.WriteAllText(Path.Combine(engineProject, "Vecxy.Engine.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
    File.WriteAllText(Path.Combine(engineProject, "Assets", "Shaders", "sprite.glsl"), "#type vertex\n#type fragment");
    File.WriteAllText(Path.Combine(root, "Game.csproj"),
        "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><ProjectReference Include=\"Engine/Vecxy.Engine/Vecxy.Engine.csproj\" /></ItemGroup></Project>");
    var withEngine = AssetPipeline.Scan(root);
    var engineAsset = withEngine.Assets.Single(x => x.Source == "Engine");
    Assert(engineAsset.Path == "Shaders/sprite.glsl", "engine asset scanned from project reference");
    Assert(AssetPipeline.GenerateSource(withEngine).Contains("class Engine", StringComparison.Ordinal), "engine generated namespace");

    Console.WriteLine("Vecxy.AssetPipeline tests passed.");
    return 0;
}
finally
{
    if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
}

static void Assert(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException($"Test failed: {name}");
}
