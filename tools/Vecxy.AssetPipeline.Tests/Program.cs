using Vecxy.AssetPipeline;
using Vecxy.Assets;

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

    await TestPackages(root);
    await TestBinaryFormat();
    TestDependencyCycle(root);

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

static async Task TestPackages(string parent)
{
    var root = Path.Combine(parent, "vpack-integration");
    Directory.CreateDirectory(Path.Combine(root, "Assets", "Shared"));
    Directory.CreateDirectory(Path.Combine(root, "Assets", "DLC", "Cars"));
    Directory.CreateDirectory(Path.Combine(root, "Assets", "Harbor"));
    File.WriteAllText(Path.Combine(root, "Game.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
    File.WriteAllText(Path.Combine(root, "Assets", "game.vpack"), "name: Game\ncompression: maximum\nplatforms:\n  android:\n    compression: fast\n");
    File.WriteAllText(Path.Combine(root, "Assets", "player.txt"), "player");
    File.WriteAllText(Path.Combine(root, "Assets", "Shared", "shared.vpack"), "name: Shared\nload: startup\ncompression: balanced\nplatforms:\n  android:\n    compression:\n      algorithm: lz4\n      block-size: 256kb\n");
    File.WriteAllText(Path.Combine(root, "Assets", "Shared", "common.txt"), new string('s', 4096));
    File.WriteAllText(Path.Combine(root, "Assets", "DLC", "dlc.vpack"), "name: DLC\ndependencies:\n  - Shared\n");
    File.WriteAllText(Path.Combine(root, "Assets", "DLC", "links.xml"), "<asset>Shared/common.txt</asset>");
    File.WriteAllText(Path.Combine(root, "Assets", "DLC", "dlc.txt"), "dlc");
    File.WriteAllText(Path.Combine(root, "Assets", "DLC", "settings.yaml"), "value: 42");
    File.WriteAllText(Path.Combine(root, "Assets", "DLC", "Cars", "cars.vpack"), "name: Cars\ndependencies:\n  - Shared\n");
    File.WriteAllText(Path.Combine(root, "Assets", "DLC", "Cars", "sedan.txt"), "sedan");
    File.WriteAllText(Path.Combine(root, "Assets", "Harbor", "harbor.vpack"), "name: Harbor\ndependencies:\n  - Shared\ncompression:\n  algorithm: zstd\n  level: 5\n  block-size: 512kb\n");
    File.WriteAllText(Path.Combine(root, "Assets", "Harbor", "harbor.txt"), "harbor");

    var packages = VPackPipeline.DiscoverPackages(root);
    Assert(packages.Count == 5, "implicit and explicit package discovery");
    var game = packages.Single(x => x.Id == PackageId.Game);
    Assert(game.DescriptorPath == "game.vpack" && game.Load == PackageLoadMode.Startup, "root Game descriptor");
    Assert(VPackPipeline.ResolveCompression(game, VPackPlatform.Android).Algorithm == VPackCompressionAlgorithm.Lz4, "root Game platform configuration");
    var manifest = AssetPipeline.Scan(root);
    Assert(manifest.Assets.Single(x => x.Path == "player.txt").Package == PackageId.Game, "implicit Game membership");
    var dlc = packages.Single(x => x.Name == "DLC");
    var cars = packages.Single(x => x.Name == "Cars");
    Assert(manifest.Assets.Single(x => x.Path.EndsWith("dlc.txt")).Package == dlc.Id, "explicit package membership");
    Assert(manifest.Assets.Single(x => x.Path.EndsWith("sedan.txt")).Package == cars.Id, "nested package boundary");
    Assert(AssetPipeline.Scan(root).Assets.Single(x => x.Path.EndsWith("sedan.txt")).Package == cars.Id, "stable package assignment");
    var shared = packages.Single(x => x.Name == "Shared");
    var android = VPackPipeline.ResolveCompression(shared, VPackPlatform.Android);
    Assert(android.Algorithm == VPackCompressionAlgorithm.Lz4 && android.BlockSize == 256 * 1024, "advanced platform override");
    Assert(VPackPipeline.ResolveCompression(shared, VPackPlatform.Windows).Algorithm == VPackCompressionAlgorithm.Zstd, "desktop profile resolution");
    Assert(VPackPipeline.ValidatePackageDependencies(manifest).Count == 0, "declared cross-package dependency");
    var generated = AssetPipeline.GenerateSource(manifest);
    Assert(generated.Contains("public static class DLC", StringComparison.Ordinal) && generated.Contains("LoadAsync", StringComparison.Ordinal), "generated package API");
    Assert(generated.Contains("TextHandle Dlc", StringComparison.Ordinal), "generated packaged asset handle");

    var builds = await VPackPipeline.BuildAsync(root, VPackPlatform.Windows);
    var output = Path.Combine(root, "Build", "Windows", "Packages");
    Assert(builds.Count == 5 && File.Exists(Path.Combine(output, "packages.manifest")), "platform package build output");
    Assert(File.Exists(Path.Combine(output, "dlc.vpack")), "explicit package output");
    await VPackPipeline.BuildAsync(root, VPackPlatform.Linux);
    await VPackPipeline.BuildAsync(root, VPackPlatform.Android);
    Assert(File.Exists(Path.Combine(root, "Build", "Linux", "Packages", "game.vpack")) &&
           File.Exists(Path.Combine(root, "Build", "Android", "Packages", "game.vpack")), "per-platform build outputs");
    await using (var reader = await VPackReader.OpenAsync(File.OpenRead(Path.Combine(output, "dlc.vpack"))))
    {
        var asset = manifest.Assets.Single(x => x.Path.EndsWith("links.xml"));
        Assert(System.Text.Encoding.UTF8.GetString((await reader.ReadAssetAsync(new AssetId(asset.Id))).Span).Contains("Shared/common.txt"), "lookup by AssetId");
    }

    var module = new AssetsModule(new AssetsModule.Options { AssetsDirectory = Path.Combine(root, "Assets"), PackagesDirectory = output, HotReloadEnabled = false });
    module.OnInitialize();
    var dlcAsset = manifest.Assets.Single(x => x.Path.EndsWith("dlc.txt"));
    var dlcConfig = manifest.Assets.Single(x => x.Path.EndsWith("settings.yaml"));
    AssertThrows<AssetPackageNotLoadedException>(() => module.Load<TextAsset>(new AssetId(dlcAsset.Id)), "unloaded package diagnostic");
    await using (var lease = await module.LoadPackageAsync(dlc.Id))
    {
        Assert(lease.Package.IsLoaded && module.GetPackage(shared.Id).IsLoaded, "dependency package loading");
        using var loaded = module.Load<TextAsset>(new AssetId(dlcAsset.Id));
        using var config = module.LoadConfig<TestConfig>(new ConfigHandle(dlcConfig.Id));
        Assert(config.Value.Value == 42, "packaged config loading");
    }
    Assert(!module.GetPackage(dlc.Id).IsLoaded, "package reference-counted unload");
    module.Dispose();

    File.WriteAllText(Path.Combine(root, "Assets", "DLC", "dlc.vpack"), "name: DLC\n");
    var invalid = AssetPipeline.Scan(root);
    Assert(VPackPipeline.ValidatePackageDependencies(invalid).Any(x => x.Contains("VXY2104", StringComparison.Ordinal)), "undeclared cross-package dependency detection");
}

static async Task TestBinaryFormat()
{
    foreach (var algorithm in new[] { VPackCompressionAlgorithm.None, VPackCompressionAlgorithm.Lz4, VPackCompressionAlgorithm.Zstd })
    {
        var first = new VPackAssetSource(AssetId.New(), "Text", System.Text.Encoding.UTF8.GetBytes(new string('a', 10000)), algorithm == VPackCompressionAlgorithm.None);
        var second = new VPackAssetSource(AssetId.New(), "Text", System.Text.Encoding.UTF8.GetBytes("random-access"));
        await using var stream = new MemoryStream();
        await VPackWriter.WriteAsync(stream, PackageId.FromName("Roundtrip"), VPackPlatform.Windows, [], [first, second], new(algorithm, 3, 8 * 1024));
        stream.Position = 0;
        await using var reader = await VPackReader.OpenAsync(stream);
        Assert((await reader.ReadAssetAsync(first.Id)).Span.SequenceEqual(first.Data.Span), $"{algorithm} block roundtrip");
        Assert((await reader.ReadAssetAsync(second.Id)).Span.SequenceEqual(second.Data.Span), $"{algorithm} block random access");
    }

    await using var corrupt = new MemoryStream(new byte[VPackFormat.HeaderSize]);
    await AssertThrowsAsync<InvalidDataException>(async () => await VPackReader.OpenAsync(corrupt), "corrupted header rejection");
    var unsupported = new byte[VPackFormat.HeaderSize];
    BitConverter.GetBytes(VPackFormat.Magic).CopyTo(unsupported, 0); BitConverter.GetBytes((ushort)99).CopyTo(unsupported, 4); BitConverter.GetBytes(VPackFormat.HeaderSize).CopyTo(unsupported, 6);
    await AssertThrowsAsync<NotSupportedException>(async () => await VPackReader.OpenAsync(new MemoryStream(unsupported)), "unsupported version rejection");
    await using (var valid = new MemoryStream())
    {
        await VPackWriter.WriteAsync(valid, PackageId.FromName("CorruptIndex"), VPackPlatform.Windows, [],
            [new VPackAssetSource(AssetId.New(), "Text", new byte[] { 1, 2, 3 })], new(VPackCompressionAlgorithm.None, 0, 1024));
        var bytes = valid.ToArray(); var indexOffset = BitConverter.ToInt64(bytes, 32);
        BitConverter.GetBytes(int.MaxValue).CopyTo(bytes, checked((int)indexOffset));
        await AssertThrowsAsync<InvalidDataException>(async () => await VPackReader.OpenAsync(new MemoryStream(bytes)), "corrupted index rejection");
    }
    await AssertThrowsAsync<FileNotFoundException>(async () => await VPackReader.OpenAsync(File.OpenRead(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".vpack"))), "missing package");
}

static void TestDependencyCycle(string parent)
{
    var root = Path.Combine(parent, "cycle"); Directory.CreateDirectory(Path.Combine(root, "Assets", "A")); Directory.CreateDirectory(Path.Combine(root, "Assets", "B"));
    File.WriteAllText(Path.Combine(root, "Assets", "A", "a.vpack"), "name: A\ndependencies: [B]\n");
    File.WriteAllText(Path.Combine(root, "Assets", "B", "b.vpack"), "name: B\ndependencies: [A]\n");
    AssertThrows<InvalidDataException>(() => VPackPipeline.DiscoverPackages(root), "package cycle detection");
}

static void AssertThrows<T>(Action action, string name) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException($"Test failed: {name}"); }
static async Task AssertThrowsAsync<T>(Func<Task> action, string name) where T : Exception { try { await action(); } catch (T) { return; } throw new InvalidOperationException($"Test failed: {name}"); }

file sealed class TestConfig : IYamlConfig { public int Value { get; init; } }
