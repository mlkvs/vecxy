namespace Vecxy.Rendering;

public sealed class RenderingStatistics
{
    private float _smoothedDeltaTime;

    public float FrameTimeMilliseconds => _smoothedDeltaTime * 1000.0f;
    public float FramesPerSecond =>
        _smoothedDeltaTime > 0.0f
            ? 1.0f / _smoothedDeltaTime
            : 0.0f;

    public int DrawCalls { get; private set; }
    public int RenderItems { get; private set; }
    public int ActiveViews { get; private set; }

    internal void BeginFrame(float deltaTime, int activeViews)
    {
        var safeDeltaTime = Math.Max(0.0f, deltaTime);
        _smoothedDeltaTime = _smoothedDeltaTime <= 0.0f
            ? safeDeltaTime
            : _smoothedDeltaTime * 0.9f + safeDeltaTime * 0.1f;

        DrawCalls = 0;
        RenderItems = 0;
        ActiveViews = activeViews;
    }

    internal void RecordDraw()
    {
        DrawCalls++;
        RenderItems++;
    }
}
