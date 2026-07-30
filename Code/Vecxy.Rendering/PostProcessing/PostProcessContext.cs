using System.Numerics;

namespace Vecxy.Rendering;

public readonly record struct PostProcessContext
(
    Vector2 Resolution,
    float Time,
    Camera Camera
);