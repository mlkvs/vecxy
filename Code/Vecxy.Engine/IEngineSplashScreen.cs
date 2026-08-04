namespace Vecxy.Engine;

/// <summary>
/// Platform splash screens receive engine startup progress and are dismissed
/// only after the first frame has been rendered successfully.
/// </summary>
public interface IEngineSplashScreen : IDisposable
{
    void ReportProgress(float progress);

    void PrepareForFirstFrame();

    void Dismiss();
}
