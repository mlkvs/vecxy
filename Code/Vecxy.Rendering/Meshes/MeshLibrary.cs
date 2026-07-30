using Vecxy.Assets;
using Vecxy.Diagnostics;

namespace Vecxy.Rendering;

public sealed class MeshLibrary : IDisposable
{
    private readonly IAssetsManager _assets;
    private readonly GraphicsDevice _device;
    private readonly Dictionary<AssetId, Entry> _models = [];
    private bool _disposed;

    public MeshLibrary(
        IAssetsManager assets,
        GraphicsDevice device)
    {
        _assets = assets;
        _device = device;
        _assets.Unloaded += OnAssetUnloaded;
    }

    public IReadOnlyList<Mesh> Get(
        Model model,
        int meshIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(model);

        var asset = model.Source;

        if (_models.TryGetValue(asset.Id, out var entry))
        {
            if (entry.AssetVersion != asset.Version)
                Reload(asset, entry);

            return GetMesh(entry.Meshes, meshIndex, asset.Metadata.Path);
        }

        var meshes = CreateMeshes(asset.Value, asset.Metadata.Path);
        entry = new Entry(asset.Version, meshes);
        _models.Add(asset.Id, entry);
        return GetMesh(meshes, meshIndex, asset.Metadata.Path);
    }

    private void Reload(
        AssetRef<ModelAsset> asset,
        Entry entry)
    {
        try
        {
            var replacement = CreateMeshes(asset.Value, asset.Metadata.Path);
            var previous = entry.Meshes;

            entry.Meshes = replacement;
            entry.AssetVersion = asset.Version;

            DisposeMeshes(previous);
        }
        catch (Exception exception)
        {
            entry.AssetVersion = asset.Version;
            Logger.Error(
                exception,
                $"Model GPU reload failed, keeping previous meshes: {asset.Metadata.Path}");
        }
    }

    private Mesh[][] CreateMeshes(
        ModelAsset asset,
        string path)
    {
        var result = new Mesh[asset.Meshes.Count][];
        var created = new List<Mesh>();

        try
        {
            for (var meshIndex = 0;
                 meshIndex < asset.Meshes.Count;
                 meshIndex++)
            {
                var sourceMesh = asset.Meshes[meshIndex];
                var primitives =
                    new Mesh[sourceMesh.Primitives.Count];

                for (var primitiveIndex = 0;
                     primitiveIndex < sourceMesh.Primitives.Count;
                     primitiveIndex++)
                {
                    var primitive =
                        sourceMesh.Primitives[primitiveIndex];
                    var vertices =
                        PackVertices(primitive.Vertices);
                    var indices = primitive.Indices.ToArray();

                    CalculateBounds(
                        primitive.Vertices,
                        out var boundsMin,
                        out var boundsMax);

                    var mesh = new Mesh(
                        _device,
                        vertices,
                        indices,
                        8,
                        boundsMin,
                        boundsMax,
                        $"{path} / Mesh {meshIndex} / Primitive {primitiveIndex}",
                        new VertexAttribute(0, 3, 0),
                        new VertexAttribute(1, 3, 3),
                        new VertexAttribute(2, 2, 6));

                    primitives[primitiveIndex] = mesh;
                    created.Add(mesh);
                }

                result[meshIndex] = primitives;
            }

            return result;
        }
        catch
        {
            foreach (var mesh in created)
                mesh.Dispose();

            throw;
        }
    }

    private static float[] PackVertices(
        IReadOnlyList<ModelVertex> vertices)
    {
        var result = new float[checked(vertices.Count * 8)];

        for (var index = 0; index < vertices.Count; index++)
        {
            var vertex = vertices[index];
            var offset = index * 8;

            result[offset] = vertex.Position.X;
            result[offset + 1] = vertex.Position.Y;
            result[offset + 2] = vertex.Position.Z;
            result[offset + 3] = vertex.Normal.X;
            result[offset + 4] = vertex.Normal.Y;
            result[offset + 5] = vertex.Normal.Z;
            result[offset + 6] = vertex.TexCoord.X;
            result[offset + 7] = vertex.TexCoord.Y;
        }

        return result;
    }

    private static void CalculateBounds(
        IReadOnlyList<ModelVertex> vertices,
        out System.Numerics.Vector3 min,
        out System.Numerics.Vector3 max)
    {
        if (vertices.Count == 0)
            throw new InvalidOperationException(
                "Mesh primitive has no vertices.");

        min = new System.Numerics.Vector3(float.MaxValue);
        max = new System.Numerics.Vector3(float.MinValue);

        for (var index = 0; index < vertices.Count; index++)
        {
            var position = vertices[index].Position;
            min = System.Numerics.Vector3.Min(min, position);
            max = System.Numerics.Vector3.Max(max, position);
        }
    }

    private static IReadOnlyList<Mesh> GetMesh(
        IReadOnlyList<Mesh[]> meshes,
        int meshIndex,
        string path)
    {
        if (meshIndex < 0 || meshIndex >= meshes.Count)
        {
            throw new InvalidOperationException(
                $"Model '{path}' no longer contains mesh {meshIndex}.");
        }

        return meshes[meshIndex];
    }

    private void OnAssetUnloaded(
        AssetId id,
        Type assetType)
    {
        if (assetType == typeof(ModelAsset) &&
            _models.Remove(id, out var entry))
        {
            DisposeMeshes(entry.Meshes);
        }
    }

    private static void DisposeMeshes(
        IEnumerable<IEnumerable<Mesh>> meshes)
    {
        foreach (var mesh in meshes.SelectMany(value => value))
            mesh.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _assets.Unloaded -= OnAssetUnloaded;

        foreach (var entry in _models.Values)
            DisposeMeshes(entry.Meshes);

        _models.Clear();
    }

    private sealed class Entry(
        int assetVersion,
        Mesh[][] meshes)
    {
        public int AssetVersion { get; set; } =
            assetVersion;

        public Mesh[][] Meshes { get; set; } =
            meshes;
    }
}
