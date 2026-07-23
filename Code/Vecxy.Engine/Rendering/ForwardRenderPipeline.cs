using System.Numerics;
using Vecxy.Assets;
using Vecxy.Rendering;

namespace Vecxy.Engine.Rendering;

internal sealed class ForwardRenderPipeline : IDisposable
{
    private readonly IRenderer _renderer;
    private ShaderProgram? _shader;
    private ShaderProgram? _outlineShader;
    private Material? _opaqueMaterial;
    private Material? _outlineMaterial;

    public ForwardRenderPipeline(IRenderer renderer) => _renderer = renderer;

    public void Render(RenderWorld world)
    {
        if (world.Camera is null) return;
        EnsureMaterials();
        var camera = world.Camera.Sync();
        _renderer.Camera = camera;
        var viewProjection = camera.GetViewProjection(_renderer.Width, _renderer.Height);

        foreach (var item in world.Objects)
        {
            if (!IsInsideFrustum(item.WorldBounds, viewProjection)) continue;
            _renderer.Submit(item.Mesh, _opaqueMaterial!, item.World);
        }

        if (world.Selected is { Mesh: not null } selected && selected.SceneObject.IsActive && selected.IsVisible)
            _renderer.Submit(selected.Mesh, _outlineMaterial!, selected.Transform.WorldMatrix, RenderPass.Selection);
    }

    private void EnsureMaterials()
    {
        if (_opaqueMaterial is not null) return;
        var assets = AssetsManager.Instance;
        var vertex = assets?.Get<TextAsset>("Shaders/basic.vert");
        var fragment = assets?.Get<TextAsset>("Shaders/basic.frag");
        if (vertex is null || fragment is null) _opaqueMaterial = _renderer.FallbackMaterial;
        else
        {
            _shader = _renderer.CreateShader(vertex, fragment);
            _opaqueMaterial = _renderer.CreateMaterial(_shader);
        }
        _opaqueMaterial.DepthTest = true;
        _opaqueMaterial.Blending = false;
        _opaqueMaterial.CullMode = CullMode.Back;

        var outlineVertex = assets?.Get<TextAsset>("Shaders/outline.vert");
        var outlineFragment = assets?.Get<TextAsset>("Shaders/outline.frag");
        if (outlineVertex is null || outlineFragment is null)
        {
            _outlineMaterial = _opaqueMaterial;
            return;
        }
        _outlineShader = _renderer.CreateShader(outlineVertex, outlineFragment);
        _outlineMaterial = _renderer.CreateMaterial(_outlineShader);
        _outlineMaterial.DepthTest = true;
        _outlineMaterial.Blending = false;
        _outlineMaterial.CullMode = CullMode.Front;
    }

    private static bool IsInsideFrustum(Bounds3 bounds, Matrix4x4 viewProjection)
    {
        Span<Vector3> corners =
        [
            new(bounds.Min.X,bounds.Min.Y,bounds.Min.Z), new(bounds.Max.X,bounds.Min.Y,bounds.Min.Z),
            new(bounds.Min.X,bounds.Max.Y,bounds.Min.Z), new(bounds.Max.X,bounds.Max.Y,bounds.Min.Z),
            new(bounds.Min.X,bounds.Min.Y,bounds.Max.Z), new(bounds.Max.X,bounds.Min.Y,bounds.Max.Z),
            new(bounds.Min.X,bounds.Max.Y,bounds.Max.Z), new(bounds.Max.X,bounds.Max.Y,bounds.Max.Z)
        ];
        Span<Vector4> clip = stackalloc Vector4[8];
        for (var i = 0; i < corners.Length; i++) clip[i] = Vector4.Transform(new Vector4(corners[i], 1f), viewProjection);
        return !All(clip, p => p.X < -p.W) && !All(clip, p => p.X > p.W) &&
               !All(clip, p => p.Y < -p.W) && !All(clip, p => p.Y > p.W) &&
               !All(clip, p => p.Z < -p.W) && !All(clip, p => p.Z > p.W);
    }

    private static bool All(ReadOnlySpan<Vector4> values, Func<Vector4, bool> predicate)
    {
        foreach (var value in values) if (!predicate(value)) return false;
        return true;
    }

    public void Dispose()
    {
        _shader?.Dispose();
        _outlineShader?.Dispose();
    }
}
