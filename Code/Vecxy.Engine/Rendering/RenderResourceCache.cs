using System.Runtime.CompilerServices;
using Vecxy.Assets;
using Vecxy.Rendering;

namespace Vecxy.Engine.Rendering;

internal sealed class RenderResourceCache(IRenderer renderer) : IDisposable
{
    private readonly Dictionary<ModelPrimitive, Mesh> _meshes = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<ModelPrimitive> _live = new(ReferenceEqualityComparer.Instance);

    public void BeginExtraction() => _live.Clear();

    public Mesh GetMesh(ModelPrimitive primitive)
    {
        _live.Add(primitive);
        if (_meshes.TryGetValue(primitive, out var mesh)) return mesh;
        var vertices = primitive.Vertices.Select(v =>
            new Vertex(v.Position, v.Normal, Color.White, v.TexCoord)).ToArray();
        mesh = renderer.CreateMesh(vertices, primitive.Indices.ToArray());
        _meshes.Add(primitive, mesh);
        return mesh;
    }

    public void EndExtraction()
    {
        foreach (var stale in _meshes.Keys.Where(x => !_live.Contains(x)).ToArray())
        {
            _meshes[stale].Dispose();
            _meshes.Remove(stale);
        }
    }

    public void Dispose()
    {
        Clear();
        _live.Clear();
    }

    public void Clear()
    {
        foreach (var mesh in _meshes.Values) mesh.Dispose();
        _meshes.Clear();
    }
}
