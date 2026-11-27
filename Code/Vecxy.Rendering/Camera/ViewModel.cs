using Vecxy.Scene;

namespace Vecxy.Rendering;

/// <summary>Renders this hierarchy over the world for its owning camera, with independent depth testing.</summary>
[SingleComponent]
public sealed class ViewModel(Camera camera) : AComponent
{
    public Camera Camera { get; } = camera;
}
