using System.Buffers.Binary;
using System.Numerics;
using glTFLoader;
using glTFLoader.Schema;

namespace Vecxy.Assets._Legacy;

public sealed class ModelAsset : Asset, IHotReloadableAsset
{
    public override EAssetType Type => EAssetType.Model;
    public IReadOnlyList<ModelNode> Nodes { get; private set; } = [];

    public override void Load(byte[] data)
    {
        using var modelStream = new MemoryStream(data, writable: false);
        var model = Interface.LoadModel(modelStream);
        using var binaryStream = new MemoryStream(data, writable: false);
        var binaryBuffer = Interface.LoadBinaryBuffer(binaryStream);
        Nodes = ConvertDefaultScene(model, binaryBuffer);
    }

    public void OnHotReload(byte[] newData)
    {
        Load(newData);
        NotifyReloaded();
    }

    private static IReadOnlyList<ModelNode> ConvertDefaultScene(Gltf model, byte[] buffer)
    {
        if (model.Scenes is null || model.Scenes.Length == 0) return [];
        var meshes = (model.Meshes ?? []).Select(mesh => mesh.Primitives.Select(primitive =>
        {
            if ((int)primitive.Mode != 4) throw new NotSupportedException("Only glTF triangle primitives are supported.");
            var positions = ReadVector3(model, buffer, primitive.Attributes["POSITION"]);
            var normals = primitive.Attributes.TryGetValue("NORMAL", out var normalAccessor)
                ? ReadVector3(model, buffer, normalAccessor)
                : Enumerable.Repeat(Vector3.UnitY, positions.Length).ToArray();
            var texCoords = primitive.Attributes.TryGetValue("TEXCOORD_0", out var uvAccessor)
                ? ReadVector2(model, buffer, uvAccessor)
                : new Vector2[positions.Length];
            var indices = primitive.Indices.HasValue
                ? ReadIndices(model, buffer, primitive.Indices.Value)
                : Enumerable.Range(0, positions.Length).Select(i => (uint)i).ToArray();
            var vertices = new ModelVertex[positions.Length];
            for (var i = 0; i < vertices.Length; i++)
                vertices[i] = new ModelVertex(positions[i], normals[i], texCoords[i]);
            return new ModelPrimitive(vertices, indices);
        }).ToArray()).ToArray();

        var result = new List<ModelNode>();
        var scene = model.Scenes[model.Scene ?? 0];
        foreach (var root in scene.Nodes ?? []) result.Add(CreateNode(model, meshes, root));
        return result;
    }

    private static ModelNode CreateNode(Gltf model, ModelPrimitive[][] meshes, int index)
    {
        var node = model.Nodes[index];
        var primitives = node.Mesh.HasValue ? meshes[node.Mesh.Value] : [];
        var children = (node.Children ?? []).Select(child => CreateNode(model, meshes, child)).ToArray();
        return new ModelNode(node.Name ?? $"Node{index}", ReadTransform(node), primitives, children);
    }

    private static Matrix4x4 ReadTransform(Node node)
    {
        if (node.Matrix is { Length: 16 } m)
            return new Matrix4x4(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7],
                m[8], m[9], m[10], m[11], m[12], m[13], m[14], m[15]);
        var t = node.Translation is { Length: 3 } translation
            ? new Vector3(translation[0], translation[1], translation[2]) : Vector3.Zero;
        var s = node.Scale is { Length: 3 } scale
            ? new Vector3(scale[0], scale[1], scale[2]) : Vector3.One;
        var r = node.Rotation is { Length: 4 } rotation
            ? new Quaternion(rotation[0], rotation[1], rotation[2], rotation[3]) : Quaternion.Identity;
        return Matrix4x4.CreateScale(s) * Matrix4x4.CreateFromQuaternion(r) * Matrix4x4.CreateTranslation(t);
    }

    private static Vector3[] ReadVector3(Gltf model, byte[] buffer, int index)
    {
        var accessor = model.Accessors[index];
        Require(accessor, Accessor.ComponentTypeEnum.FLOAT, Accessor.TypeEnum.VEC3);
        var result = new Vector3[accessor.Count];
        for (var i = 0; i < result.Length; i++)
        {
            var span = Element(model, buffer, accessor, i, 12);
            result[i] = new Vector3(ReadFloat(span), ReadFloat(span[4..]), ReadFloat(span[8..]));
        }
        return result;
    }

    private static Vector2[] ReadVector2(Gltf model, byte[] buffer, int index)
    {
        var accessor = model.Accessors[index];
        Require(accessor, Accessor.ComponentTypeEnum.FLOAT, Accessor.TypeEnum.VEC2);
        var result = new Vector2[accessor.Count];
        for (var i = 0; i < result.Length; i++)
        {
            var span = Element(model, buffer, accessor, i, 8);
            result[i] = new Vector2(ReadFloat(span), ReadFloat(span[4..]));
        }
        return result;
    }

    private static uint[] ReadIndices(Gltf model, byte[] buffer, int index)
    {
        var accessor = model.Accessors[index];
        if (accessor.Type != Accessor.TypeEnum.SCALAR || accessor.ComponentType is not
            (Accessor.ComponentTypeEnum.UNSIGNED_BYTE or Accessor.ComponentTypeEnum.UNSIGNED_SHORT or Accessor.ComponentTypeEnum.UNSIGNED_INT))
            throw new NotSupportedException("Unsupported glTF index format.");
        var size = accessor.ComponentType switch
        {
            Accessor.ComponentTypeEnum.UNSIGNED_BYTE => 1,
            Accessor.ComponentTypeEnum.UNSIGNED_SHORT => 2,
            _ => 4
        };
        var result = new uint[accessor.Count];
        for (var i = 0; i < result.Length; i++)
        {
            var span = Element(model, buffer, accessor, i, size);
            result[i] = accessor.ComponentType switch
            {
                Accessor.ComponentTypeEnum.UNSIGNED_BYTE => span[0],
                Accessor.ComponentTypeEnum.UNSIGNED_SHORT => BinaryPrimitives.ReadUInt16LittleEndian(span),
                _ => BinaryPrimitives.ReadUInt32LittleEndian(span)
            };
        }
        return result;
    }

    private static ReadOnlySpan<byte> Element(Gltf model, byte[] buffer, Accessor accessor, int element, int size)
    {
        if (accessor.Sparse is not null) throw new NotSupportedException("Sparse glTF accessors are not supported yet.");
        var view = model.BufferViews[accessor.BufferView ?? throw new InvalidDataException("Accessor has no buffer view.")];
        var stride = view.ByteStride ?? size;
        return buffer.AsSpan(checked(view.ByteOffset + accessor.ByteOffset + element * stride), size);
    }

    private static void Require(Accessor accessor, Accessor.ComponentTypeEnum component, Accessor.TypeEnum type)
    {
        if (accessor.ComponentType != component || accessor.Type != type)
            throw new NotSupportedException($"Unsupported glTF accessor: {accessor.ComponentType}/{accessor.Type}.");
    }

    private static float ReadFloat(ReadOnlySpan<byte> span) =>
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(span));
}

public sealed record ModelNode(string Name, Matrix4x4 Transform, IReadOnlyList<ModelPrimitive> Primitives,
    IReadOnlyList<ModelNode> Children);
public sealed record ModelPrimitive(IReadOnlyList<ModelVertex> Vertices, IReadOnlyList<uint> Indices);
public readonly record struct ModelVertex(Vector3 Position, Vector3 Normal, Vector2 TexCoord);
