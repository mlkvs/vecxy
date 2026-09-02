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
        [".luau"] = ("Script", "Scripts"),
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
        var engineSelection = EngineAssetSelection.Load(root);
        if (engineAssetsDirectory is not null)
            ScanDirectory("Engine", engineAssetsDirectory, entries, oldByPath, oldByHash, claimed,
                packages.Where(x => x.Source == "Engine").ToArray(), engineSelection.Includes);
        // Keep tombstones so generated symbols and diagnostics survive deletion. A later
        // file with the same content claims the old ID and therefore removes the tombstone.
        entries.AddRange(previous.Assets.Where(x => !claimed.Contains(x.Id) &&
            (!IsEngine(x) || engineSelection.Includes(x.Path))));
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
        IReadOnlyList<VPackPackageDefinition>? packages,
        Func<string, bool>? include = null)
    {
        foreach (var file in Directory.EnumerateFiles(assetsDirectory, "*", SearchOption.AllDirectories).Order())
        {
            if (string.Equals(Path.GetExtension(file), ".vpack", StringComparison.OrdinalIgnoreCase)) continue;
            var relative = Normalize(Path.GetRelativePath(assetsDirectory, file));
            if (include is not null && !include(relative)) continue;
            var kind = Classify(file);
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
        AppendAssetNodes(builder, BuildAssetTree(assets, trimRoot), indent);
    }

    private static void AppendAssetNodes(StringBuilder builder, AssetSymbolNode node, string indent)
    {
        foreach (var child in node.Children.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            builder.Append(indent).Append("public static class ").Append(child.Key).Append('\n')
                .Append(indent).Append("{\n");
            AppendAssetNodes(builder, child.Value, indent + "    ");
            foreach (var asset in child.Value.Assets.OrderBy(x => x.Name, StringComparer.Ordinal))
            {
                builder.Append(indent).Append("    [AssetReference(\"").Append(asset.Entry.Id.ToString("D"))
                    .Append("\")]\n").Append(indent).Append("    public static ").Append(Handle(asset.Entry.Type)).Append(' ').Append(asset.Name)
                    .Append(" => new(new Guid(\"").Append(asset.Entry.Id.ToString("D")).Append("\"));\n");
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
                var symbol = match.Value["Assets.".Length..];
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
            if (!File.Exists(fullPath)) continue;
            var extension = Path.GetExtension(fullPath).ToLowerInvariant();
            if (extension == ".luau")
            {
                var luauSource = File.ReadAllText(fullPath);
                foreach (Match match in LuauRequirePattern().Matches(luauSource))
                {
                    var dependencyPath = ResolveLuauDependency(owner.Path, match.Groups["path"].Value);
                    var dependency = entries.FirstOrDefault(candidate =>
                        candidate.Id != owner.Id &&
                        string.Equals(candidate.Source, owner.Source, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(candidate.Path, dependencyPath, StringComparison.OrdinalIgnoreCase));
                    if (dependency is not null && !owner.Dependencies.Contains(dependency.Id))
                        owner.Dependencies.Add(dependency.Id);
                }
                continue;
            }
            if (extension is not (".material" or ".atlas" or ".xml" or ".css")) continue;
            var source = File.ReadAllText(fullPath);
            foreach (var dependency in entries)
                if (dependency.Id != owner.Id && source.Contains(dependency.Path, StringComparison.OrdinalIgnoreCase)) owner.Dependencies.Add(dependency.Id);
        }
    }
    private static string ResolveLuauDependency(string ownerPath, string request)
    {
        var combined = request.StartsWith(".", StringComparison.Ordinal)
            ? Path.Combine(Path.GetDirectoryName(ownerPath) ?? string.Empty, request)
            : request;
        var segments = Normalize(combined).Split('/', StringSplitOptions.RemoveEmptyEntries);
        var result = new List<string>(segments.Length);
        foreach (var segment in segments)
        {
            if (segment == ".") continue;
            if (segment == "..")
            {
                if (result.Count == 0) return "../";
                result.RemoveAt(result.Count - 1);
            }
            else result.Add(segment);
        }
        var path = string.Join('/', result);
        return Path.GetExtension(path).Length == 0 ? path + ".luau" : path;
    }

    [GeneratedRegex(
        "\\brequire\\s*\\(\\s*[\"'](?<path>[^\"']+)[\"']\\s*\\)",
        RegexOptions.CultureInvariant)]
    private static partial Regex LuauRequirePattern();
    private static string SourceKey(string source, string path) => $"{source}:{Normalize(path)}";
    private static bool IsEngine(AssetManifestEntry entry) => string.Equals(entry.Source, "Engine", StringComparison.OrdinalIgnoreCase);
    internal static string? FindEngineAssetsDirectory(string projectDirectory)
    {
        var configured = FindConfiguredEngineAssetsDirectory(projectDirectory);
        if (configured is not null) return configured;
        var project = Directory.EnumerateFiles(projectDirectory, "*.csproj", SearchOption.TopDirectoryOnly).SingleOrDefault();
        if (project is null) return null;
        return FindEngineAssetsDirectory(project, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    private static string? FindConfiguredEngineAssetsDirectory(string projectDirectory)
    {
        var engineRoot = Environment.GetEnvironmentVariable("VECXY_ENGINE_PATH");
        var props = Path.Combine(projectDirectory, ".vecxy", "Engine.props");
        if (string.IsNullOrWhiteSpace(engineRoot) && File.Exists(props))
        {
            var document = XDocument.Load(props);
            engineRoot = document.Descendants().FirstOrDefault(x =>
                x.Name.LocalName.Equals("VecxyEnginePath", StringComparison.OrdinalIgnoreCase))?.Value;
            if (!string.IsNullOrWhiteSpace(engineRoot) && !Path.IsPathFullyQualified(engineRoot))
                engineRoot = Path.GetFullPath(engineRoot, projectDirectory);
        }

        if (string.IsNullOrWhiteSpace(engineRoot) || engineRoot.Contains("$(", StringComparison.Ordinal)) return null;
        engineRoot = Environment.ExpandEnvironmentVariables(engineRoot);
        foreach (var candidate in new[]
                 {
                     Path.Combine(engineRoot, "Code", "Vecxy.Engine", "Assets"),
                     Path.Combine(engineRoot, "Vecxy.Engine", "Assets"),
                     Path.Combine(engineRoot, "Assets")
                 })
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "engine.vpack")))
                return Path.GetFullPath(candidate);
        return null;
    }

    private static string? FindEngineAssetsDirectory(string project, ISet<string> visited)
    {
        project = Path.GetFullPath(project);
        if (!visited.Add(project)) return null;
        if (Path.GetFileName(project).Equals("Vecxy.Engine.csproj", StringComparison.OrdinalIgnoreCase))
        {
            var assets = Path.Combine(Path.GetDirectoryName(project)!, "Assets");
            if (Directory.Exists(assets)) return assets;
        }

        var document = XDocument.Load(project);
        foreach (var reference in document.Descendants().Where(x => x.Name.LocalName == "ProjectReference"))
        {
            var include = reference.Attribute("Include")?.Value;
            if (string.IsNullOrWhiteSpace(include)) continue;
            var referencedProject = Path.GetFullPath(
                include.Replace('\\', Path.DirectorySeparatorChar),
                Path.GetDirectoryName(project)!);
            if (!File.Exists(referencedProject)) continue;
            var assets = FindEngineAssetsDirectory(referencedProject, visited);
            if (assets is not null) return assets;
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
            AddSymbols(result,
                BuildAssetTree(manifest.Assets.Where(x => !IsEngine(x) && x.Package == package.Id), PackageRoot(package)),
                UniqueIdentifier(package.Name, new HashSet<string>()));
        AddSymbols(result, BuildAssetTree(manifest.Assets.Where(x => !IsEngine(x) && x.Package == PackageId.Game)), string.Empty);
        AddSymbols(result, BuildAssetTree(manifest.Assets.Where(IsEngine)), "Engine");
        return result;
    }

    private static AssetSymbolNode BuildAssetTree(IEnumerable<AssetManifestEntry> assets, string? trimRoot = null)
    {
        var root = new AssetSymbolNode();
        foreach (var asset in assets.OrderBy(x => x.Path, StringComparer.Ordinal))
        {
            var path = asset.Path;
            if (!string.IsNullOrEmpty(trimRoot) && path.StartsWith(trimRoot + "/", StringComparison.OrdinalIgnoreCase))
                path = path[(trimRoot.Length + 1)..];
            var segments = Normalize(path).Split('/', StringSplitOptions.RemoveEmptyEntries);
            var directories = segments.Length > 1 ? segments[..^1] : [Classify(path).Category];
            var node = root;
            foreach (var directory in directories)
            {
                var name = UniqueIdentifier(directory, new HashSet<string>());
                if (!node.Children.TryGetValue(name, out var child))
                    node.Children.Add(name, child = new AssetSymbolNode());
                node = child;
            }
            var used = node.Children.Keys.Concat(node.Assets.Select(x => x.Name)).ToHashSet(StringComparer.Ordinal);
            var assetName = UniqueIdentifier(asset.Name ?? Path.GetFileNameWithoutExtension(path), used);
            node.Assets.Add(new AssetSymbol(assetName, asset));
        }
        return root;
    }

    private static void AddSymbols(Dictionary<string, Guid> result, AssetSymbolNode node, string prefix)
    {
        foreach (var asset in node.Assets)
            result[string.IsNullOrEmpty(prefix) ? asset.Name : $"{prefix}.{asset.Name}"] = asset.Entry.Id;
        foreach (var child in node.Children)
            AddSymbols(result, child.Value, string.IsNullOrEmpty(prefix) ? child.Key : $"{prefix}.{child.Key}");
    }

    private sealed class AssetSymbolNode
    {
        public Dictionary<string, AssetSymbolNode> Children { get; } = new(StringComparer.Ordinal);
        public List<AssetSymbol> Assets { get; } = [];
    }

    private sealed record AssetSymbol(string Name, AssetManifestEntry Entry);
    private static string UniqueIdentifier(string value, HashSet<string> used)
    {
        var words = Regex.Split(value, "[^A-Za-z0-9]+").Where(x => x.Length > 0);
        var baseName = string.Concat(words.Select(x => char.ToUpperInvariant(x[0]) + x[1..]));
        if (baseName.Length == 0) baseName = "Asset";
        if (char.IsDigit(baseName[0])) baseName = "_" + baseName;
        var name = baseName; for (var suffix = 2; !used.Add(name); suffix++) name = baseName + suffix;
        return name;
    }

    private sealed class EngineAssetSelection
    {
        private static readonly HashSet<string> KnownFeatures = new(StringComparer.OrdinalIgnoreCase) { "Skybox" };
        private static readonly HashSet<string> KnownContent = new(StringComparer.OrdinalIgnoreCase) { "DefaultSkybox" };
        private readonly HashSet<string> _disabledFeatures;
        private readonly HashSet<string> _disabledContent;

        private EngineAssetSelection(HashSet<string> disabledFeatures, HashSet<string> disabledContent)
        {
            _disabledFeatures = disabledFeatures;
            _disabledContent = disabledContent;
        }

        public static EngineAssetSelection Load(string projectDirectory)
        {
            var project = Directory.EnumerateFiles(projectDirectory, "*.csproj", SearchOption.TopDirectoryOnly).SingleOrDefault();
            if (project is null) return new([], []);
            var document = XDocument.Load(project);
            var features = ReadList(document, "VecxyDisabledEngineFeatures");
            var content = ReadList(document, "VecxyDisabledEngineContent");
            Validate(features, KnownFeatures, "VecxyDisabledEngineFeatures", project);
            Validate(content, KnownContent, "VecxyDisabledEngineContent", project);
            return new(features, content);
        }

        public bool Includes(string path)
        {
            var normalized = Normalize(path);
            if (_disabledFeatures.Contains("Skybox") &&
                normalized.Equals("Shaders/Skybox.glsl", StringComparison.OrdinalIgnoreCase)) return false;
            if (_disabledContent.Contains("DefaultSkybox") &&
                normalized.StartsWith("SkyBox/", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        private static HashSet<string> ReadList(XDocument document, string propertyName) =>
            document.Descendants().Where(x => x.Name.LocalName == propertyName)
                .SelectMany(x => x.Value.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        private static void Validate(HashSet<string> values, HashSet<string> known, string property, string project)
        {
            var unknown = values.FirstOrDefault(value => !known.Contains(value));
            if (unknown is not null)
                throw new InvalidDataException($"Unknown Vecxy engine option '{unknown}' in {property} ({project}).");
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    [GeneratedRegex(@"\bAssets(?:\.[A-Za-z_][A-Za-z0-9_]*){2,}\b")]
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
