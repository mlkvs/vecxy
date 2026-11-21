using System.Numerics;
using Silk.NET.OpenGL;
using Vecxy.Assets;
using Vecxy.Scene;

namespace Vecxy.Rendering;

internal sealed class OutlineRenderer(
    IAssetsManager assets,
    GraphicsDevice device,
    ShaderLibrary shaders,
    TextureLibrary textures,
    RenderingStatistics statistics,
    Action<MeshRenderer, Shader, Matrix4x4, Matrix4x4> drawMesh) : IDisposable
{
    private readonly List<Outline> _outlines = [];
    private readonly List<MeshRenderer> _members = [];
    private AssetRef<ShaderAsset>? _maskSource;
    private AssetRef<ShaderAsset>? _compositeSource;
    private SceneRenderTarget? _mask;
    private SceneRenderTarget? _depth;

    public void Draw(SceneInstance scene, SceneRenderTarget target, Mesh quad,
        Matrix4x4 viewProjection, IReadOnlyList<MeshRenderer> renderers)
    {
        _outlines.Clear();
        foreach (var obj in scene.Objects)
            if (obj.IsActive)
                obj.GetComponents(_outlines);
        _outlines.RemoveAll(static outline => !outline.IsActive || outline.Thickness <= 0 || outline.Opacity <= 0);
        if (_outlines.Count == 0)
            return;
        _outlines.Sort(static (a, b) =>
        {
            var order = a.Order.CompareTo(b.Order);
            return order != 0 ? order : a.SceneObject!.Id.CompareTo(b.SceneObject!.Id);
        });

        _maskSource ??= assets.Load<ShaderAsset>("Shaders/Outline/Mask.glsl");
        _compositeSource ??= assets.Load<ShaderAsset>("Shaders/Outline/Composite.glsl");
        var maskShader = shaders.Get(_maskSource);
        var compositeShader = shaders.Get(_compositeSource);
        var fallback = shaders.GetFallback();
        if (ReferenceEquals(maskShader, fallback) || ReferenceEquals(compositeShader, fallback))
            return;

        var gl = device.GL;
        try
        {
            _mask ??= new SceneRenderTarget(device, sampleDepth: true);
            _mask.EnsureSize(target.Width, target.Height);
            var needsDepth = _outlines.Exists(static outline => !outline.ThroughWalls);
            if (needsDepth)
            {
                _depth ??= new SceneRenderTarget(device, sampleDepth: true);
                _depth.EnsureSize(target.Width, target.Height);
                target.CopyDepthTo(_depth);
            }

            foreach (var outline in _outlines)
            {
                CollectMembers(outline, renderers);
                if (_members.Count == 0)
                    continue;
                var bounds = GetScreenBounds(target, viewProjection, outline.Thickness);
                if (bounds.Width == 0 || bounds.Height == 0)
                    continue;

                _mask.Bind(device);
                gl.Enable(EnableCap.DepthTest);
                gl.DepthFunc(DepthFunction.Less);
                gl.DepthMask(true);
                gl.Disable(EnableCap.Blend);
                gl.ClearColor(0, 0, 0, 0);
                gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                maskShader.Bind();
                foreach (var renderer in _members)
                {
                    BindAlpha(renderer.Material, maskShader);
                    drawMesh(renderer, maskShader, renderer.Transform.WorldMatrix, viewProjection);
                }

                target.Bind(device);
                gl.Disable(EnableCap.DepthTest);
                gl.DepthMask(false);
                gl.Enable(EnableCap.Blend);
                gl.BlendFuncSeparate(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha,
                    BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
                gl.Enable(EnableCap.ScissorTest);
                gl.Scissor(bounds.X, bounds.Y, bounds.Width, bounds.Height);
                try
                {
                    compositeShader.Bind();
                    _mask.BindColorTexture(0);
                    _mask.BindDepthTexture(1);
                    if (!outline.ThroughWalls)
                        _depth!.BindDepthTexture(2);
                    compositeShader.Set("uMask", 0);
                    compositeShader.Set("uMaskDepth", 1);
                    compositeShader.Set("uSceneDepth", outline.ThroughWalls ? 1 : 2);
                    compositeShader.Set("uColor", new Vector4(outline.Color, outline.Opacity));
                    compositeShader.Set("uThickness", outline.Thickness);
                    compositeShader.Set("uThroughWalls", outline.ThroughWalls ? 1 : 0);
                    quad.Draw();
                    statistics.RecordDraw();
                }
                finally
                {
                    gl.Disable(EnableCap.ScissorTest);
                }
            }
        }
        finally
        {
            target.Bind(device);
            gl.Disable(EnableCap.Blend);
            gl.Enable(EnableCap.DepthTest);
            gl.DepthMask(true);
            gl.DepthFunc(DepthFunction.Less);
        }
    }

    private void CollectMembers(Outline outline, IReadOnlyList<MeshRenderer> renderers)
    {
        _members.Clear();
        foreach (var renderer in renderers)
        {
            var owner = renderer.SceneObject;
            while (owner is not null)
            {
                if (owner == outline.SceneObject)
                {
                    _members.Add(renderer);
                    break;
                }
                owner = outline.IncludeChildren ? owner.Parent : null;
            }
        }
    }

    private void BindAlpha(Material material, Shader shader)
    {
        var alpha = 1f;
        var tiling = Vector2.One;
        var offset = Vector2.Zero;
        var sampler = TextureSamplerState.Default;
        Texture? texture = null;
        foreach (var (key, value) in material.Parameters)
        {
            if ((key is "uColor" or "uTint") && value is VectorMaterialParameter tint)
                alpha *= tint.Value.W;
            if (key != "uTexture")
                continue;
            switch (value)
            {
                case TextureMaterialParameter reference when !reference.Texture.HasError:
                    texture = textures.Get(reference.Texture);
                    (tiling, offset, sampler) = (reference.Tiling, reference.Offset, reference.Sampler);
                    break;
                case EmbeddedTextureMaterialParameter embedded:
                    texture = textures.Get(embedded.Texture);
                    (tiling, offset, sampler) = (embedded.Tiling, embedded.Offset, embedded.Sampler);
                    break;
            }
        }

        (texture ?? textures.GetWhite()).Bind(0, sampler);
        shader.Set("uTexture", 0);
        shader.Set("uTextureTiling", tiling);
        shader.Set("uTextureOffset", offset);
        shader.Set("uAlpha", alpha);
        shader.Set("uAlphaCutoff", Math.Max(material.AlphaCutoff, .001f));
    }

    private ScreenBounds GetScreenBounds(SceneRenderTarget target, Matrix4x4 viewProjection, float thickness)
    {
        var low = new Vector2(float.PositiveInfinity);
        var high = new Vector2(float.NegativeInfinity);
        Span<Vector4> corners = stackalloc Vector4[8];
        foreach (var renderer in _members)
        {
            var matrix = renderer.Transform.WorldMatrix * viewProjection;
            var (min, max) = GetLocalBounds(renderer);
            var commonOutside = 63;
            var crossesNearPlane = false;
            for (var corner = 0; corner < corners.Length; corner++)
            {
                var clip = Vector4.Transform(new Vector4(
                    (corner & 1) == 0 ? min.X : max.X,
                    (corner & 2) == 0 ? min.Y : max.Y,
                    (corner & 4) == 0 ? min.Z : max.Z, 1), matrix);
                corners[corner] = clip;
                commonOutside &= (clip.X < -clip.W ? 1 : 0) | (clip.X > clip.W ? 2 : 0)
                    | (clip.Y < -clip.W ? 4 : 0) | (clip.Y > clip.W ? 8 : 0)
                    | (clip.Z < -clip.W ? 16 : 0) | (clip.Z > clip.W ? 32 : 0);
                crossesNearPlane |= clip.W <= 0 || clip.Z < -clip.W || !float.IsFinite(clip.W);
            }
            if (commonOutside != 0)
                continue;
            // Near-plane clipping can introduce projected corners outside the box projections.
            if (crossesNearPlane)
                return new ScreenBounds(0, 0, (uint)target.Width, (uint)target.Height);
            foreach (var clip in corners)
            {
                var projected = new Vector2(clip.X, clip.Y) / clip.W;
                low = Vector2.Min(low, projected);
                high = Vector2.Max(high, projected);
            }
        }
        if (!float.IsFinite(low.X))
            return default;

        var half = new Vector2(target.Width * .5f, target.Height * .5f);
        var padding = new Vector2(thickness + 2);
        low = (low + Vector2.One) * half - padding;
        high = (high + Vector2.One) * half + padding;
        var left = (int)Math.Clamp(MathF.Floor(low.X), 0, target.Width);
        var right = (int)Math.Clamp(MathF.Ceiling(high.X), 0, target.Width);
        var bottom = (int)Math.Clamp(MathF.Floor(low.Y), 0, target.Height);
        var top = (int)Math.Clamp(MathF.Ceiling(high.Y), 0, target.Height);
        return new ScreenBounds(left, bottom, (uint)(right - left), (uint)(top - bottom));
    }

    private static (Vector3 Min, Vector3 Max) GetLocalBounds(MeshRenderer renderer)
    {
        var mesh = renderer.Mesh;
        if (renderer is not SkinnedMeshRenderer skinned || skinned.BoneMatrixSpan.IsEmpty)
            return (mesh.BoundsMin, mesh.BoundsMax);

        // Imported skin weights are nonnegative and normalized. The union of
        // all bone-transformed boxes contains every blended vertex.
        var min = new Vector3(float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity);
        var half = mesh.BoundsSize * .5f;
        foreach (var bone in skinned.BoneMatrixSpan)
        {
            var center = Vector3.Transform(mesh.BoundsCenter, bone);
            var extent = Vector3.Abs(Vector3.TransformNormal(new Vector3(half.X, 0, 0), bone))
                + Vector3.Abs(Vector3.TransformNormal(new Vector3(0, half.Y, 0), bone))
                + Vector3.Abs(Vector3.TransformNormal(new Vector3(0, 0, half.Z), bone));
            min = Vector3.Min(min, center - extent);
            max = Vector3.Max(max, center + extent);
        }
        return (min, max);
    }

    public void Dispose()
    {
        _mask?.Dispose();
        _depth?.Dispose();
        _maskSource?.Dispose();
        _compositeSource?.Dispose();
    }

    private readonly record struct ScreenBounds(int X, int Y, uint Width, uint Height);
}
