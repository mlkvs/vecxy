using System.Numerics;
using System.Runtime.CompilerServices;
using Vecxy.Assets;
using Vecxy.Diagnostics;
using Vecxy.Engine.Objects;
using Vecxy.Rendering;

namespace Vecxy.Engine.Scenes;

public sealed class SceneManager : IDisposable
{
    private readonly IRenderer _renderer;
    private readonly IInput _input;
    private ShaderProgram? _shader;
    private ShaderProgram? _outlineShader;
    private Material? _material;
    private Material? _outlineMaterial;
    private Selection? _selection;
    private SceneObject? _selectedObject;
    private readonly Dictionary<ModelPrimitive, Mesh> _meshCache = new(ReferenceEqualityComparer.Instance);
    private Mesh? _staticMesh;
    private int _staticSignature;

    public Scene? ActiveScene { get; private set; }
    public SceneObject? SelectedObject => _selectedObject;
    public void Select(SceneObject? sceneObject)
    {
        _selectedObject = sceneObject;
        var renderer = sceneObject?.Scripts.OfType<MeshRenderer>().FirstOrDefault();
        _selection = renderer is null ? null : new Selection(renderer);
    }

    internal SceneManager(IRenderer renderer, IInput input)
    {
        _renderer = renderer;
        _input = input;
    }

    public Scene CreateScene(string name, SceneMode mode = SceneMode.Single) => new(name, mode);

    public void Load(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (ReferenceEquals(scene, ActiveScene)) return;
        if (scene.Mode == SceneMode.Single) UnloadActive();
        ActiveScene = scene;
        scene.Start();
        _selection = null;
        _selectedObject = null;
    }

    public void UnloadActive()
    {
        ActiveScene?.Dispose();
        ActiveScene = null;
        _selection = null;
        _selectedObject = null;
    }

    internal void Update(float deltaTime)
    {
        var scene = ActiveScene;
        if (scene is null) return;
        scene.Update(deltaTime);
        if (_selection is { } selected && !scene.Traverse().Contains(selected.Renderer.SceneObject)) _selection = null;
        if (_input.ConsumeLeftMousePressed() && !_input.IsRightMouseDown) SelectAtCursor(scene);
    }

    internal void Render(IRenderer renderer)
    {
        var scene = ActiveScene;
        if (scene is null) return;
        var cameraObject = scene.Traverse().FirstOrDefault(x => x.IsActive && x.GetScript<CameraScript>() is not null);
        var camera = cameraObject?.GetScript<CameraScript>();
        if (camera is null) return;
        EnsureMaterials();
        renderer.Camera = camera.Sync();

        var visible = scene.Traverse().Where(x => x.IsActive)
            .SelectMany(x => x.Scripts.OfType<MeshRenderer>()).Where(x => x.IsVisible).ToArray();
        foreach (var meshRenderer in visible)
        {
            meshRenderer.Prepare(GetMesh);
        }
        var staticRenderers = visible.Where(x => x.IsStatic).ToArray();
        if (staticRenderers.Length > 0)
        {
            RebuildStaticBatchIfNeeded(staticRenderers);
            renderer.Submit(_staticMesh!, _material!, Matrix4x4.Identity);
            renderer.MarkStaticBatch(staticRenderers.Length);
        }
        foreach (var meshRenderer in visible.Where(x => !x.IsStatic))
            renderer.Submit(meshRenderer.Mesh!, _material!, meshRenderer.Transform.WorldMatrix);
        if (_selection is { } selected && _outlineMaterial is not null &&
            selected.Renderer.SceneObject.IsActive && selected.Renderer.IsVisible && selected.Renderer.Mesh is not null)
            renderer.Submit(selected.Renderer.Mesh, _outlineMaterial, selected.Renderer.Transform.WorldMatrix);
    }

    private Mesh GetMesh(ModelPrimitive primitive)
    {
        if (_meshCache.TryGetValue(primitive, out var mesh)) return mesh;
        var vertices = primitive.Vertices.Select(v =>
            new Vertex(v.Position, v.Normal, Color.White, v.TexCoord)).ToArray();
        mesh = _renderer.CreateMesh(vertices, primitive.Indices.ToArray());
        _meshCache.Add(primitive, mesh);
        return mesh;
    }

