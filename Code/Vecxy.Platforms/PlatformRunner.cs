using Vecxy.Engine;
using System.Reflection;

namespace Vecxy.Platforms;

public static class PlatformRunner
{
    public static void Run(
        IVEntry application,
        PlatformContext context,
        Engine.Engine.Options? options = null,
        IEngineSplashScreen? splashScreen = null)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(context);

        options ??= new Engine.Engine.Options();
        var layers = new List<AAppLayer.IDefinition>();

        application.OnConfigureEngine(context, options);
        application.OnConfigureLayers(context, layers);
        
        using var engine = new Engine.Engine
        (
            options,
            layers,
            context.AssetsDirectory,
            splashScreen
        );
        
        engine.Run();
    }

    public static void RunDesktop<TApplication>()
        where TApplication : IVEntry, new()
    {
        var assetsDirectory = Assembly.GetEntryAssembly()?
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "VecxyAssetsDirectory")?.Value;
        assetsDirectory = Path.GetFullPath(assetsDirectory ??
            Path.Combine(AppContext.BaseDirectory, "Assets"));
        var openBootstrapAsset = DesktopBootstrapAssets.CreateReader(AppContext.BaseDirectory);
        Run(
            new TApplication(),
            new PlatformContext(PlatformKind.Desktop, assetsDirectory, openBootstrapAsset));
    }

    public static void RunDesktopApplication(string[]? args = null)
    {
        var assetsDirectory = Assembly.GetEntryAssembly()?
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "VecxyAssetsDirectory")?.Value;
        assetsDirectory = Path.GetFullPath(assetsDirectory ??
            Path.Combine(AppContext.BaseDirectory, "Assets"));
        var openBootstrapAsset = DesktopBootstrapAssets.CreateReader(AppContext.BaseDirectory);
        Run(
            ApplicationResolver.Create(),
            new PlatformContext(PlatformKind.Desktop, assetsDirectory, openBootstrapAsset));
    }
}
