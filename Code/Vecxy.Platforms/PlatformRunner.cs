using Vecxy.Engine;

namespace Vecxy.Platforms;

public static class PlatformRunner
{
    public static void Run(
        IVecxyApplication application,
        PlatformContext context,
        Engine.Engine.Options? options = null,
        IEngineSplashScreen? splashScreen = null)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(context);

        options ??= application.CreateEngineOptions(context);
        using var engine = new Engine.Engine(
            options,
            application.CreateLayers(context),
            context.AssetsDirectory,
            splashScreen);
        engine.Run();
    }

    public static void RunDesktop<TApplication>()
        where TApplication : IVecxyApplication, new()
    {
        var assetsDirectory = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "Assets"));
        Run(
            new TApplication(),
            new PlatformContext(PlatformKind.Desktop, assetsDirectory));
    }
}
