using VecxyEngine = Vecxy.Engine.Engine;

namespace Vecxy.Platforms;

public static class PlatformRunner
{
    public static void Run(IVecxyApplication application, PlatformContext context)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(context);

        using var engine = new VecxyEngine(
            application.CreateEngineOptions(context),
            application.CreateLayers(context));
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
