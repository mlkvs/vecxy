using System.Numerics;
using Vecxy.Scene;

namespace Vecxy.Rendering;

/// <summary>Scene-space drawing with the camera's depth buffer, before post processing.</summary>
public interface IRenderWorldStage
{
    void RegisterWorld(Action<RenderWorldContext> draw);
    void UnregisterWorld(Action<RenderWorldContext> draw);
}

public readonly record struct RenderWorldContext(
    SceneInstance Scene, Camera Camera, Matrix4x4 ViewProjection, int Width, int Height);
