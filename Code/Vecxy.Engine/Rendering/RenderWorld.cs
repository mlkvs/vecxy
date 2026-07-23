using System.Numerics;
using Vecxy.Assets;
using Vecxy.Engine.Objects;
using Vecxy.Rendering;

namespace Vecxy.Engine.Rendering;

public sealed class RenderWorld
{
    public CameraScript? Camera { get; internal set; }
    public List<RenderObject> Objects { get; } = [];
    public MeshRenderer? Selected { get; internal set; }
}

public readonly record struct RenderObject(
    SceneObject SceneObject,
    MeshRenderer Renderer,
    ModelPrimitive MeshData,
    Mesh Mesh,
    Matrix4x4 World,
    Bounds3 WorldBounds);
