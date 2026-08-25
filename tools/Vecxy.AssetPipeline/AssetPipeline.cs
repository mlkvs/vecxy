using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Vecxy.Assets;

namespace Vecxy.AssetPipeline;

public static partial class AssetPipeline
{
    private static readonly Dictionary<string, (string Type, string Category)> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = ("Texture2D", "Textures"), [".jpg"] = ("Texture2D", "Textures"),
        [".jpeg"] = ("Texture2D", "Textures"), [".bmp"] = ("Texture2D", "Textures"),
        [".tga"] = ("Texture2D", "Textures"), [".ppm"] = ("Texture2D", "Textures"),
        [".glb"] = ("Model", "Models"), [".wav"] = ("Sound", "Sounds"),
        [".mp3"] = ("Sound", "Sounds"), [".ogg"] = ("Sound", "Sounds"),
        [".material"] = ("Material", "Materials"),
        [".yaml"] = ("Config", "Configs"), [".yml"] = ("Config", "Configs"),
        [".glsl"] = ("Shader", "Shaders"), [".vert"] = ("Text", "Shaders"),
        [".frag"] = ("Text", "Shaders"), [".input"] = ("Input", "Inputs"),
        [".txt"] = ("Text", "Texts"), [".postfx"] = ("Text", "PostProcessing"),
        [".xml"] = ("Asset", "UI"), [".css"] = ("Asset", "UI"),
        [".atlas"] = ("Asset", "Atlases"), [".ttf"] = ("Asset", "Fonts"),
        [".otf"] = ("Asset", "Fonts")
    };

    public static AssetManifest Scan(string projectDirectory)
    {
        var root = Path.GetFullPath(projectDirectory);
        var assetsDirectory = Path.Combine(root, "Assets");
        Directory.CreateDirectory(assetsDirectory);
        var manifestPath = Path.Combine(root, "Assets.manifest");
        var previous = File.Exists(manifestPath) ? AssetManifest.Load(manifestPath) : new AssetManifest();
        var oldByPath = previous.Assets.ToDictionary(x => SourceKey(x.Source, x.Path), StringComparer.OrdinalIgnoreCase);
        var oldByHash = previous.Assets.Where(x => !string.IsNullOrEmpty(x.Hash))
            .GroupBy(x => x.Hash!, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.Ordinal);
        var claimed = new HashSet<Guid>();
        var entries = new List<AssetManifestEntry>();
        var packages = VPackPipeline.DiscoverPackages(root);
        ScanDirectory("Game", assetsDirectory, entries, oldByPath, oldByHash, claimed, packages.Where(x => x.Source == "Game").ToArray());
        var engineAssetsDirectory = FindEngineAssetsDirectory(root);
        if (engineAssetsDirectory is not null)
            ScanDirectory("Engine", engineAssetsDirectory, entries, oldByPath, oldByHash, claimed, packages.Where(x => x.Source == "Engine").ToArray());
        // Keep tombstones so generated symbols and diagnostics survive deletion. A later
        // file with the same content claims the old ID and therefore removes the tombstone.
        entries.AddRange(previous.Assets.Where(x => !claimed.Contains(x.Id)));
        TrackAssetDependencies(assetsDirectory, engineAssetsDirectory, entries);
        var manifest = new AssetManifest
        {
            Assets = entries,
            Packages = packages.Select(x => new AssetPackageManifestEntry
            {
                Id = x.Id, Name = x.Name, Descriptor = x.DescriptorPath,
                Load = x.Load, Dependencies = x.Dependencies.Select(PackageId.FromName).ToList(),
                Version = x.Version, Remote = x.Remote
            }).ToList()
        };
        SaveManifest(manifestPath, manifest);
        return manifest;
    }

    private static void ScanDirectory(
        string source,
        string assetsDirectory,
        ICollection<AssetManifestEntry> entries,
        IReadOnlyDictionary<string, AssetManifestEntry> oldByPath,
        IReadOnlyDictionary<string, AssetManifestEntry[]> oldByHash,
        ISet<Guid> claimed,
        IReadOnlyList<VPackPackageDefinition>? packages)
    {
        foreach (var file in Directory.EnumerateFiles(assetsDirectory, "*", SearchOption.AllDirectories).Order())
        {
            if (string.Equals(Path.GetExtension(file), ".vpack", StringComparison.OrdinalIgnoreCase)) continue;
            var kind = Classify(file);
            var relative = Normalize(Path.GetRelativePath(assetsDirectory, file));
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
            AssetManifestEntry? match = oldByPath.GetValueOrDefault(SourceKey(source, relative));
            if (match is null && oldByHash.TryGetValue(hash, out var candidates))
                match = candidates.FirstOrDefault(x =>
                    string.Equals(x.Source, source, StringComparison.OrdinalIgnoreCase) &&
                    !claimed.Contains(x.Id));
            var id = match?.Id ?? Guid.NewGuid();
            claimed.Add(id);
            entries.Add(new AssetManifestEntry
            {
                Id = id, Source = source, Path = relative, Type = kind.Type, Hash = hash,
                Name = match?.Name ?? Path.GetFileNameWithoutExtension(relative),
                Package = packages is null ? PackageId.Game : VPackPipeline.ResolvePackage(packages, relative).Id
            });
        }
    }

    public static string Generate(string projectDirectory)
    {
        var root = Path.GetFullPath(projectDirectory);
        var output = Path.Combine(root, "Generated", "Assets.g.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, GenerateSource(AssetManifest.Load(Path.Combine(root, "Assets.manifest"))), new UTF8Encoding(false));
        return output;
    }

    public static string GenerateSource(AssetManifest manifest)
    {
        var builder = new StringBuilder("// <auto-generated />\n#nullable enable\nusing System;\nusing System.Threading;\nusing System.Threading.Tasks;\nusing Vecxy.Assets;\n\npublic static partial class Assets\n{\n");
        AppendCategories(builder, manifest.Assets.Where(x => !IsEngine(x) && x.Package == PackageId.Game), "    ");
        var engineAssets = manifest.Assets.Where(IsEngine).ToArray();
        var enginePackageId = engineAssets.Select(x => x.Package).Distinct().SingleOrDefault();
        foreach (var package in manifest.Packages.Where(x => x.Id != PackageId.Game && x.Id != enginePackageId).OrderBy(x => x.Name, StringComparer.Ordinal))
        {
            var packageName = UniqueIdentifier(package.Name, new HashSet<string>());
            builder.Append("    public static class ").Append(packageName).Append("\n    {\n")
                .Append("        private static AssetPackage Package => AssetPackages.Get(new PackageId(new Guid(\"")
                .Append(package.Id.Value.ToString("D")).Append("\")));\n")
                .Append("        public static bool IsLoaded => Package.IsLoaded;\n")
                .Append("        public static ValueTask<AssetPackageLease> LoadAsync(CancellationToken cancellationToken = default) => Package.LoadAsync(cancellationToken);\n\n");
            AppendRemoteApi(builder, "        ");
            AppendCategories(builder, manifest.Assets.Where(x => !IsEngine(x) && x.Package == package.Id), "        ", PackageRoot(package));
            builder.Append("    }\n\n");
        }
        if (engineAssets.Length > 0)
        {
            builder.Append("    public static class Engine\n    {\n")
                .Append("        private static AssetPackage Package => AssetPackages.Get(new PackageId(new Guid(\"")
                .Append(enginePackageId.Value.ToString("D")).Append("\")));\n")
                .Append("        public static bool IsLoaded => Package.IsLoaded;\n")
                .Append("        public static ValueTask<AssetPackageLease> LoadAsync(CancellationToken cancellationToken = default) => Package.LoadAsync(cancellationToken);\n\n");
            AppendRemoteApi(builder, "        ");
            AppendCategories(builder, engineAssets, "        ");
            builder.Append("    }\n\n");
        }
        return builder.Append("}\n").ToString();
    }

    private static void AppendCategories(StringBuilder builder, IEnumerable<AssetManifestEntry> assets, string indent, string? trimRoot = null)
    {
        foreach (var category in assets.GroupBy(x => Category(x, trimRoot)).OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            builder.Append(indent).Append("public static class ").Append(category.Key).Append('\n')
                .Append(indent).Append("{\n");
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var asset in category.OrderBy(x => x.Path, StringComparer.Ordinal))
            {
                var name = UniqueIdentifier(asset.Name ?? Path.GetFileNameWithoutExtension(asset.Path), used);
                builder.Append(indent).Append("    [AssetReference(\"").Append(asset.Id.ToString("D"))
                    .Append("\")]\n").Append(indent).Append("    public static ").Append(Handle(asset.Type)).Append(' ').Append(name)
                    .Append(" => new(new Guid(\"").Append(asset.Id.ToString("D")).Append("\"));\n");
            }
            builder.Append(indent).Append("}\n\n");
        }
    }

    private static void AppendRemoteApi(StringBuilder builder, string indent)
    {
        builder.Append(indent).Append("public static Task<RemotePackageStatus> GetRemoteStatusAsync(CancellationToken cancellationToken = default) => Package.GetRemoteStatusAsync(cancellationToken);\n")
            .Append(indent).Append("public static Task<RemotePackageStatus> CheckForUpdatesAsync(CancellationToken cancellationToken = default) => Package.CheckForUpdatesAsync(cancellationToken);\n")
            .Append(indent).Append("public static Task DownloadAsync(IProgress<PackageDownloadProgress>? progress = null, CancellationToken cancellationToken = default) => Package.DownloadAsync(progress, cancellationToken);\n")
            .Append(indent).Append("public static Task DownloadUpdateAsync(IProgress<PackageDownloadProgress>? progress = null, CancellationToken cancellationToken = default) => Package.DownloadUpdateAsync(progress, cancellationToken);\n")
            .Append(indent).Append("public static ValueTask<AssetPackageLease> EnsureLoadedAsync(IProgress<PackageDownloadProgress>? progress = null, CancellationToken cancellationToken = default) => Package.EnsureLoadedAsync(progress, cancellationToken);\n")
            .Append(indent).Append("public static Task<PackageCacheInfo> GetCacheInfoAsync(CancellationToken cancellationToken = default) => Package.GetCacheInfoAsync(cancellationToken);\n")
            .Append(indent).Append("public static Task RemoveCachedAsync(CancellationToken cancellationToken = default) => Package.RemoveCachedAsync(cancellationToken);\n\n");
    }

    private static string PackageRoot(AssetPackageManifestEntry package)
    {
        var descriptor = package.Descriptor ?? string.Empty;
        var separator = descriptor.LastIndexOf('/');
        return separator < 0 ? string.Empty : descriptor[..separator];
    }

    public static AssetReferenceDocument Analyze(string projectDirectory)
    {
        var root = Path.GetFullPath(projectDirectory);
        var manifest = AssetManifest.Load(Path.Combine(root, "Assets.manifest"));
        var symbols = BuildSymbols(manifest);
        var references = new Dictionary<Guid, List<AssetReferenceLocation>>();
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                     .Where(x => !IsUnder(x, "obj") && !IsUnder(x, "bin") && !IsUnder(x, "Generated")))
        {
            var lines = File.ReadAllLines(file);
            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            foreach (Match match in AssetUseRegex().Matches(lines[lineIndex]))
            {
                var symbol = match.Groups[3].Success
                    ? $"{match.Groups[1].Value}.{match.Groups[2].Value}.{match.Groups[3].Value}"
                    : $"{match.Groups[1].Value}.{match.Groups[2].Value}";
                if (!symbols.TryGetValue(symbol, out var id)) continue;
                if (!references.TryGetValue(id, out var locations)) references[id] = locations = [];
                locations.Add(new AssetReferenceLocation { File = Normalize(Path.GetRelativePath(root, file)), Line = lineIndex + 1 });
            }
        }
        var document = new AssetReferenceDocument(references);
        var output = Path.Combine(root, "obj", "vecxy.asset.references.json");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(document, JsonOptions));
        return document;
    }

    public static IReadOnlyList<MissingAsset> Validate(string projectDirectory, AssetReferenceDocument? references = null)
    {
        var root = Path.GetFullPath(projectDirectory);
        var manifest = AssetManifest.Load(Path.Combine(root, "Assets.manifest"));
        references ??= LoadReferences(root);
        var engineAssets = FindEngineAssetsDirectory(root);
        return manifest.Assets.Where(x => !File.Exists(Path.Combine(
                IsEngine(x) ? engineAssets ?? Path.Combine(root, "__missing_engine_assets__") : Path.Combine(root, "Assets"),
                x.Path.Replace('/', Path.DirectorySeparatorChar))))
            .Select(x => new MissingAsset(x, references.References.GetValueOrDefault(x.Id) ?? [])).ToArray();
    }

    public static AssetReferenceDocument LoadReferences(string root)
    {
        var path = Path.Combine(root, "obj", "vecxy.asset.references.json");
        return File.Exists(path) ? JsonSerializer.Deserialize<AssetReferenceDocument>(File.ReadAllText(path), JsonOptions) ?? new() : new();
    }

    private static void SaveManifest(string path, AssetManifest manifest) => File.WriteAllText(path, JsonSerializer.Serialize(manifest, AssetManifest.SerializerOptions), new UTF8Encoding(false));
    private static void TrackAssetDependencies(string assetsDirectory, string? engineAssetsDirectory, IReadOnlyList<AssetManifestEntry> entries)
    {
        foreach (var owner in entries)
        {
            owner.Dependencies.Clear();
            var sourceDirectory = IsEngine(owner) ? engineAssetsDirectory : assetsDirectory;
            if (sourceDirectory is null) continue;
            var fullPath = Path.Combine(sourceDirectory, owner.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath) || Path.GetExtension(fullPath).ToLowerInvariant() is not (".material" or ".atlas" or ".xml" or ".css")) continue;
            var source = File.ReadAllText(fullPath);
            foreach (var dependency in entries)
                if (dependency.Id != owner.Id && source.Contains(dependency.Path, StringComparison.OrdinalIgnoreCase)) owner.Dependencies.Add(dependency.Id);
        }
    }
    private static string SourceKey(string source, string path) => $"{source}:{Normalize(path)}";
    private static bool IsEngine(AssetManifestEntry entry) => string.Equals(entry.Source, "Engine", StringComparison.OrdinalIgnoreCase);
    internal static string? FindEngineAssetsDirectory(string projectDirectory)
    {
        var project = Directory.EnumerateFiles(projectDirectory, "*.csproj", SearchOption.TopDirectoryOnly).SingleOrDefault();
        if (project is null) return null;
        var document = XDocument.Load(project);
        foreach (var reference in document.Descendants().Where(x => x.Name.LocalName == "ProjectReference"))
        {
            var include = reference.Attribute("Include")?.Value;
            if (string.IsNullOrWhiteSpace(include) || !include.EndsWith("Vecxy.Engine.csproj", StringComparison.OrdinalIgnoreCase)) continue;
            var engineProject = Path.GetFullPath(include.Replace('\\', Path.DirectorySeparatorChar), projectDirectory);
            var assets = Path.Combine(Path.GetDirectoryName(engineProject)!, "Assets");
            if (Directory.Exists(assets)) return assets;
        }
        return null;
    }
    private static string Normalize(string path) => path.Replace('\\', '/');
    private static bool IsUnder(string path, string segment) => Normalize(path).Contains($"/{segment}/", StringComparison.OrdinalIgnoreCase);
    private static (string Type, string Category) Classify(string path) =>
        Types.TryGetValue(Path.GetExtension(path), out var value) ? value : ("Asset", "Other");
    private static string Category(AssetManifestEntry entry, string? trimRoot = null)
    {
        var path = entry.Path;
        if (!string.IsNullOrEmpty(trimRoot) && path.StartsWith(trimRoot + "/", StringComparison.OrdinalIgnoreCase)) path = path[(trimRoot.Length + 1)..];
        var separator = path.IndexOf('/');
        var name = separator > 0 ? path[..separator] : Classify(path).Category;
        return UniqueIdentifier(name, new HashSet<string>());
    }
    private static string Handle(string type) => type switch
    {
        "Texture2D" => "TextureHandle", "Model" => "ModelHandle", "Sound" => "SoundHandle",
        "Material" => "MaterialHandle", "Config" => "ConfigHandle", "Text" => "TextHandle",
        "Shader" => "ShaderHandle", "Input" => "InputHandle", _ => "AssetHandle"
    };
    private static Dictionary<string, Guid> BuildSymbols(AssetManifest manifest)
    {
        var result = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var package in manifest.Packages.Where(x => x.Id != PackageId.Game))
        foreach (var group in manifest.Assets.Where(x => !IsEngine(x) && x.Package == package.Id).GroupBy(x => Category(x, PackageRoot(package))))
        {
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var asset in group.OrderBy(x => x.Path, StringComparer.Ordinal))
                result[$"{package.Name}.{group.Key}.{UniqueIdentifier(asset.Name ?? Path.GetFileNameWithoutExtension(asset.Path), used)}"] = asset.Id;
        }
        foreach (var source in manifest.Assets.Where(x => IsEngine(x) || x.Package == PackageId.Game).GroupBy(IsEngine))
        {
            foreach (var group in source.GroupBy(x => Category(x)))
            {
                var used = new HashSet<string>(StringComparer.Ordinal);
                foreach (var asset in group.OrderBy(x => x.Path, StringComparer.Ordinal))
                {
                    var prefix = source.Key ? "Engine." : string.Empty;
                    result[$"{prefix}{group.Key}.{UniqueIdentifier(asset.Name ?? Path.GetFileNameWithoutExtension(asset.Path), used)}"] = asset.Id;
                }
            }
        }
        return result;
    }
    private static string UniqueIdentifier(string value, HashSet<string> used)
    {
        var words = Regex.Split(value, "[^A-Za-z0-9]+").Where(x => x.Length > 0);
        var baseName = string.Concat(words.Select(x => char.ToUpperInvariant(x[0]) + x[1..]));
        if (baseName.Length == 0) baseName = "Asset";
        if (char.IsDigit(baseName[0])) baseName = "_" + baseName;
        var name = baseName; for (var suffix = 2; !used.Add(name); suffix++) name = baseName + suffix;
        return name;
    }
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    [GeneratedRegex(@"\bAssets\.([A-Za-z_][A-Za-z0-9_]*)\.([A-Za-z_][A-Za-z0-9_]*)(?:\.([A-Za-z_][A-Za-z0-9_]*))?\b")]
    private static partial Regex AssetUseRegex();
}

public sealed class AssetReferenceDocument : Dictionary<Guid, List<AssetReferenceLocation>>
{
    public AssetReferenceDocument() { }
    public AssetReferenceDocument(IDictionary<Guid, List<AssetReferenceLocation>> values) : base(values) { }
    [System.Text.Json.Serialization.JsonIgnore]
    public Dictionary<Guid, List<AssetReferenceLocation>> References => this;
}
public sealed class AssetReferenceLocation { public required string File { get; init; } public int Line { get; init; } }
public sealed record MissingAsset(AssetManifestEntry Asset, IReadOnlyList<AssetReferenceLocation> References);
