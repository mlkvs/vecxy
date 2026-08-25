using System.Text.Json;
using System.Text.RegularExpressions;
using Vecxy.Assets;
using YamlDotNet.RepresentationModel;

namespace Vecxy.AssetPipeline;

public sealed record VPackPackageDefinition(
    PackageId Id, string Name, string Root, string? DescriptorPath, PackageLoadMode Load,
    string CompressionPreset, VPackCompressionSettings? AdvancedCompression,
    IReadOnlyList<string> Dependencies, IReadOnlyDictionary<VPackPlatform, VPackPlatformOverride> Platforms);

public sealed record VPackPlatformOverride(string? Preset, VPackCompressionSettings? Advanced);
public sealed record VPackPackageBuild(PackageId Id, string Name, string File, VPackBuildResult Statistics);

public static partial class VPackPipeline
{
    public static IReadOnlyList<VPackPackageDefinition> DiscoverPackages(string projectDirectory)
    {
        var assets = Path.Combine(Path.GetFullPath(projectDirectory), "Assets");
        Directory.CreateDirectory(assets);
        var packages = new List<VPackPackageDefinition>
        {
            new(PackageId.Game, "Game", "", null, PackageLoadMode.Startup, "balanced", null, [],
                new Dictionary<VPackPlatform, VPackPlatformOverride>())
        };
        foreach (var path in Directory.EnumerateFiles(assets, "*.vpack", SearchOption.AllDirectories).Order())
            packages.Add(ParseDescriptor(assets, path));
        ValidatePackages(packages);
        return packages;
    }

    public static VPackPackageDefinition ResolvePackage(IReadOnlyList<VPackPackageDefinition> packages, string assetPath)
    {
        var normalized = Normalize(assetPath);
        return packages.Where(x => x.Root.Length == 0 || normalized.StartsWith(x.Root + "/", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Root.Length).First();
    }

    public static VPackCompressionSettings ResolveCompression(VPackPackageDefinition package, VPackPlatform platform)
    {
        var preset = package.CompressionPreset;
        var advanced = package.AdvancedCompression;
        if (package.Platforms.TryGetValue(platform, out var platformOverride))
        {
            preset = platformOverride.Preset ?? preset;
            advanced = platformOverride.Advanced ?? (platformOverride.Preset is null ? advanced : null);
        }
        return advanced ?? VPackPlatformProfiles.Resolve(platform, preset);
    }

    public static IReadOnlyList<string> ValidatePackageDependencies(AssetManifest manifest)
    {
        var errors = new List<string>();
        var packages = manifest.Packages.ToDictionary(x => x.Id);
        foreach (var asset in manifest.Assets)
        foreach (var dependencyId in asset.Dependencies)
        {
            var dependency = manifest.Assets.FirstOrDefault(x => x.Id == dependencyId);
            if (dependency is null || dependency.Package == asset.Package) continue;
            if (!packages.TryGetValue(asset.Package, out var owner) || !packages.TryGetValue(dependency.Package, out var target)) continue;
            if (!owner.Dependencies.Contains(target.Id))
                errors.Add($"VXY2104 Package dependency violation\nPackage: {owner.Name}\nAsset: {asset.Path}\nReferences asset: {dependency.Path}\nPackage \"{owner.Name}\" does not declare dependency on \"{target.Name}\".");
            if (owner.Id == PackageId.Game && target.Load == PackageLoadMode.OnDemand)
                errors.Add($"VXY2105 Startup package Game cannot depend on on-demand package \"{target.Name}\" ({asset.Path} -> {dependency.Path}).");
        }
        return errors;
    }

    public static async Task<IReadOnlyList<VPackPackageBuild>> BuildAsync(string projectDirectory, VPackPlatform platform, CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(projectDirectory);
        var manifest = AssetPipeline.Scan(root);
        var packageDefinitions = DiscoverPackages(root).ToDictionary(x => x.Id);
        var validation = ValidatePackageDependencies(manifest);
        if (validation.Count > 0) throw new InvalidDataException(string.Join(Environment.NewLine + Environment.NewLine, validation));
        var platformName = platform.ToString();
        var output = Path.Combine(root, "Build", platformName, "Packages");
        Directory.CreateDirectory(output);
        var engineAssets = AssetPipeline.FindEngineAssetsDirectory(root);
        var builds = new List<VPackPackageBuild>();
        foreach (var definition in packageDefinitions.Values.OrderBy(x => x.Id.Value))
        {
            var sources = manifest.Assets.Where(x => x.Package == definition.Id && !string.IsNullOrEmpty(x.Hash) &&
                !(platform == VPackPlatform.Android && string.Equals(x.Source, "Game", StringComparison.OrdinalIgnoreCase) &&
                  string.Equals(x.Path, "Configs/Build.yaml", StringComparison.OrdinalIgnoreCase))).Select(entry =>
            {
                var baseDirectory = string.Equals(entry.Source, "Engine", StringComparison.OrdinalIgnoreCase) ? engineAssets : Path.Combine(root, "Assets");
                if (baseDirectory is null) throw new DirectoryNotFoundException("Engine Assets directory was not found.");
                var bytes = File.ReadAllBytes(Path.Combine(baseDirectory, entry.Path.Replace('/', Path.DirectorySeparatorChar)));
                return new VPackAssetSource(new AssetId(entry.Id), entry.Type, bytes, IsUsuallyCompressed(entry.Path));
            }).ToArray();
            var fileName = definition.Name.ToLowerInvariant() + ".vpack";
            var path = Path.Combine(output, fileName);
            await using var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 128 * 1024, useAsync: true);
            var result = await VPackWriter.WriteAsync(stream, definition.Id, platform,
                definition.Dependencies.Select(PackageId.FromName).ToArray(), sources, ResolveCompression(definition, platform), cancellationToken);
            builds.Add(new VPackPackageBuild(definition.Id, definition.Name, fileName, result));
        }
        var packageManifest = new VPackBuildManifest
        {
            FormatVersion = VPackFormat.Version, Platform = platform,
            Packages = builds.Select(x => new VPackBuildManifestEntry { Id = x.Id, Name = x.Name, File = x.File }).ToList()
        };
        await File.WriteAllTextAsync(Path.Combine(output, "packages.manifest"), JsonSerializer.Serialize(packageManifest, AssetManifest.SerializerOptions), cancellationToken);
        return builds;
    }