    private void RebuildStaticBatchIfNeeded(IReadOnlyList<MeshRenderer> renderers)
    {
        var signature = new HashCode();
        foreach (var renderer in renderers)
        {
            signature.Add(RuntimeHelpers.GetHashCode(renderer.MeshData));
            signature.Add(renderer.Transform.WorldMatrix);
        }
        var value = signature.ToHashCode();
        if (_staticMesh is not null && value == _staticSignature) return;

        var vertices = new List<Vertex>();
        var indices = new List<uint>();
        foreach (var renderer in renderers)
        {
            var transform = renderer.Transform.WorldMatrix;
            Matrix4x4.Invert(transform, out var inverse);
            var normalTransform = Matrix4x4.Transpose(inverse);
            var offset = (uint)vertices.Count;
            foreach (var vertex in renderer.MeshData.Vertices)
                vertices.Add(new Vertex(Vector3.Transform(vertex.Position, transform),
                    Vector3.Normalize(Vector3.TransformNormal(vertex.Normal, normalTransform)), Color.White,
                    vertex.TexCoord));
            var sourceIndices = renderer.MeshData.Indices;
            var mirrored = transform.GetDeterminant() < 0f;
            for (var index = 0; index + 2 < sourceIndices.Count; index += 3)
            {
                indices.Add(offset + sourceIndices[index]);
                indices.Add(offset + sourceIndices[index + (mirrored ? 2 : 1)]);
                indices.Add(offset + sourceIndices[index + (mirrored ? 1 : 2)]);
            }
        }
        _staticMesh?.Dispose();
        _staticMesh = _renderer.CreateMesh(vertices.ToArray(), indices.ToArray());
        _staticSignature = value;
    }

    private void EnsureMaterials()
    {
        if (_material is not null) return;
        var assets = AssetsManager.Instance;
        var vertex = assets?.Get<TextAsset>("Shaders/basic.vert");
        var fragment = assets?.Get<TextAsset>("Shaders/basic.frag");
        if (vertex is null || fragment is null) _material = _renderer.FallbackMaterial;
        else
        {
            _shader = _renderer.CreateShader(vertex, fragment);
            _material = _renderer.CreateMaterial(_shader);
        }
        _material.DepthTest = true;
        _material.CullMode = CullMode.Back;
        var outlineVertex = assets?.Get<TextAsset>("Shaders/outline.vert");
        var outlineFragment = assets?.Get<TextAsset>("Shaders/outline.frag");
        if (outlineVertex is null || outlineFragment is null) return;
        _outlineShader = _renderer.CreateShader(outlineVertex, outlineFragment);
        _outlineMaterial = _renderer.CreateMaterial(_outlineShader);
        _outlineMaterial.DepthTest = true;
        _outlineMaterial.Blending = false;
        _outlineMaterial.CullMode = CullMode.Front;
    }

    private void SelectAtCursor(Scene scene)
    {
        var camera = scene.Traverse().Select(x => x.GetScript<CameraScript>()).FirstOrDefault(x => x is not null);
        if (camera is null) return;
        if (!_renderer.ScreenBounds.Contains(_input.MousePosition)) return;
        var localPointer = _input.MousePosition - new System.Numerics.Vector2(_renderer.ScreenBounds.X, _renderer.ScreenBounds.Y);
        var ray = Ray3.FromScreen(localPointer, _renderer.Width, _renderer.Height, camera.Sync());
        var nearestDistance = float.PositiveInfinity;
        Selection? nearest = null;
        foreach (var meshRenderer in scene.Traverse().Where(x => x.IsActive)
                     .SelectMany(x => x.Scripts.OfType<MeshRenderer>()).Where(x => x.IsVisible))
        {
            if (meshRenderer!.Mesh is null ||
                !meshRenderer.Mesh.Intersects(ray, meshRenderer.Transform.WorldMatrix, out var distance) ||
                distance >= nearestDistance) continue;
            nearestDistance = distance;
            nearest = new Selection(meshRenderer);
        }
        _selection = nearest;
        _selectedObject = nearest?.Renderer.SceneObject;
        Logger.Info(nearest is null ? "Selection cleared." : $"Selected object: {nearest.Value.Renderer.SceneObject.Name}");
    }

    public void Dispose()
    {
        UnloadActive();
        _shader?.Dispose();
        _outlineShader?.Dispose();
        _staticMesh?.Dispose();
        foreach (var mesh in _meshCache.Values) mesh.Dispose();
        _meshCache.Clear();
    }

    private readonly record struct Selection(MeshRenderer Renderer);
}
