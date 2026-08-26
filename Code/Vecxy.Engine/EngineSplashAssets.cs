namespace Vecxy.Engine;

public static class EngineSplashAssets
{
    private const string LogoResourceName = "Vecxy.Engine.Splash.Logo.png";
    private const string FmodLogoResourceName = "Vecxy.Engine.Splash.FmodLogo.png";

    public static Stream? OpenLogo() =>
        typeof(EngineSplashAssets).Assembly.GetManifestResourceStream(LogoResourceName);

    public static Stream? OpenFmodLogo() =>
        typeof(EngineSplashAssets).Assembly.GetManifestResourceStream(FmodLogoResourceName);
}
