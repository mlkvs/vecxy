using Vecxy.Rendering;

namespace Vecxy.Engine;

public sealed class EngineOptions
{
    public string WindowTitle { get; init; } = "Vecxy";
    public int WindowWidth { get; init; } = 800;
    public int WindowHeight { get; init; } = 600;
    public int TargetFrameRate { get; init; } = 60;
    public string AssetsPath { get; init; } = Path.Combine(AppContext.BaseDirectory, "Assets");
    public bool UsePackedAssets { get; init; }
    public Color ClearColor { get; init; } = Color.CornflowerBlue;
}
