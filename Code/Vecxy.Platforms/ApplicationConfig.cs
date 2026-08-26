using Vecxy.Assets;
using Vecxy.Engine;
using Vecxy.Kernel;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Vecxy.Platforms;

public sealed class ApplicationConfig
{
    public ApplicationSection Application { get; init; } = new();
    public WindowSection Window { get; init; } = new();
    public EngineSection Engine { get; init; } = new();
    public AssetsSection Assets { get; init; } = new();
    public List<string> Layers { get; init; } = [];

    public sealed class ApplicationSection
    {
        public string Title { get; init; } = "Vecxy Game";
    }

    public sealed class WindowSection
    {
        public int Width { get; init; } = 1280;
        public int Height { get; init; } = 720;
        public int? MonitorIndex { get; init; }
    }

    public sealed class EngineSection
    {
        public int TargetFrameRate { get; init; } = 60;
        public bool ShowSplashScreen { get; init; } = true;
        public string SplashScreenLogoPath { get; init; } = "Textures/Logo.png";
    }

    public sealed class AssetsSection
    {
        public bool HotReload { get; init; } = true;
        public int HotReloadDelayMilliseconds { get; init; } = 150;
    }

    internal void Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(Application.Title))
            throw new InvalidDataException($"Application title is missing in '{path}'.");
        if (Window.Width <= 0 || Window.Height <= 0)
            throw new InvalidDataException($"Window dimensions must be positive in '{path}'.");
        if (Engine.TargetFrameRate <= 0)
            throw new InvalidDataException($"Target frame rate must be positive in '{path}'.");
        if (Assets.HotReloadDelayMilliseconds < 0)
            throw new InvalidDataException($"Hot reload delay cannot be negative in '{path}'.");
        if (Layers.Count == 0 || Layers.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException($"At least one valid layer is required in '{path}'.");
        var duplicate = Layers.GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidDataException($"Layer '{duplicate.Key}' is listed more than once in '{path}'.");
    }
}

internal static class ApplicationConfigLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    public static ApplicationConfig Load(PlatformContext context, string path)
    {
        try
        {
            using var stream = context.OpenAsset(path);
            using var reader = new StreamReader(stream);
            var config = Deserializer.Deserialize<ApplicationConfig>(reader) ??
                         throw new InvalidDataException($"Application configuration '{path}' is empty.");
            config.Validate(path);
            return config;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException($"Could not read application configuration '{path}'.", exception);
        }
    }
}

public abstract class ConfiguredApplication : IEntryPoint
{
    private ApplicationConfig? _config;

    public void OnConfigureEngine(PlatformContext context, Engine.Engine.Options options)
    {
        var config = GetConfig(context);
        options.ShowSplashScreen = config.Engine.ShowSplashScreen;
        options.SplashScreenLogoPath = config.Engine.SplashScreenLogoPath;
        options.TargetFrameRate = config.Engine.TargetFrameRate;
        options.Window = new IWindow.Options(
            config.Application.Title,
            config.Window.Width,
            config.Window.Height,
            config.Window.MonitorIndex);
    }

    public void OnConfigureLayers(PlatformContext context, List<AAppLayer.IDefinition> layers)
    {
        var config = GetConfig(context);
        foreach (var id in config.Layers)
        {
            layers.Add(id.Equals("engine", StringComparison.OrdinalIgnoreCase)
                ? new EngineLayer.Definition(new AssetsModule.Options
                {
                    AssetsDirectory = context.AssetsDirectory,
                    HotReloadEnabled = config.Assets.HotReload,
                    HotReloadDelay = TimeSpan.FromMilliseconds(config.Assets.HotReloadDelayMilliseconds)
                })
                : ApplicationLayerResolver.Create(id));
        }
    }

    private ApplicationConfig GetConfig(PlatformContext context)
    {
        if (_config is not null)
            return _config;
        var attribute = GetType().GetCustomAttributes(typeof(VecxyApplicationAttribute), false)
            .Cast<VecxyApplicationAttribute>()
            .SingleOrDefault() ?? throw new InvalidOperationException(
                $"{GetType().FullName} must be marked with [{nameof(VecxyApplicationAttribute)}].");
        return _config = ApplicationConfigLoader.Load(context, attribute.ConfigPath);
    }
}
