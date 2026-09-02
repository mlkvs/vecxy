using System.Text;
using System.Text.RegularExpressions;
using Pipeline = Vecxy.AssetPipeline.AssetPipeline;

internal static partial class NewProjectCommand
{
    public static string Create(string name, string? output, string? engineOption)
    {
        ValidateName(name);
        var destination = Path.GetFullPath(output ?? Path.Combine(Directory.GetCurrentDirectory(), name));
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
            throw new IOException($"Destination directory is not empty: {destination}");

        var engine = ResolveEngine(engineOption);
        Directory.CreateDirectory(destination);
        Directory.CreateDirectory(Path.Combine(destination, "Assets", "Configs"));
        Directory.CreateDirectory(Path.Combine(destination, "Generated"));
        Directory.CreateDirectory(Path.Combine(destination, "Properties"));
        Directory.CreateDirectory(Path.Combine(destination, ".vecxy"));

        var projectName = name;
        var rootNamespace = NamespacePart().Replace(name, "_");
        var applicationId = $"game.vecxy.{Slug(name)}";
        Write(Path.Combine(destination, $"{projectName}.csproj"), ProjectFile(applicationId, name));
        Write(Path.Combine(destination, "Program.cs"), EntryPoint(rootNamespace));
        Write(Path.Combine(destination, "Assets", "Configs", "Application.yaml"), ApplicationConfig(name));
        Write(Path.Combine(destination, "Assets", "game.vpack"), PackageConfig());
        Write(Path.Combine(destination, "Properties", "launchSettings.json"), LaunchSettings());
        Write(Path.Combine(destination, ".gitignore"), GitIgnore());
        Write(Path.Combine(destination, ".vecxy", "Engine.props"), EngineProps(engine));
        Write(Path.Combine(destination, ".vecxy", "config.json"), EngineConfig());

        Pipeline.Scan(destination);
        Pipeline.Generate(destination);
        return destination;
    }

    private static string ResolveEngine(string? option)
    {
        if (!string.IsNullOrWhiteSpace(option))
            return ValidateEngine(Path.GetFullPath(option));

        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            foreach (var candidate in new[] { directory.FullName, Path.Combine(directory.FullName, "Engine", "Vecxy") })
                if (IsEngine(candidate)) return Path.GetFullPath(candidate);
        }
        throw new DirectoryNotFoundException("Vecxy Engine was not found. Pass --engine <path-to-Engine/Vecxy>.");
    }

    private static string ValidateEngine(string path)
    {
        if (IsEngine(path)) return path;
        var nested = Path.Combine(path, "Engine", "Vecxy");
        if (IsEngine(nested)) return nested;
        throw new DirectoryNotFoundException($"Vecxy Engine was not found at '{path}'. Expected Code/Vecxy.Platforms/build/Vecxy.Platforms.props.");
    }

    private static bool IsEngine(string path) => File.Exists(Path.Combine(path, "Code", "Vecxy.Platforms", "build", "Vecxy.Platforms.props"));

    private static string ProjectFile(string applicationId, string title)
    {
        return $$"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <VecxyGenerateEntryPoint>true</VecxyGenerateEntryPoint>
            <VecxyApplicationId>{{applicationId}}</VecxyApplicationId>
            <VecxyApplicationTitle>{{EscapeXml(title)}}</VecxyApplicationTitle>
            <VecxyApplicationVersion>1.0.0</VecxyApplicationVersion>
            <Version>1.0.0</Version>
          </PropertyGroup>

          <Import Project=".vecxy/Engine.props" />
        </Project>
        """;
    }

    private static string EngineProps(string engine) => $$"""
        <Project>
          <PropertyGroup>
            <VecxyEnginePath>{{EscapeXml(engine.Replace('\\', '/'))}}</VecxyEnginePath>
          </PropertyGroup>
          <Import Project="$(VecxyEnginePath)/Code/Vecxy.Platforms/build/Vecxy.Platforms.props" />
          <ItemGroup>
            <ProjectReference Include="$(VecxyEnginePath)/Code/Vecxy.Engine/Vecxy.Engine.csproj" />
            <ProjectReference Include="$(VecxyEnginePath)/Code/Vecxy.Assets/Vecxy.Assets.csproj" />
            <ProjectReference Include="$(VecxyEnginePath)/Code/Vecxy.Kernel/Vecxy.Kernel.csproj" />
          </ItemGroup>
        </Project>
        """;

    private static string EngineConfig()
    {
        var reference = Environment.GetEnvironmentVariable("VECXY_ENGINE_REF") ?? "develop";
        return $$"""
        {
          "engine": {
            "repository": "https://github.com/mlkvs/vecxy.git",
            "ref": "{{JsonString(reference)}}"
          }
        }
        """;
    }

    private static string EntryPoint(string rootNamespace) => $$"""
        using Vecxy.Kernel;
        using Vecxy.Platforms;

        namespace {{rootNamespace}};

        [App]
        public sealed class Application : AApp;
        """;

    private static string ApplicationConfig(string title) => $$"""
        application:
          title: {{YamlString(title)}}

        window:
          width: 1280
          height: 720

        engine:
          targetFrameRate: 60
          showSplashScreen: false

        assets:
          hotReload: true

        layers:
          - engine
        """;

    private static string PackageConfig() => """
        name: Game
        version: 1.0.0
        load: startup
        dependencies: []
        compression: balanced
        """;

    private static string LaunchSettings() => """
        {
          "$schema": "http://json.schemastore.org/launchsettings.json",
          "profiles": {
            "Game": {
              "commandName": "Project"
            }
          }
        }
        """;

    private static string GitIgnore() => """
        bin/
        obj/
        Build/
        .vecxy/Engine.props
        """;

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !ProjectName().IsMatch(name))
            throw new ArgumentException("Project name must start with a letter or underscore and contain only letters, digits, dots, underscores, or hyphens.");
    }

    private static string Slug(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.ToLowerInvariant())
            builder.Append(character is >= 'a' and <= 'z' or >= '0' and <= '9' ? character : '.');
        var result = builder.ToString().Trim('.');
        return result.Length == 0 ? "game" : result;
    }

    private static string EscapeXml(string value) => System.Security.SecurityElement.Escape(value) ?? value;
    private static string YamlString(string value) => $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
    private static string JsonString(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    private static void Write(string path, string contents) => File.WriteAllText(path, contents.Replace("\r\n", "\n") + (contents.EndsWith('\n') ? "" : "\n"), new UTF8Encoding(false));

    [GeneratedRegex("^[\\p{L}_][\\p{L}\\p{N}_.-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ProjectName();

    [GeneratedRegex("[^\\p{L}\\p{N}_]", RegexOptions.CultureInvariant)]
    private static partial Regex NamespacePart();
}
