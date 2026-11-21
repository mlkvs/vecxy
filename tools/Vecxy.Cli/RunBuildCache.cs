using System.Collections;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

/// <summary>Content-based reuse of a successful run build and its restored project graph.</summary>
internal sealed class RunBuildCache(string project, string mode, string runtime, string output)
{
    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    { "bin", "obj", "build", "artifacts", ".artifacts", ".git", ".idea", ".vs", ".agents", ".vecxy", "node_modules", "__pycache__", ".venv", "venv" };
    private readonly string _profile = string.Join("\n", "run-cache-v2", Path.GetFullPath(project), mode, runtime, Path.GetFullPath(output));
    private string Stamp => Path.Combine(project, "obj", "vecxy-run", HashText(_profile) + ".json");
    private sealed record Snapshot(string Profile, Dictionary<string, string> Inputs, Dictionary<string, string> Outputs,
        string? ToolAssembly, Dictionary<string, string>? ToolInputs, Dictionary<string, string> ToolOutputs);
    private sealed record ToolSnapshot(string ToolAssembly, Dictionary<string, string>? ToolInputs,
        Dictionary<string, string> ToolOutputs);

    public bool IsCurrent()
    {
        try
        {
            if (!File.Exists(Stamp)) return false;
            var previous = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(Stamp));
            var current = CaptureInputs();
            return previous is { Inputs: not null, Outputs: not null } && previous.Profile == _profile && previous.Outputs.Count > 0 &&
                previous.Inputs.Count == current.Count && previous.Inputs.All(file => current.TryGetValue(file.Key, out var hash) && hash == file.Value) &&
                previous.Outputs.All(file => FileHash(file.Key) == file.Value);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or System.Xml.XmlException)
        { return false; }
    }

    public bool CanRefreshAssets()
    {
        try
        {
            if (!File.Exists(Stamp)) return false;
            var previous = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(Stamp));
            if (previous is not { Inputs: not null, Outputs: not null } || previous.Profile != _profile || !Directory.Exists(output)) return false;
            var current = CaptureInputs();
            if (previous.Inputs.Count != current.Count || current.Keys.Any(path => !previous.Inputs.ContainsKey(path))) return false;
            var engine = Environment.GetEnvironmentVariable("VECXY_ENGINE_PATH");
            var roots = new[] { Path.Combine(project, "Assets"), Path.Combine(engine ?? project, "Code", "Vecxy.Engine", "Assets") }
                .Select(path => Path.GetFullPath(path) + Path.DirectorySeparatorChar).ToArray();
            bool RuntimeData(string path)
            {
                if (path == Path.Combine(project, "Assets.manifest")) return true;
                if (!roots.Any(root => path.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))) return false;
                // These formats can also participate in compilation/code generation.
                return Path.GetExtension(path).ToLowerInvariant() is not (".cs" or ".csproj" or ".props" or ".targets" or ".resx" or ".input" or ".atlas");
            }
            if (current.Any(file => file.Value != previous.Inputs[file.Key] && !RuntimeData(file.Key))) return false;
            var changed = current.Where(file => file.Value != previous.Inputs[file.Key]).Select(file => file.Key).ToArray();
            foreach (var projectFile in current.Keys.Where(path => path.EndsWith(".csproj") && File.Exists(path)))
                foreach (var item in XDocument.Load(projectFile).Descendants().Where(item => item.Name.LocalName == "EmbeddedResource"))
                {
                    var include = item.Attribute("Include")?.Value;
                    if (string.IsNullOrEmpty(include)) continue;
                    var directory = Path.GetDirectoryName(projectFile)!;
                    include = include.Replace("$(MSBuildProjectDirectory)", directory).Replace("$(VecxyEnginePath)", engine ?? "$(VecxyEnginePath)")
                        .Replace('\\', Path.DirectorySeparatorChar);
                    if (include.Contains("$("))
                    {
                        if (changed.Any(path => path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal))) return false;
                        continue;
                    }
                    foreach (var part in include.Split(';', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var wildcard = part.IndexOfAny(['*', '?']);
                        var bound = Path.GetFullPath(wildcard < 0 ? part : part[..wildcard], directory);
                        if (changed.Any(path => wildcard < 0 ? path == bound : path.StartsWith(bound, StringComparison.Ordinal))) return false;
                    }
                }
            // Broken/missing runtime packages can be replaced; changed code cannot.
            return previous.Outputs.Where(file => !AssetRefreshCommand.IsPackageFile(file.Key) && Path.GetFileName(file.Key) != "Assets.manifest")
                .All(file => FileHash(file.Key) == file.Value);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or System.Xml.XmlException)
        { return false; }
    }

    public Dictionary<string, string> CaptureInputs()
    {
        var files = new HashSet<string>(StringComparer.Ordinal);
        var projects = new HashSet<string>(StringComparer.Ordinal);
        var engine = Environment.GetEnvironmentVariable("VECXY_ENGINE_PATH") ?? Environment.GetEnvironmentVariable("VecxyEnginePath");
        AddProject(Directory.GetFiles(project, "*.csproj").Single());
        if (!string.IsNullOrEmpty(engine))
            foreach (var relative in new[] { "tools/Vecxy.Cli", "tools/Vecxy.AssetPipeline", "Code/Vecxy.Networking.Weaver" })
                foreach (var tool in Directory.Exists(Path.Combine(engine, relative)) ? Directory.GetFiles(Path.Combine(engine, relative), "*.csproj") : []) AddProject(tool);
        foreach (var path in projects.ToArray())
        {
            AddTree(Path.GetDirectoryName(path)!);
            AddParents(Path.GetDirectoryName(path)!);
            AddMsBuildInputs(path, new HashSet<string>(StringComparer.Ordinal));
        }
        var settings = Path.Combine(project, ".vecxy");
        if (Directory.Exists(settings)) foreach (var file in Directory.GetFiles(settings)) files.Add(Path.GetFullPath(file));
        var result = files.Order(StringComparer.Ordinal).ToDictionary(path => path, FileHash, StringComparer.Ordinal);
        foreach (DictionaryEntry variable in Environment.GetEnvironmentVariables())
        {
            var name = (string)variable.Key;
            if ((name.StartsWith("VECXY_", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Vecxy", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("NUGET_", StringComparison.OrdinalIgnoreCase) ||
                name is "DOTNET_ROOT" or "DOTNET_ROOT_X64" or "DOTNET_ROOT_ARM64" or "DOTNET_ROOT_X86" or "MSBuildSDKsPath" or "Configuration" or "DefineConstants" or "CI") &&
                name is not ("VECXY_WAIT_FOR_DEBUGGER" or "VECXY_CLI_BOOTSTRAP"))
                result["env:" + name] = HashText(variable.Value?.ToString() ?? "");
        }
        return result;

        void AddProject(string path)
        {
            path = Path.GetFullPath(path);
            if (!projects.Add(path)) return;
            var directory = Path.GetDirectoryName(path)!;
            var graphName = Path.GetFileName(path) + ".nuget.dgspec.json";
            var graph = new[] { Path.Combine(directory, "obj", "Desktop", graphName), Path.Combine(directory, "obj", graphName) }.FirstOrDefault(File.Exists);
            if (graph is not null)
            {
                files.Add(graph);
                using var json = JsonDocument.Parse(File.ReadAllText(graph));
                foreach (var dependency in json.RootElement.GetProperty("projects").EnumerateObject())
                {
                    projects.Add(Path.GetFullPath(dependency.Name));
                    if (dependency.Value.TryGetProperty("restore", out var restore) && restore.TryGetProperty("configFilePaths", out var configs))
                        foreach (var config in configs.EnumerateArray()) if (config.GetString() is { } configPath) files.Add(Path.GetFullPath(configPath));
                }
            }
            else AddMsBuildInputs(path, new HashSet<string>(StringComparer.Ordinal), followProjects: true);
        }
        void AddTree(string directory)
        {
            if (!Directory.Exists(directory)) { files.Add(directory); return; }
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var name = Path.GetFileName(entry);
                if (Directory.Exists(entry))
                {
                    if (!IgnoredDirectories.Contains(name) && (File.GetAttributes(entry) & FileAttributes.ReparsePoint) == 0) AddTree(entry);
                }
                else if (!name.EndsWith(".log", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".user", StringComparison.OrdinalIgnoreCase) &&
                    !Path.GetExtension(name).StartsWith(".blend", StringComparison.OrdinalIgnoreCase) && name != "vecxy.components.json") files.Add(Path.GetFullPath(entry));
            }
        }
        void AddParents(string directory)
        {
            for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
                foreach (var name in new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "global.json", "NuGet.Config", "nuget.config" })
                {
                    var file = Path.Combine(current.FullName, name);
                    files.Add(file); // Adding a nearer override also invalidates reuse.
                    if (File.Exists(file) && (name.EndsWith(".props") || name.EndsWith(".targets"))) AddMsBuildInputs(file, new HashSet<string>(StringComparer.Ordinal));
                }
        }
        void AddMsBuildInputs(string file, HashSet<string> visited, bool followProjects = false)
        {
            file = Path.GetFullPath(file); files.Add(file);
            if (!visited.Add(file) || !File.Exists(file)) return;
            var directory = Path.GetDirectoryName(file)!;
            foreach (var item in XDocument.Load(file).Descendants())
            {
                var kind = item.Name.LocalName;
                if (kind is not ("Import" or "ProjectReference" or "Compile" or "Content" or "None" or "EmbeddedResource" or "AdditionalFiles")) continue;
                var include = item.Attribute(kind == "Import" ? "Project" : "Include")?.Value;
                if (string.IsNullOrEmpty(include)) continue;
                include = include.Replace("$(VecxyEnginePath)", engine ?? "$(VecxyEnginePath)")
                    .Replace("$(MSBuildProjectDirectory)", project).Replace("$(MSBuildThisFileDirectory)", directory + Path.DirectorySeparatorChar);
                if (include.Contains("$(") || include.Contains("@(")) continue;
                foreach (var part in include.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var normalized = part.Replace('\\', Path.DirectorySeparatorChar);
                    var wildcard = normalized.IndexOfAny(['*', '?']);
                    if (wildcard >= 0)
                    {
                        var prefix = normalized[..wildcard];
                        var parent = prefix.EndsWith(Path.DirectorySeparatorChar) ? prefix : Path.GetDirectoryName(prefix);
                        if (!string.IsNullOrEmpty(parent)) AddTree(Path.GetFullPath(parent, directory));
                        continue;
                    }
                    var dependency = Path.GetFullPath(normalized, directory); files.Add(dependency);
                    if (kind == "Import") AddMsBuildInputs(dependency, visited, followProjects);
                    else if (kind == "ProjectReference" && followProjects) AddProject(dependency);
                }
            }
        }
    }

    public void Save(Dictionary<string, string> before)
    {
        var inputs = CaptureInputs();
        // First restore can discover new dependencies. On subsequent builds,
        // do not mark code edited during compilation as already compiled.
        bool Stable(string path) => !path.EndsWith(".nuget.dgspec.json", StringComparison.Ordinal) &&
            Path.GetFileName(path) != "Assets.manifest" && !path.EndsWith(".usdscene", StringComparison.Ordinal) &&
            !path.Replace('\\', '/').Contains("/Generated/") && !path.Replace('\\', '/').Contains("/Scenes/Compiled/");
        var hadGraph = before.Keys.Any(path => path.EndsWith(".nuget.dgspec.json") && path.StartsWith(Path.GetFullPath(project), StringComparison.Ordinal));
        if (hadGraph && (before.Where(pair => Stable(pair.Key)).Any(pair => !inputs.TryGetValue(pair.Key, out var hash) || hash != pair.Value) ||
            inputs.Keys.Where(Stable).Any(path => !before.ContainsKey(path))))
        {
            Console.WriteLine("Build inputs changed during compilation; run cache was not saved.");
            return;
        }
        var outputs = Directory.GetFiles(output, "*", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(".log", StringComparison.OrdinalIgnoreCase) && Path.GetFileName(path) != "imgui.ini")
            .ToDictionary(path => Path.GetFullPath(path), FileHash, StringComparer.Ordinal);
        Directory.CreateDirectory(Path.GetDirectoryName(Stamp)!);
        var temporary = Stamp + "." + Guid.NewGuid().ToString("N");
        try
        {
            var assembly = typeof(RunBuildCache).Assembly.Location;
            var toolInputs = CaptureToolInputs();
            var toolOutputs = Directory.GetFiles(Path.GetDirectoryName(assembly)!, "*", SearchOption.AllDirectories)
                .ToDictionary(path => Path.GetFullPath(path), FileHash, StringComparer.Ordinal);
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Snapshot(_profile, inputs, outputs, assembly, toolInputs, toolOutputs)));
            File.Move(temporary, Stamp, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private Dictionary<string, string>? CaptureToolInputs()
    {
        var engine = Environment.GetEnvironmentVariable("VECXY_ENGINE_PATH");
        if (string.IsNullOrEmpty(engine)) return null;
        var inputs = new RunBuildCache(Path.Combine(engine, "tools", "Vecxy.Cli"), "tool", runtime, output).CaptureInputs();
        // Directory stamps detect newly added CLI source files as well as edits.
        foreach (var directory in inputs.Keys.Where(path => !path.StartsWith("env:") && File.Exists(path))
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) &&
                !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .Select(Path.GetDirectoryName).OfType<string>().Distinct().ToArray())
            inputs["dir:" + directory] = HashText(string.Join("\n", Directory.EnumerateFileSystemEntries(directory).Where(path =>
                !IgnoredDirectories.Contains(Path.GetFileName(path)) &&
                !path.EndsWith(".log", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".user", StringComparison.OrdinalIgnoreCase) &&
                !Path.GetExtension(path).StartsWith(".blend", StringComparison.OrdinalIgnoreCase) && Path.GetFileName(path) != "vecxy.components.json")
                .Select(Path.GetFileName).Order(StringComparer.Ordinal)));
        return inputs;
    }

    public static void RecordCompiledTool()
    {
        var engine = Environment.GetEnvironmentVariable("VECXY_ENGINE_PATH");
        if (string.IsNullOrEmpty(engine) ||
            !long.TryParse(Environment.GetEnvironmentVariable("VECXY_CLI_BOOTSTRAP"), out var epoch))
            return;

        try
        {
            var start = DateTimeOffset.FromUnixTimeMilliseconds(epoch).UtcDateTime;
            var tool = Path.Combine(engine, "tools", "Vecxy.Cli");
            var assembly = typeof(RunBuildCache).Assembly.Location;
            var cache = new RunBuildCache(tool, "tool", "", tool);
            var inputs = cache.CaptureToolInputs();
            if (inputs is null || inputs.Keys.Any(path => Path.IsPathRooted(path) && File.Exists(path) &&
                !path.EndsWith(".nuget.dgspec.json", StringComparison.Ordinal) && File.GetLastWriteTimeUtc(path) >= start))
                return;

            var outputs = Directory.GetFiles(Path.GetDirectoryName(assembly)!, "*", SearchOption.AllDirectories)
                .ToDictionary(path => Path.GetFullPath(path), FileHash, StringComparer.Ordinal);
            var path = Path.Combine(tool, "obj", "vecxy-tool.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + "." + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(new ToolSnapshot(assembly, inputs, outputs)));
                File.Move(temporary, path, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or
            System.Xml.XmlException or ArgumentOutOfRangeException)
        {
            // Starting the CLI must not depend on being able to write a cache.
        }
    }

    public void Invalidate() { if (File.Exists(Stamp)) File.Delete(Stamp); }
    private static string FileHash(string path) => File.Exists(path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : "<missing>";
    private static string HashText(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
