using Android.App;
using Android.Content.PM;
using Android.Util;
using Android.Views;
using Silk.NET.Windowing.Sdl.Android;
using Vecxy.Engine;
using Vecxy.Kernel;

namespace Vecxy.Platforms.Android;

[Activity(
    MainLauncher = true,
    Exported = true,
    HardwareAccelerated = true,
    ConfigurationChanges = SilkActivity.ConfigChangesFlags,
    Theme = "@style/VecxyTheme")]
public sealed class VecxyActivity : SilkActivity
{
    private const string LogTag = "Vecxy.Android";
    private int? _primaryTouchId;

    protected override void OnRun()
    {
        var phase = "preparing Android storage";
        try
        {
            var filesDirectory = FilesDir?.AbsolutePath ??
                throw new InvalidOperationException("Android files directory is unavailable.");
            var assetManager = Assets ??
                throw new InvalidOperationException("Android asset manager is unavailable.");
            var context = new PlatformContext(
                PlatformKind.Android,
                Path.Combine(filesDirectory, "Assets"));

            phase = "resolving the game application";
            var application = AndroidApplicationResolver.Create();
            var options = new Engine.Engine.Options();
            var layers = new List<AAppLayer.IDefinition>();

            application.OnConfigureEngine(context, options);
            
            IEngineSplashScreen? splashScreen = options.ShowSplashScreen
                ? AndroidEngineSplashScreen.Attach(
                    this,
                    assetManager,
                    options.SplashScreenLogoPath)
                : null;

            phase = "extracting packaged assets";
            var assetsDirectory = AndroidAssetExtractor.Extract(
                assetManager,
                filesDirectory,
                GetInstalledPackageVersion());

            phase = "running the game application";
            PlatformRunner.Run(
                application,
                context with { AssetsDirectory = assetsDirectory },
                options,
                splashScreen);
        }
        catch (Exception exception)
        {
            var details = $"Startup failed while {phase}: {exception}";
            Log.Error(LogTag, details);
            var filesDirectory = FilesDir?.AbsolutePath;
            if (!string.IsNullOrWhiteSpace(filesDirectory))
                File.WriteAllText(Path.Combine(filesDirectory, "vecxy-crash.txt"), details);
            throw;
        }
    }
    

    public override bool DispatchTouchEvent(MotionEvent? eventData)
    {
        if (eventData is null)
            return false;

        var action = eventData.ActionMasked;
        var actionIndex = eventData.ActionIndex;
        switch (action)
        {
            case MotionEventActions.Down:
                _primaryTouchId = eventData.GetPointerId(actionIndex);
                Publish(eventData, actionIndex, ETouchPhase.Began);
                break;
            case MotionEventActions.PointerDown:
                Publish(eventData, actionIndex, ETouchPhase.Began);
                break;
            case MotionEventActions.Move:
                for (var index = 0; index < eventData.PointerCount; index++)
                    Publish(eventData, index, ETouchPhase.Moved);
                break;
            case MotionEventActions.Up:
            case MotionEventActions.PointerUp:
                Publish(eventData, actionIndex, ETouchPhase.Ended);
                if (eventData.GetPointerId(actionIndex) == _primaryTouchId)
                    _primaryTouchId = null;
                break;
            case MotionEventActions.Cancel:
                for (var index = 0; index < eventData.PointerCount; index++)
                    Publish(eventData, index, ETouchPhase.Cancelled);
                _primaryTouchId = null;
                break;
        }

        return true;
    }

    private void Publish(MotionEvent eventData, int index, ETouchPhase phase)
    {
        var id = eventData.GetPointerId(index);
        PlatformTouchSource.Publish(new IWindow.TouchEvent(
            id,
            eventData.GetX(index),
            eventData.GetY(index),
            phase,
            eventData.GetPressure(index),
            id == _primaryTouchId));
    }

    private string GetInstalledPackageVersion()
    {
        var info = PackageManager?.GetPackageInfo(PackageName!, PackageInfoFlags.MatchAll) ??
            throw new InvalidOperationException("Android package information is unavailable.");
        return $"{info.VersionName}:{info.LastUpdateTime}";
    }
}