    private static VPackPackageDefinition ParseDescriptor(string assetsRoot, string path)
    {
        using var input = File.OpenText(path); var yaml = new YamlStream();
        try { yaml.Load(input); } catch (Exception e) { throw new InvalidDataException($"Malformed VPack descriptor '{path}': {e.Message}", e); }
        if (yaml.Documents.Count != 1 || yaml.Documents[0].RootNode is not YamlMappingNode root) throw new InvalidDataException($"VPack descriptor must contain one mapping: {path}");
        ValidateKeys(root, path, "name", "load", "compression", "dependencies", "platforms");
        var name = Scalar(root, "name") ?? throw new InvalidDataException($"VPack descriptor is missing 'name': {path}");
        var load = (Scalar(root, "load") ?? "on-demand").ToLowerInvariant() switch { "startup" => PackageLoadMode.Startup, "on-demand" => PackageLoadMode.OnDemand, var x => throw new InvalidDataException($"Invalid load value '{x}' in {path}.") };
        var (preset, advanced) = Compression(root, "compression", "balanced", path, null);
        var dependencies = Sequence(root, "dependencies");
        var platforms = new Dictionary<VPackPlatform, VPackPlatformOverride>();
        if (Node(root, "platforms") is YamlMappingNode platformMap)
        foreach (var pair in platformMap.Children)
        {
            var platform = ParsePlatform(((YamlScalarNode)pair.Key).Value, path);
            if (pair.Value is not YamlMappingNode values) throw new InvalidDataException($"Platform override must be a mapping in {path}.");
            ValidateKeys(values, path, "compression");
            var resolved = Compression(values, "compression", null, path, platform);
            platforms.Add(platform, new VPackPlatformOverride(resolved.Preset, resolved.Advanced));
        }
        var rootPath = Normalize(Path.GetRelativePath(assetsRoot, Path.GetDirectoryName(path)!));
        if (rootPath == ".") rootPath = "";
        return new(PackageId.FromName(name), name, rootPath, Normalize(Path.GetRelativePath(assetsRoot, path)), load, preset!, advanced, dependencies, platforms);
    }

    private static (string? Preset, VPackCompressionSettings? Advanced) Compression(YamlMappingNode root, string key, string? fallback, string path, VPackPlatform? platform)
    {
        var node = Node(root, key); if (node is null) return (fallback, null);
        if (node is YamlScalarNode scalar) { ValidatePreset(scalar.Value, path); return (scalar.Value!.ToLowerInvariant(), null); }
        if (node is not YamlMappingNode map) throw new InvalidDataException($"Invalid compression in {path}.");
        ValidateKeys(map, path, "algorithm", "level", "block-size");
        var algorithm = (Scalar(map, "algorithm") ?? throw new InvalidDataException($"Compression algorithm is required in {path}.")).ToLowerInvariant() switch
        { "none" => VPackCompressionAlgorithm.None, "lz4" => VPackCompressionAlgorithm.Lz4, "zstd" => VPackCompressionAlgorithm.Zstd, var x => throw new InvalidDataException($"Unsupported compression algorithm '{x}' in {path}.") };
        var level = int.TryParse(Scalar(map, "level"), out var parsed) ? parsed : algorithm == VPackCompressionAlgorithm.Zstd ? 3 : 0;
        if (algorithm == VPackCompressionAlgorithm.Zstd && level is < -5 or > 22) throw new InvalidDataException($"Zstd level must be between -5 and 22 in {path}.");
        var block = ParseSize(Scalar(map, "block-size")) ?? VPackPlatformProfiles.DefaultBlockSize(platform ?? VPackPlatform.Windows);
        return (null, new VPackCompressionSettings(algorithm, level, block));
    }

