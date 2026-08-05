using Vecxy.Engine;

namespace Vecxy.Platforms;

public static class PlatformRunner
{
    public static void Run(
        IEntryPoint application,
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
        where TApplication : IEntryPoint, new()
    {
        var assetsDirectory = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "Assets"));
        Run(
            new TApplication(),
            new PlatformContext(PlatformKind.Desktop, assetsDirectory));
    }
}
