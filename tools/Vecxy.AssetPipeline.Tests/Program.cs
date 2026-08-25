using Vecxy.AssetPipeline;
using Vecxy.Assets;

var root = Path.Combine(Path.GetTempPath(), "vecxy-asset-tests", Guid.NewGuid().ToString("N"));
try
{
    Assert(PackageVersion.Parse("1.10.2") > PackageVersion.Parse("1.9.9"), "semantic package version comparison");
    Assert(!PackageVersion.TryParse("1.0", out _) && !PackageVersion.TryParse("01.0.0", out _), "semantic package version validation");
    Assert(typeof(PackageDownloadProgress).GetProperty(nameof(PackageDownloadProgress.TotalBytes))!.PropertyType == typeof(long) &&
           typeof(RemotePackagePlatformEntry).GetProperty(nameof(RemotePackagePlatformEntry.Size))!.PropertyType == typeof(long), "multi-gigabyte-safe size types");
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
    Directory.CreateDirectory(Path.Combine(engineProject, "Assets", "SkyBox", "cubemap"));
    File.WriteAllText(Path.Combine(engineProject, "Vecxy.Engine.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
    File.WriteAllText(Path.Combine(engineProject, "Assets", "engine.vpack"), "name: Engine\nload: startup\ncompression: balanced\n");
    File.WriteAllText(Path.Combine(engineProject, "Assets", "Shaders", "sprite.glsl"), "#type vertex\n#type fragment");
    File.WriteAllText(Path.Combine(engineProject, "Assets", "Shaders", "Skybox.glsl"), "#type vertex\n#type fragment");
    File.WriteAllText(Path.Combine(engineProject, "Assets", "SkyBox", "Skybox.yaml"), "enabled: true");
    File.WriteAllBytes(Path.Combine(engineProject, "Assets", "SkyBox", "cubemap", "px.png"), [9, 8, 7]);
    File.WriteAllText(Path.Combine(root, "Game.csproj"),
        "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><ProjectReference Include=\"Engine/Vecxy.Engine/Vecxy.Engine.csproj\" /></ItemGroup></Project>");
    var withEngine = AssetPipeline.Scan(root);
    var engineAsset = withEngine.Assets.Single(x => x.Source == "Engine" && x.Path == "Shaders/sprite.glsl");
    Assert(engineAsset.Path == "Shaders/sprite.glsl", "engine asset scanned from project reference");
    var enginePackage = withEngine.Packages.Single(x => x.Name == "Engine");
    Assert(engineAsset.Package == enginePackage.Id, "engine asset assigned to Engine package");
    Assert(AssetPipeline.GenerateSource(withEngine).Contains("class Engine", StringComparison.Ordinal), "engine generated namespace");

    File.WriteAllText(Path.Combine(root, "Game.csproj"),
        "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><VecxyDisabledEngineFeatures>Skybox</VecxyDisabledEngineFeatures><VecxyDisabledEngineContent>DefaultSkybox</VecxyDisabledEngineContent></PropertyGroup><ItemGroup><ProjectReference Include=\"Engine/Vecxy.Engine/Vecxy.Engine.csproj\" /></ItemGroup></Project>");
    var withoutSkybox = AssetPipeline.Scan(root);
    Assert(withoutSkybox.Assets.All(x => x.Source != "Engine" ||
        (!x.Path.Equals("Shaders/Skybox.glsl", StringComparison.OrdinalIgnoreCase) &&
         !x.Path.StartsWith("SkyBox/", StringComparison.OrdinalIgnoreCase))),
        "project disables skybox renderer and default content without tombstones");

    File.WriteAllText(Path.Combine(root, "Game.csproj"),
        "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><VecxyDisabledEngineContent>DefaultSkybox</VecxyDisabledEngineContent></PropertyGroup><ItemGroup><ProjectReference Include=\"Engine/Vecxy.Engine/Vecxy.Engine.csproj\" /></ItemGroup></Project>");
    var customSkybox = AssetPipeline.Scan(root);
    Assert(customSkybox.Assets.Any(x => x.Source == "Engine" && x.Path.Equals("Shaders/Skybox.glsl", StringComparison.OrdinalIgnoreCase)) &&
           customSkybox.Assets.All(x => x.Source != "Engine" || !x.Path.StartsWith("SkyBox/", StringComparison.OrdinalIgnoreCase)),
        "custom skybox keeps renderer but excludes default content");

    await TestPackages(root);
    TestRemoteConfiguration(root);
    await TestBinaryFormat();
    await TestHttpTransport();
    await TestRemotePackages(root);
    TestDependencyCycle(root);

    Console.WriteLine("Vecxy.AssetPipeline tests passed.");
    return 0;
}
finally
{
    if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
}

static void TestRemoteConfiguration(string parent)
{
    var root = Path.Combine(parent, "remote-config"); Directory.CreateDirectory(Path.Combine(root, "Assets", "DLC"));
    var descriptor = Path.Combine(root, "Assets", "DLC", "dlc.vpack");
    File.WriteAllText(descriptor, "name: DLC\nversion: 2.3.4\nremote:\n  url: https://example.test/{platform}/{architecture}/{name}-{version}.vpack\n  cache: session\n  update: always\n  integrity: sha256\n  size: 5000000000\n  sha256: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n");
    var package = VPackPipeline.DiscoverPackages(root).Single(x => x.Name == "DLC");
    var remote = package.Remote ?? throw new InvalidOperationException("Test failed: direct remote YAML configuration");
    Assert(remote.Cache == PackageCacheMode.Session && remote.Update == PackageUpdatePolicy.Always && remote.Size == 5_000_000_000L, "direct remote YAML policies");
    Assert(VPackPipeline.ResolveUrlTemplate(remote.Url!, package.Name, package.Version, VPackPlatform.Android, "arm64") ==
           "https://example.test/android/arm64/dlc-2.3.4.vpack", "URL placeholder resolution");
    File.WriteAllText(descriptor, "name: DLC\nremote:\n  url: https://example.test/{unknown}/dlc.vpack\n  cache: none\n  update: manual\n");
    AssertThrows<InvalidDataException>(() => VPackPipeline.DiscoverPackages(root), "unknown URL placeholder rejection");
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
    var engineProject = Path.Combine(root, "Engine", "Vecxy.Engine");
    Directory.CreateDirectory(Path.Combine(engineProject, "Assets", "Shaders"));
    File.WriteAllText(Path.Combine(engineProject, "Vecxy.Engine.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
    File.WriteAllText(Path.Combine(engineProject, "Assets", "engine.vpack"), "name: Engine\nload: startup\ncompression: balanced\n");
    File.WriteAllText(Path.Combine(engineProject, "Assets", "Shaders", "engine.glsl"), "#type vertex\n#type fragment");
    File.WriteAllText(Path.Combine(root, "Game.csproj"),
        "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><ProjectReference Include=\"Engine/Vecxy.Engine/Vecxy.Engine.csproj\" /></ItemGroup></Project>");
    File.WriteAllText(Path.Combine(root, "Assets", "game.vpack"), "name: Game\ncompression: maximum\nplatforms:\n  android:\n    compression: fast\n");
    File.WriteAllText(Path.Combine(root, "Assets", "player.txt"), "player");
    File.WriteAllText(Path.Combine(root, "Assets", "Shared", "shared.vpack"), "name: Shared\nload: startup\ncompression: balanced\nplatforms:\n  android:\n    compression:\n      algorithm: lz4\n      block-size: 256kb\n");
    File.WriteAllText(Path.Combine(root, "Assets", "Shared", "common.txt"), new string('s', 4096));
    File.WriteAllText(Path.Combine(root, "Assets", "DLC", "dlc.vpack"), "name: DLC\nversion: 1.2.0\ndependencies:\n  - Shared\nremote:\n  manifest: https://example.test/packages.json\n  cache: persistent\n  update: check\n  integrity: sha256\n");
    File.WriteAllText(Path.Combine(root, "Assets", "DLC", "links.xml"), "<asset>Shared/common.txt</asset>");
    File.WriteAllText(Path.Combine(root, "Assets", "DLC", "dlc.txt"), "dlc");
    File.WriteAllText(Path.Combine(root, "Assets", "DLC", "settings.yaml"), "value: 42");
    File.WriteAllText(Path.Combine(root, "Assets", "DLC", "Cars", "cars.vpack"), "name: Cars\ndependencies:\n  - Shared\n");
    File.WriteAllText(Path.Combine(root, "Assets", "DLC", "Cars", "sedan.txt"), "sedan");
    File.WriteAllText(Path.Combine(root, "Assets", "Harbor", "harbor.vpack"), "name: Harbor\ndependencies:\n  - Shared\ncompression:\n  algorithm: zstd\n  level: 5\n  block-size: 512kb\n");
    File.WriteAllText(Path.Combine(root, "Assets", "Harbor", "harbor.txt"), "harbor");

    var packages = VPackPipeline.DiscoverPackages(root);
    Assert(packages.Count == 6, "implicit, engine, and explicit package discovery");
    var game = packages.Single(x => x.Id == PackageId.Game);
    Assert(game.DescriptorPath == "game.vpack" && game.Load == PackageLoadMode.Startup, "root Game descriptor");
    Assert(VPackPipeline.ResolveCompression(game, VPackPlatform.Android).Algorithm == VPackCompressionAlgorithm.Lz4, "root Game platform configuration");
    var manifest = AssetPipeline.Scan(root);
    Assert(manifest.Assets.Single(x => x.Path == "player.txt").Package == PackageId.Game, "implicit Game membership");
    var dlc = packages.Single(x => x.Name == "DLC");
    Assert(dlc.Version == PackageVersion.Parse("1.2.0") && dlc.Remote?.Update == PackageUpdatePolicy.Check && dlc.Remote.Cache == PackageCacheMode.Persistent, "remote YAML configuration");
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
    Assert(generated.Contains("EnsureLoadedAsync", StringComparison.Ordinal) && generated.Contains("CheckForUpdatesAsync", StringComparison.Ordinal), "generated remote package API");
    Assert(generated.Contains("TextHandle Dlc", StringComparison.Ordinal), "generated packaged asset handle");

    var builds = await VPackPipeline.BuildAsync(root, VPackPlatform.Windows);
    var output = Path.Combine(root, "Build", "Windows");
    Assert(builds.Count == 6 && File.Exists(Path.Combine(output, "packages.manifest")), "platform package build output");
    Assert(File.Exists(Path.Combine(output, "Remote", "dlc.vpack")), "remote package distribution output");
    Assert(File.Exists(Path.Combine(output, "engine.vpack")), "engine package output");
    var bundledBuildManifest = System.Text.Json.JsonSerializer.Deserialize<VPackBuildManifest>(
        File.ReadAllText(Path.Combine(output, "packages.manifest")), AssetManifest.SerializerOptions)!;
    Assert(bundledBuildManifest.Packages.All(x => x.Id != dlc.Id), "remote package excluded from application manifest");
    var remoteBuildManifest = RemotePackageManifest.Parse(File.ReadAllText(Path.Combine(output, "packages.json")));
    Assert(remoteBuildManifest.Packages["DLC"].Version == PackageVersion.Parse("1.2.0") &&
           remoteBuildManifest.Packages["DLC"].Platforms["windows"].Size > 0, "remote manifest generation from build output");
    AssertThrows<RemoteManifestException>(() => RemotePackageManifest.Parse("{\"version\":99,\"packages\":{}}"), "unknown remote manifest version rejection");
    await VPackPipeline.BuildAsync(root, VPackPlatform.Linux);
    await VPackPipeline.BuildAsync(root, VPackPlatform.Android);
    Assert(File.Exists(Path.Combine(root, "Build", "Linux", "game.vpack")) &&
           File.Exists(Path.Combine(root, "Build", "Android", "game.vpack")), "per-platform build outputs");
    await using (var reader = await VPackReader.OpenAsync(File.OpenRead(Path.Combine(output, "Remote", "dlc.vpack"))))
    {
        var asset = manifest.Assets.Single(x => x.Path.EndsWith("links.xml"));
        Assert(System.Text.Encoding.UTF8.GetString((await reader.ReadAssetAsync(new AssetId(asset.Id))).Span).Contains("Shared/common.txt"), "lookup by AssetId");
    }

    var module = new AssetsModule(new AssetsModule.Options { AssetsDirectory = Path.Combine(root, "Assets"), PackagesDirectory = output, HotReloadEnabled = false });
    module.OnInitialize();
    var carsAsset = manifest.Assets.Single(x => x.Path.EndsWith("sedan.txt"));
    AssertThrows<AssetPackageNotLoadedException>(() => module.Load<TextAsset>(new AssetId(carsAsset.Id)), "unloaded package diagnostic");
    await using (var lease = await module.LoadPackageAsync(cars.Id))
    {
        Assert(lease.Package.IsLoaded && module.GetPackage(shared.Id).IsLoaded, "dependency package loading");
        using var loaded = module.Load<TextAsset>(new AssetId(carsAsset.Id));
        Assert(loaded.Value.Content == "sedan", "packaged asset loading");
    }
    Assert(!module.GetPackage(cars.Id).IsLoaded, "package reference-counted unload");
    module.Dispose();

    var runtimeAssets = Path.Combine(root, "RuntimeAssets");
    var packagedModule = new AssetsModule(new AssetsModule.Options
    {
        AssetsDirectory = runtimeAssets,
        PackagesDirectory = output,
        HotReloadEnabled = true
    });
    packagedModule.OnInitialize();
    Assert(!Directory.Exists(runtimeAssets), "packaged runtime does not create loose Assets directory");
    packagedModule.Dispose();

    File.WriteAllText(Path.Combine(root, "Assets", "DLC", "dlc.vpack"), "name: DLC\n");
    var invalid = AssetPipeline.Scan(root);
    Assert(VPackPipeline.ValidatePackageDependencies(invalid).Any(x => x.Contains("VXY2104", StringComparison.Ordinal)), "undeclared cross-package dependency detection");
}

static async Task TestRemotePackages(string parent)
{
    var root = Path.Combine(parent, "remote-integration"); Directory.CreateDirectory(root);
    var packageId = PackageId.FromName("DLC"); var assetId = AssetId.New();
    var v1 = await BuildPackage(packageId, assetId, "remote-v1");
    var transport = new FakeRemoteTransport(v1.Bytes) { FailOnceAfterBytes = Math.Max(1, v1.Bytes.Length / 2) };
    transport.ManifestJson = RemoteManifest("1.0.0", packageId, v1.Bytes.Length, v1.Hash);
    var manifest = new AssetManifest
    {
        Assets = [new AssetManifestEntry { Id = assetId.Value, Source = "Game", Path = "DLC/dlc.txt", Type = "Text", Name = "Dlc", Package = packageId }],
        Packages = [new AssetPackageManifestEntry
        {
            Id = packageId, Name = "DLC", Load = PackageLoadMode.OnDemand, Version = PackageVersion.Parse("1.0.0"),
            Remote = new VPackRemoteConfig { Manifest = "https://example.test/packages.json", Cache = PackageCacheMode.Persistent, Update = PackageUpdatePolicy.Check }
        }]
    };
    File.WriteAllText(Path.Combine(root, "Assets.manifest"), System.Text.Json.JsonSerializer.Serialize(manifest, AssetManifest.SerializerOptions));
    File.WriteAllText(Path.Combine(root, "packages.manifest"), System.Text.Json.JsonSerializer.Serialize(new VPackBuildManifest
        { FormatVersion = VPackFormat.Version, Platform = VPackPlatform.Windows }, AssetManifest.SerializerOptions));
    var module = new AssetsModule(new AssetsModule.Options
    {
        AssetsDirectory = Path.Combine(root, "Assets"), PackagesDirectory = root,
        PackageCacheDirectory = Path.Combine(root, "Cache"), ApplicationId = "Vecxy.Remote.Tests",
        RemoteTransport = transport, HotReloadEnabled = false
    });
    module.OnInitialize(); var package = module.GetPackage(packageId);
    await AssertThrowsAsync<PackageDownloadException>(async () => await package.EnsureLoadedAsync(), "interrupted remote download");
    var progress = new List<PackageDownloadProgress>();
    var first = package.EnsureLoadedAsync(new InlineProgress<PackageDownloadProgress>(progress.Add)).AsTask();
    var second = package.EnsureLoadedAsync().AsTask();
    var leases = await Task.WhenAll(first, second);
    Assert(transport.DownloadCalls == 2 && transport.LastResumeOffset > 0, "resumable and deduplicated package download");
    Assert(progress.Last().Fraction == 1 && progress.Last().TotalBytes == v1.Bytes.Length, "download progress final state");
    using (var loaded = module.Load<TextAsset>(assetId)) Assert(loaded.Value.Content == "remote-v1", "downloaded VPack loads through AssetManager");

    var v11 = await BuildPackage(packageId, assetId, "remote-v1.1"); transport.Bytes = v11.Bytes;
    transport.ManifestJson = RemoteManifest("1.1.0", packageId, v11.Bytes.Length, v11.Hash);
    var status = await package.CheckForUpdatesAsync();
    Assert(status.IsUpdateAvailable && status.LocalVersion == PackageVersion.Parse("1.0.0") && status.RemoteVersion == PackageVersion.Parse("1.1.0"), "remote update comparison");
    await package.DownloadUpdateAsync();
    Assert(package.IsLoaded, "old package remains loaded during atomic update");
    foreach (var lease in leases) await lease.DisposeAsync();
    await using (var updated = await package.LoadAsync())
    using (var loaded = module.Load<TextAsset>(assetId)) Assert(loaded.Value.Content == "remote-v1.1", "atomic package update activation");

    transport.ManifestJson = RemoteManifest("1.2.0", packageId, v11.Bytes.Length, new string('0', 64));
    await AssertThrowsAsync<PackageIntegrityException>(async () => await package.DownloadUpdateAsync(), "remote SHA-256 rejection");
    var preserved = await package.GetRemoteStatusAsync();
    Assert(preserved.LocalVersion == PackageVersion.Parse("1.1.0"), "failed update preserves active cache version");
    var cacheInfo = await package.GetCacheInfoAsync(); Assert(cacheInfo.CachedSize == v11.Bytes.Length && cacheInfo.Versions == 1, "cache info and old version cleanup");
    transport.Offline = true;
    await using (var offlineLease = await package.EnsureLoadedAsync())
    using (var loaded = module.Load<TextAsset>(assetId)) Assert(loaded.Value.Content == "remote-v1.1", "offline cached package loading");
    await package.RemoveCachedAsync();
    await AssertThrowsAsync<RemotePackageException>(async () => await package.EnsureLoadedAsync(), "offline without local package");
    module.Dispose();
}

static async Task TestHttpTransport()
{
    var bytes = System.Text.Encoding.UTF8.GetBytes("resumable-http-payload");
    using var acceptingClient = new HttpClient(new StubHttpHandler((request, _) =>
    {
        var offset = request.Headers.Range?.Ranges.Single().From ?? 0;
        var response = new HttpResponseMessage(offset > 0 ? System.Net.HttpStatusCode.PartialContent : System.Net.HttpStatusCode.OK)
            { Content = new StreamContent(new MemoryStream(bytes[(int)offset..])) };
        if (offset > 0) response.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(offset, bytes.Length - 1, bytes.Length);
        return Task.FromResult(response);
    }));
    using var transport = new HttpRemotePackageTransport(acceptingClient);
    await using var resumed = new MemoryStream(); await resumed.WriteAsync(bytes.AsMemory(0, 5));
    var result = await transport.DownloadAsync(new Uri("https://example.test/package"), resumed, 5);
    Assert(result.ResumedBytes == 5 && resumed.ToArray().SequenceEqual(bytes), "HTTP range resume");

    using var rejectingClient = new HttpClient(new StubHttpHandler((_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        { Content = new StreamContent(new MemoryStream(bytes)) })));
    using var rejectingTransport = new HttpRemotePackageTransport(rejectingClient);
    await using var restarted = new MemoryStream(); await restarted.WriteAsync(new byte[8]);
    var restartedResult = await rejectingTransport.DownloadAsync(new Uri("https://example.test/package"), restarted, 8);
    Assert(restartedResult.ResumedBytes == 0 && restartedResult.TotalBytes == bytes.Length && restarted.ToArray().SequenceEqual(bytes), "server range rejection and unknown length recovery");

    using var cancellingClient = new HttpClient(new StubHttpHandler(async (_, token) =>
    { await Task.Delay(Timeout.InfiniteTimeSpan, token); return new HttpResponseMessage(); }));
    using var cancellingTransport = new HttpRemotePackageTransport(cancellingClient);
    using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
    await AssertThrowsAsync<OperationCanceledException>(async () =>
        await cancellingTransport.DownloadAsync(new Uri("https://example.test/package"), new MemoryStream(), 0, cancellationToken: cancellation.Token), "HTTP cancellation");
}

static async Task<(byte[] Bytes, string Hash)> BuildPackage(PackageId package, AssetId asset, string text)
{
    await using var stream = new MemoryStream();
    await VPackWriter.WriteAsync(stream, package, VPackPlatform.Windows, [],
        [new VPackAssetSource(asset, "Text", System.Text.Encoding.UTF8.GetBytes(text))],
        new(VPackCompressionAlgorithm.None, 0, 4096));
    var bytes = stream.ToArray();
    return (bytes, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant());
}

static string RemoteManifest(string version, PackageId id, long size, string hash) =>
    System.Text.Json.JsonSerializer.Serialize(new RemotePackageManifest
    {
        Version = RemotePackageManifest.CurrentVersion,
        Packages = new Dictionary<string, RemotePackageManifestPackage>(StringComparer.OrdinalIgnoreCase)
        {
            ["DLC"] = new()
            {
                Id = id, Version = PackageVersion.Parse(version),
                Platforms = new Dictionary<string, RemotePackagePlatformEntry>(StringComparer.OrdinalIgnoreCase)
                {
                    ["windows"] = new() { Url = "https://example.test/dlc.vpack", Size = size, Sha256 = hash }
                }
            }
        }
    }, AssetManifest.SerializerOptions);

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

file sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}

file sealed class FakeRemoteTransport(byte[] bytes) : IRemotePackageTransport
{
    public byte[] Bytes { get; set; } = bytes;
    public string ManifestJson { get; set; } = "";
    public int DownloadCalls { get; private set; }
    public int FailOnceAfterBytes { get; set; }
    public long LastResumeOffset { get; private set; }
    public bool Offline { get; set; }

    public Task<string> GetStringAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Offline) throw new RemoteManifestException("Offline test transport.");
        return Task.FromResult(ManifestJson);
    }

    public async Task<RemoteDownloadResult> DownloadAsync(Uri uri, Stream destination, long resumeOffset,
        IProgress<PackageDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); if (Offline) throw new PackageDownloadException("Offline test transport.");
        DownloadCalls++; LastResumeOffset = resumeOffset;
        destination.Position = resumeOffset; var remaining = Bytes.AsMemory(checked((int)resumeOffset));
        if (FailOnceAfterBytes > 0)
        {
            var count = Math.Min(FailOnceAfterBytes, remaining.Length);
            await destination.WriteAsync(remaining[..count], cancellationToken); await destination.FlushAsync(cancellationToken);
            FailOnceAfterBytes = 0; throw new PackageDownloadException("Simulated interrupted download.");
        }
        await destination.WriteAsync(remaining, cancellationToken); await destination.FlushAsync(cancellationToken);
        progress?.Report(new(Bytes.Length, Bytes.Length, 1, Bytes.Length, TimeSpan.Zero, resumeOffset));
        return new(Bytes.Length, resumeOffset, "test", null);
    }
}

file sealed class StubHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request, cancellationToken);
}