    private static void ValidatePackages(IReadOnlyList<VPackPackageDefinition> packages)
    {
        foreach (var package in packages) if (!NameRegex().IsMatch(package.Name)) throw new InvalidDataException($"Invalid VPack package name '{package.Name}' in {package.DescriptorPath}. Use a C# identifier.");
        var duplicate = packages.GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(x => x.Count() > 1); if (duplicate is not null) throw new InvalidDataException($"Duplicate VPack package name '{duplicate.Key}'.");
        var ids = packages.GroupBy(x => x.Id).FirstOrDefault(x => x.Count() > 1); if (ids is not null) throw new InvalidDataException($"Duplicate VPack package ID '{ids.Key}'.");
        var roots = packages.Where(x => x.DescriptorPath is not null).GroupBy(x => x.Root, StringComparer.OrdinalIgnoreCase).FirstOrDefault(x => x.Count() > 1); if (roots is not null) throw new InvalidDataException($"Multiple VPack descriptors define the same package root '{roots.Key}'.");
        var byName = packages.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var p in packages) foreach (var d in p.Dependencies) if (!byName.ContainsKey(d)) throw new InvalidDataException($"Package '{p.Name}' depends on missing package '{d}'.");
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Visit(VPackPackageDefinition p) { if (visited.Contains(p.Name)) return; if (!visiting.Add(p.Name)) throw new InvalidDataException($"Circular VPack dependency detected at '{p.Name}'."); foreach(var d in p.Dependencies) Visit(byName[d]); visiting.Remove(p.Name); visited.Add(p.Name); }
        foreach (var p in packages) Visit(p);
    }
    private static void ValidatePreset(string? preset,string path) { if (preset?.ToLowerInvariant() is not ("none" or "fast" or "balanced" or "maximum")) throw new InvalidDataException($"Invalid compression preset '{preset}' in {path}."); }
    private static int? ParseSize(string? value) { if(value is null)return null; var match=SizeRegex().Match(value); if(!match.Success)throw new InvalidDataException($"Invalid block-size '{value}'."); var amount=int.Parse(match.Groups[1].Value); return checked(amount * (match.Groups[2].Value.ToLowerInvariant() switch { "kb"=>1024,"mb"=>1024*1024,_=>1 })); }
    private static VPackPlatform ParsePlatform(string? value,string path)=>value?.ToLowerInvariant() switch {"windows"=>VPackPlatform.Windows,"linux"=>VPackPlatform.Linux,"android"=>VPackPlatform.Android,_=>throw new InvalidDataException($"Invalid platform '{value}' in {path}.")};
    private static YamlNode? Node(YamlMappingNode map,string key)=>map.Children.TryGetValue(new YamlScalarNode(key),out var node)?node:null;
    private static string? Scalar(YamlMappingNode map,string key)=>Node(map,key) is YamlScalarNode scalar?scalar.Value:null;
    private static string[] Sequence(YamlMappingNode map,string key)
    { var node=Node(map,key); if(node is null)return []; if(node is not YamlSequenceNode sequence)throw new InvalidDataException($"'{key}' must be a sequence."); return sequence.Children.Select(x=>x is YamlScalarNode scalar?scalar.Value??"":throw new InvalidDataException($"'{key}' entries must be strings.")).ToArray(); }
    private static void ValidateKeys(YamlMappingNode map,string path,params string[] allowed)
    { var set=allowed.ToHashSet(StringComparer.Ordinal); foreach(var key in map.Children.Keys){if(key is not YamlScalarNode scalar || scalar.Value is null || !set.Contains(scalar.Value))throw new InvalidDataException($"Unsupported VPack setting '{(key as YamlScalarNode)?.Value}' in {path}.");} }
    private static string Normalize(string path)=>path.Replace('\\','/');
    private static bool IsUsuallyCompressed(string path)=>Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp" or ".ogg" or ".mp3" or ".mp4";
    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")] private static partial Regex NameRegex();
    [GeneratedRegex("^([1-9][0-9]*)(b|kb|mb)$",RegexOptions.IgnoreCase)] private static partial Regex SizeRegex();
}

public static class VPackPlatformProfiles
{
    public static int DefaultBlockSize(VPackPlatform platform) => platform == VPackPlatform.Android ? 256 * 1024 : 512 * 1024;
    public static VPackCompressionSettings Resolve(VPackPlatform platform, string preset) => (platform, preset.ToLowerInvariant()) switch
    {
        (_, "none") => new(VPackCompressionAlgorithm.None, 0, DefaultBlockSize(platform)),
        (_, "fast") => new(VPackCompressionAlgorithm.Lz4, 0, DefaultBlockSize(platform)),
        (VPackPlatform.Android, "balanced") => new(VPackCompressionAlgorithm.Zstd, 1, DefaultBlockSize(platform)),
        (VPackPlatform.Android, "maximum") => new(VPackCompressionAlgorithm.Zstd, 6, DefaultBlockSize(platform)),
        (_, "balanced") => new(VPackCompressionAlgorithm.Zstd, 3, DefaultBlockSize(platform)),
        (_, "maximum") => new(VPackCompressionAlgorithm.Zstd, 12, DefaultBlockSize(platform)),
        _ => throw new InvalidDataException($"Unknown compression preset '{preset}'.")
    };
}
