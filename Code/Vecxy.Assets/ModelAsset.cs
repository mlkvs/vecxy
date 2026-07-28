using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;
using glTFLoader;
using glTFLoader.Schema;
using StbImageSharp;

namespace Vecxy.Assets;

public readonly record struct ModelVertex(
    Vector3 Position,
    Vector3 Normal,
    Vector2 TexCoord);

public sealed class ModelPrimitive
{
    public IReadOnlyList<ModelVertex> Vertices { get; }
    public IReadOnlyList<uint> Indices { get; }
    public int? MaterialIndex { get; }

    internal ModelPrimitive(
        ModelVertex[] vertices,
        uint[] indices,
        int? materialIndex)
    {
        Vertices = Array.AsReadOnly(vertices);
        Indices = Array.AsReadOnly(indices);
        MaterialIndex = materialIndex;
    }
}

public sealed class ModelMesh
{
    public string Name { get; }
    public IReadOnlyList<ModelPrimitive> Primitives { get; }

    internal ModelMesh(
        string name,
        ModelPrimitive[] primitives)
    {
        Name = name;
        Primitives = Array.AsReadOnly(primitives);
    }
}

public sealed class ModelNode
{
    public string Name { get; }
    public Matrix4x4 LocalTransform { get; }
    public int? MeshIndex { get; }
    public int? LightIndex { get; }
    public IReadOnlyList<int> Children { get; }

    internal ModelNode(
        string name,
        Matrix4x4 localTransform,
        int? meshIndex,
        int? lightIndex,
        int[] children)
    {
        Name = name;
        LocalTransform = localTransform;
        MeshIndex = meshIndex;
        LightIndex = lightIndex;
        Children = Array.AsReadOnly(children);
    }
}

public enum EModelLightKind : byte
{
    Point,
    Spot,
    Directional
}

public sealed class ModelLight
{
    public string Name { get; }
    public EModelLightKind Kind { get; }
    public Vector3 Color { get; }
    public float Intensity { get; }
    public float Range { get; }
    public float InnerConeAngle { get; }
    public float OuterConeAngle { get; }

    internal ModelLight(
        string name,
        EModelLightKind kind,
        Vector3 color,
        float intensity,
        float range,
        float innerConeAngle,
        float outerConeAngle)
    {
        Name = name;
        Kind = kind;
        Color = color;
        Intensity = intensity;
        Range = range;
        InnerConeAngle = innerConeAngle;
        OuterConeAngle = outerConeAngle;
    }
}

public sealed class ModelAsset
{
    public IReadOnlyList<ModelNode> Nodes { get; }
    public IReadOnlyList<ModelMesh> Meshes { get; }
    public IReadOnlyList<ModelMaterial> Materials { get; }
    public IReadOnlyList<ModelLight> Lights { get; }
    public IReadOnlyList<int> RootNodes { get; }

    internal ModelAsset(
        ModelNode[] nodes,
        ModelMesh[] meshes,
        ModelMaterial[] materials,
        ModelLight[] lights,
        int[] rootNodes)
    {
        Nodes = Array.AsReadOnly(nodes);
        Meshes = Array.AsReadOnly(meshes);
        Materials = Array.AsReadOnly(materials);
        Lights = Array.AsReadOnly(lights);
        RootNodes = Array.AsReadOnly(rootNodes);
    }
}

public sealed class ModelMaterial
{
    public string Name { get; }
    public Vector4 BaseColor { get; }
    public TextureAsset? BaseColorTexture { get; }

    internal ModelMaterial(
        string name,
        Vector4 baseColor,
        TextureAsset? baseColorTexture)
    {
        Name = name;
        BaseColor = baseColor;
        BaseColorTexture = baseColorTexture;
    }
}

public sealed class ModelAssetImporter : IAssetImporter<ModelAsset>
{
    private const string PositionAttribute = "POSITION";
    private const string NormalAttribute = "NORMAL";
    private const string TexCoordAttribute = "TEXCOORD_0";

    public IReadOnlyCollection<string> Extensions { get; } =
        [".glb"];

    public ModelAsset Import(
        AssetMetadata metadata,
        AssetImportContext context)
    {
        var source = context.ReadAllBytes(metadata.Path);

        Gltf gltf;
        byte[] binaryBuffer;
        using (var stream = new MemoryStream(source, writable: false))
            gltf = Interface.LoadModel(stream);

        using (var stream = new MemoryStream(source, writable: false))
            binaryBuffer = Interface.LoadBinaryBuffer(stream);

        ValidateDocument(gltf, metadata.Path);
        var lightImport = ParseLights(source, metadata.Path);
        var images = ImportImages(gltf, binaryBuffer, metadata.Path);
        var materials = ImportMaterials(gltf, images);

        var meshes = (gltf.Meshes ?? [])
            .Select((mesh, index) =>
                ImportMesh(gltf, binaryBuffer, mesh, index, metadata.Path))
            .ToArray();

        var nodes = (gltf.Nodes ?? [])
            .Select((node, index) =>
                ImportNode(
                    node,
                    index,
                    meshes.Length,
                    lightImport.Lights.Length,
                    gltf.Nodes?.Length ?? 0,
                    metadata.Path,
                    lightImport.NodeLights))
            .ToArray();

        var roots = GetRootNodes(gltf, nodes, metadata.Path);
        ValidateHierarchy(nodes, roots, metadata.Path);

        return new ModelAsset(
            nodes,
            meshes,
            materials,
            lightImport.Lights,
            roots);
    }

    private static void ValidateDocument(
        Gltf gltf,
        string path)
    {
        if (gltf.Buffers is null || gltf.Buffers.Length != 1)
        {
            throw new NotSupportedException(
                $"Model '{path}' must contain exactly one embedded GLB buffer.");
        }

        if (!string.IsNullOrWhiteSpace(gltf.Buffers[0].Uri))
        {
            throw new NotSupportedException(
                $"Model '{path}' references an external buffer. Only self-contained GLB files are supported.");
        }

        if (gltf.ExtensionsRequired is { Length: > 0 })
        {
            var unsupported = gltf.ExtensionsRequired
                .Where(extension => !string.Equals(
                    extension,
                    "KHR_lights_punctual",
                    StringComparison.Ordinal))
                .ToArray();

            if (unsupported.Length == 0)
                return;

            throw new NotSupportedException(
                $"Model '{path}' requires unsupported glTF extensions: " +
                string.Join(", ", unsupported));
        }
    }

    private static ModelMesh ImportMesh(
        Gltf gltf,
        byte[] buffer,
        glTFLoader.Schema.Mesh mesh,
        int meshIndex,
        string path)
    {
        if (mesh.Primitives is null || mesh.Primitives.Length == 0)
        {
            throw new InvalidDataException(
                $"Mesh {meshIndex} in '{path}' has no primitives.");
        }

        var primitives = mesh.Primitives
            .Select((primitive, primitiveIndex) =>
                ImportPrimitive(
                    gltf,
                    buffer,
                    primitive,
                    meshIndex,
                    primitiveIndex,
                    path))
            .ToArray();

        return new ModelMesh(
            string.IsNullOrWhiteSpace(mesh.Name)
                ? $"Mesh {meshIndex}"
                : mesh.Name,
            primitives);
    }

    private static ModelPrimitive ImportPrimitive(
        Gltf gltf,
        byte[] buffer,
        MeshPrimitive primitive,
        int meshIndex,
        int primitiveIndex,
        string path)
    {
        if (primitive.Mode != MeshPrimitive.ModeEnum.TRIANGLES)
        {
            throw new NotSupportedException(
                $"Mesh {meshIndex}, primitive {primitiveIndex} in '{path}' uses {primitive.Mode}; only triangles are supported.");
        }

        if (primitive.Targets is { Length: > 0 })
        {
            throw new NotSupportedException(
                $"Mesh {meshIndex}, primitive {primitiveIndex} in '{path}' uses morph targets.");
        }

        if (!primitive.Attributes.TryGetValue(
                PositionAttribute,
                out var positionAccessor))
        {
            throw new InvalidDataException(
                $"Mesh {meshIndex}, primitive {primitiveIndex} in '{path}' has no POSITION attribute.");
        }

        var positions = ReadVectors3(
            gltf,
            buffer,
            positionAccessor,
            PositionAttribute,
            path);

        var normals = primitive.Attributes.TryGetValue(
            NormalAttribute,
            out var normalAccessor)
            ? ReadVectors3(
                gltf,
                buffer,
                normalAccessor,
                NormalAttribute,
                path)
            : null;

        var texCoords = primitive.Attributes.TryGetValue(
            TexCoordAttribute,
            out var texCoordAccessor)
            ? ReadVectors2(
                gltf,
                buffer,
                texCoordAccessor,
                TexCoordAttribute,
                path)
            : null;

        if (normals is not null && normals.Length != positions.Length)
        {
            throw new InvalidDataException(
                $"NORMAL count does not match POSITION count in mesh {meshIndex}, primitive {primitiveIndex} of '{path}'.");
        }

        if (texCoords is not null && texCoords.Length != positions.Length)
        {
            throw new InvalidDataException(
                $"TEXCOORD_0 count does not match POSITION count in mesh {meshIndex}, primitive {primitiveIndex} of '{path}'.");
        }

        var indices = primitive.Indices is { } indexAccessor
            ? ReadIndices(gltf, buffer, indexAccessor, path)
            : Enumerable.Range(0, positions.Length)
                .Select(index => checked((uint)index))
                .ToArray();

        if (indices.Length % 3 != 0)
        {
            throw new InvalidDataException(
                $"Mesh {meshIndex}, primitive {primitiveIndex} in '{path}' has a non-triangular index count.");
        }

        if (indices.Any(index => index >= positions.Length))
        {
            throw new InvalidDataException(
                $"Mesh {meshIndex}, primitive {primitiveIndex} in '{path}' contains an out-of-range vertex index.");
        }

        normals ??= GenerateNormals(positions, indices);

        var vertices = new ModelVertex[positions.Length];
        for (var index = 0; index < vertices.Length; index++)
        {
            vertices[index] = new ModelVertex(
                positions[index],
                normals[index],
                texCoords?[index] ?? Vector2.Zero);
        }

        return new ModelPrimitive(
            vertices,
            indices,
            primitive.Material);
    }

    private static TextureAsset?[] ImportImages(
        Gltf gltf,
        byte[] buffer,
        string path)
    {
        if (gltf.Images is not { Length: > 0 })
            return [];

        var result = new TextureAsset?[gltf.Images.Length];

        for (var index = 0; index < gltf.Images.Length; index++)
        {
            var image = gltf.Images[index];
            if (image.BufferView is not { } bufferViewIndex)
                continue;

            if (gltf.BufferViews is null ||
                bufferViewIndex < 0 ||
                bufferViewIndex >= gltf.BufferViews.Length)
            {
                throw new InvalidDataException(
                    $"Model '{path}' references invalid image buffer view {bufferViewIndex}.");
            }

            var view = gltf.BufferViews[bufferViewIndex];
            var start = view.ByteOffset;
            var length = view.ByteLength;

            if (start < 0 ||
                length <= 0 ||
                start + length > buffer.Length)
            {
                throw new InvalidDataException(
                    $"Model '{path}' image buffer view {bufferViewIndex} is outside the binary buffer.");
            }

            using var stream = new MemoryStream(
                buffer,
                start,
                length,
                writable: false);
            var decoded = ImageResult.FromStream(
                stream,
                ColorComponents.RedGreenBlueAlpha);

            result[index] = new TextureAsset(
                decoded.Width,
                decoded.Height,
                decoded.Data);
        }

        return result;
    }

    private static ModelMaterial[] ImportMaterials(
        Gltf gltf,
        IReadOnlyList<TextureAsset?> images)
    {
        if (gltf.Materials is not { Length: > 0 })
            return [];

        var result = new ModelMaterial[gltf.Materials.Length];

        for (var materialIndex = 0; materialIndex < gltf.Materials.Length; materialIndex++)
        {
            var material = gltf.Materials[materialIndex];
            var pbr = material.PbrMetallicRoughness;
            var baseColor = Vector4.One;

            if (pbr?.BaseColorFactor is { Length: 4 } factor)
            {
                baseColor = new Vector4(
                    factor[0],
                    factor[1],
                    factor[2],
                    factor[3]);
            }

            TextureAsset? baseColorTexture = null;
            var textureIndex = pbr?.BaseColorTexture?.Index;
            if (textureIndex is not null &&
                gltf.Textures is { } textures &&
                textureIndex.Value >= 0 &&
                textureIndex.Value < textures.Length)
            {
                var sourceIndex = textures[textureIndex.Value].Source;
                if (sourceIndex is >= 0 &&
                    sourceIndex < images.Count)
                {
                    baseColorTexture = images[sourceIndex.Value];
                }
            }

            result[materialIndex] = new ModelMaterial(
                string.IsNullOrWhiteSpace(material.Name)
                    ? $"Material {materialIndex}"
                    : material.Name,
                baseColor,
                baseColorTexture);
        }

        return result;
    }

    private static Vector3[] ReadVectors3(
        Gltf gltf,
        byte[] buffer,
        int accessorIndex,
        string attribute,
        string path)
    {
        var accessor = GetAccessor(
            gltf,
            accessorIndex,
            Accessor.TypeEnum.VEC3,
            Accessor.ComponentTypeEnum.FLOAT,
            attribute,
            path);
        var result = new Vector3[accessor.Count];

        ForEachElement(
            gltf,
            accessor,
            sizeof(float) * 3,
            buffer.Length,
            path,
            offset =>
            {
                var destination = result[offset.Index];
                destination.X = ReadSingle(buffer, offset.ByteOffset);
                destination.Y = ReadSingle(buffer, offset.ByteOffset + sizeof(float));
                destination.Z = ReadSingle(buffer, offset.ByteOffset + sizeof(float) * 2);
                result[offset.Index] = destination;
            });

        return result;
    }

    private static Vector2[] ReadVectors2(
        Gltf gltf,
        byte[] buffer,
        int accessorIndex,
        string attribute,
        string path)
    {
        if (gltf.Accessors is null ||
            accessorIndex < 0 ||
            accessorIndex >= gltf.Accessors.Length)
        {
            throw new InvalidDataException(
                $"Model '{path}' references invalid {attribute} accessor {accessorIndex}.");
        }

        var accessor = gltf.Accessors[accessorIndex];
        if (accessor.Type != Accessor.TypeEnum.VEC2)
        {
            throw new NotSupportedException(
                $"{attribute} accessor {accessorIndex} in '{path}' must use VEC2.");
        }

        var componentSize = accessor.ComponentType switch
        {
            Accessor.ComponentTypeEnum.FLOAT => sizeof(float),
            Accessor.ComponentTypeEnum.UNSIGNED_BYTE
                when accessor.Normalized => sizeof(byte),
            Accessor.ComponentTypeEnum.UNSIGNED_SHORT
                when accessor.Normalized => sizeof(ushort),
            _ => throw new NotSupportedException(
                $"{attribute} accessor {accessorIndex} in '{path}' uses unsupported component type {accessor.ComponentType}.")
        };

        var result = new Vector2[accessor.Count];

        ForEachElement(
            gltf,
            accessor,
            componentSize * 2,
            buffer.Length,
            path,
            offset =>
            {
                result[offset.Index] = new Vector2(
                    ReadTexCoordComponent(
                        buffer,
                        offset.ByteOffset,
                        accessor.ComponentType),
                    ReadTexCoordComponent(
                        buffer,
                        offset.ByteOffset + componentSize,
                        accessor.ComponentType));
            });

        return result;
    }

    private static uint[] ReadIndices(
        Gltf gltf,
        byte[] buffer,
        int accessorIndex,
        string path)
    {
        if (gltf.Accessors is null ||
            accessorIndex < 0 ||
            accessorIndex >= gltf.Accessors.Length)
        {
            throw new InvalidDataException(
                $"Model '{path}' references invalid index accessor {accessorIndex}.");
        }

        var accessor = gltf.Accessors[accessorIndex];
        if (accessor.Type != Accessor.TypeEnum.SCALAR)
        {
            throw new InvalidDataException(
                $"Index accessor {accessorIndex} in '{path}' is not SCALAR.");
        }

        var componentSize = accessor.ComponentType switch
        {
            Accessor.ComponentTypeEnum.UNSIGNED_BYTE => sizeof(byte),
            Accessor.ComponentTypeEnum.UNSIGNED_SHORT => sizeof(ushort),
            Accessor.ComponentTypeEnum.UNSIGNED_INT => sizeof(uint),
            _ => throw new NotSupportedException(
                $"Index accessor {accessorIndex} in '{path}' uses unsupported component type {accessor.ComponentType}.")
        };

        var result = new uint[accessor.Count];

        ForEachElement(
            gltf,
            accessor,
            componentSize,
            buffer.Length,
            path,
            offset =>
            {
                result[offset.Index] = accessor.ComponentType switch
                {
                    Accessor.ComponentTypeEnum.UNSIGNED_BYTE =>
                        buffer[offset.ByteOffset],
                    Accessor.ComponentTypeEnum.UNSIGNED_SHORT =>
                        BinaryPrimitives.ReadUInt16LittleEndian(
                            buffer.AsSpan(offset.ByteOffset, sizeof(ushort))),
                    Accessor.ComponentTypeEnum.UNSIGNED_INT =>
                        BinaryPrimitives.ReadUInt32LittleEndian(
                            buffer.AsSpan(offset.ByteOffset, sizeof(uint))),
                    _ => throw new InvalidOperationException(
                        "Unexpected index component type.")
                };
            });

        return result;
    }

    private static Accessor GetAccessor(
        Gltf gltf,
        int accessorIndex,
        Accessor.TypeEnum expectedType,
        Accessor.ComponentTypeEnum expectedComponentType,
        string attribute,
        string path)
    {
        if (gltf.Accessors is null ||
            accessorIndex < 0 ||
            accessorIndex >= gltf.Accessors.Length)
        {
            throw new InvalidDataException(
                $"Model '{path}' references invalid {attribute} accessor {accessorIndex}.");
        }

        var accessor = gltf.Accessors[accessorIndex];
        if (accessor.Type != expectedType ||
            accessor.ComponentType != expectedComponentType)
        {
            throw new NotSupportedException(
                $"{attribute} accessor {accessorIndex} in '{path}' must use {expectedType}/{expectedComponentType}.");
        }

        return accessor;
    }

    private static void ForEachElement(
        Gltf gltf,
        Accessor accessor,
        int elementSize,
        int bufferLength,
        string path,
        Action<ElementOffset> read)
    {
        if (accessor.Sparse is not null)
            throw new NotSupportedException(
                $"Model '{path}' contains a sparse accessor.");

        if (accessor.BufferView is not { } viewIndex ||
            gltf.BufferViews is null ||
            viewIndex < 0 ||
            viewIndex >= gltf.BufferViews.Length)
        {
            throw new InvalidDataException(
                $"Model '{path}' contains an accessor without a valid buffer view.");
        }

        var view = gltf.BufferViews[viewIndex];
        if (view.Buffer != 0)
            throw new NotSupportedException(
                $"Model '{path}' references a non-embedded buffer.");

        var stride = view.ByteStride ?? elementSize;
        if (stride < elementSize)
            throw new InvalidDataException(
                $"Model '{path}' contains an accessor with an invalid byte stride.");

        var start = checked(view.ByteOffset + accessor.ByteOffset);
        var viewEnd = checked(view.ByteOffset + view.ByteLength);
        var end = accessor.Count == 0
            ? start
            : checked(start + (accessor.Count - 1) * stride + elementSize);

        if (start < view.ByteOffset ||
            end > viewEnd ||
            end > bufferLength)
        {
            throw new InvalidDataException(
                $"Model '{path}' contains an accessor outside its buffer view.");
        }

        for (var index = 0; index < accessor.Count; index++)
            read(new ElementOffset(index, checked(start + index * stride)));
    }

    private static ModelNode ImportNode(
        Node node,
        int nodeIndex,
        int meshCount,
        int lightCount,
        int nodeCount,
        string path,
        IReadOnlyDictionary<int, int> nodeLights)
    {
        if (node.Mesh is { } meshIndex &&
            (meshIndex < 0 || meshIndex >= meshCount))
        {
            throw new InvalidDataException(
                $"Node {nodeIndex} in '{path}' references invalid mesh {meshIndex}.");
        }

        int? lightIndex = null;
        if (nodeLights.TryGetValue(nodeIndex, out var currentLightIndex))
        {
            if (currentLightIndex < 0 || currentLightIndex >= lightCount)
            {
                throw new InvalidDataException(
                    $"Node {nodeIndex} in '{path}' references invalid light {currentLightIndex}.");
            }

            lightIndex = currentLightIndex;
        }

        var children = node.Children ?? [];
        if (children.Any(child => child < 0 || child >= nodeCount))
        {
            throw new InvalidDataException(
                $"Node {nodeIndex} in '{path}' references an invalid child.");
        }

        if (children.Distinct().Count() != children.Length)
        {
            throw new InvalidDataException(
                $"Node {nodeIndex} in '{path}' references the same child more than once.");
        }

        var localTransform = node.ShouldSerializeMatrix()
            ? ReadMatrix(node.Matrix, nodeIndex, path)
            : ReadTrs(node, nodeIndex, path);

        return new ModelNode(
            string.IsNullOrWhiteSpace(node.Name)
                ? $"Node {nodeIndex}"
                : node.Name,
            localTransform,
            node.Mesh,
            lightIndex,
            children.ToArray());
    }

    private static LightImportData ParseLights(
        byte[] source,
        string path)
    {
        using var document = ReadGlbJsonDocument(source, path);
        var root = document.RootElement;

        if (!root.TryGetProperty("extensions", out var extensions) ||
            !extensions.TryGetProperty("KHR_lights_punctual", out var punctual) ||
            !punctual.TryGetProperty("lights", out var lightsElement))
        {
            return LightImportData.Empty;
        }

        var lights = new List<ModelLight>();

        foreach (var lightElement in lightsElement.EnumerateArray())
        {
            var type = lightElement.TryGetProperty("type", out var typeElement)
                ? typeElement.GetString()
                : null;

            if (!string.Equals(type, "point", StringComparison.Ordinal) &&
                !string.Equals(type, "spot", StringComparison.Ordinal) &&
                !string.Equals(type, "directional", StringComparison.Ordinal))
            {
                continue;
            }

            var name =
                lightElement.TryGetProperty("name", out var nameElement)
                    ? nameElement.GetString()
                    : null;
            var color =
                lightElement.TryGetProperty("color", out var colorElement)
                    ? ReadVector3(colorElement, Vector3.One)
                    : Vector3.One;
            var intensity =
                lightElement.TryGetProperty("intensity", out var intensityElement)
                    ? intensityElement.GetSingle()
                    : 1.0f;
            var range =
                lightElement.TryGetProperty("range", out var rangeElement)
                    ? rangeElement.GetSingle()
                    : 0.0f;

            var innerConeAngle = 0.0f;
            var outerConeAngle = MathF.PI / 4.0f;

            if (string.Equals(type, "spot", StringComparison.Ordinal) &&
                lightElement.TryGetProperty("spot", out var spotElement))
            {
                if (spotElement.TryGetProperty("innerConeAngle", out var innerElement))
                    innerConeAngle = innerElement.GetSingle();

                if (spotElement.TryGetProperty("outerConeAngle", out var outerElement))
                    outerConeAngle = outerElement.GetSingle();
            }

            lights.Add(
                new ModelLight(
                    string.IsNullOrWhiteSpace(name)
                        ? $"Light {lights.Count}"
                        : name!,
                    string.Equals(type, "spot", StringComparison.Ordinal)
                        ? EModelLightKind.Spot
                        : string.Equals(type, "directional", StringComparison.Ordinal)
                            ? EModelLightKind.Directional
                            : EModelLightKind.Point,
                    color,
                    intensity,
                    range,
                    innerConeAngle,
                    outerConeAngle));
        }

        if (!root.TryGetProperty("nodes", out var nodesElement))
            return new LightImportData(lights.ToArray(), new Dictionary<int, int>());

        var nodeLights = new Dictionary<int, int>();
        var nodeIndex = 0;

        foreach (var nodeElement in nodesElement.EnumerateArray())
        {
            if (nodeElement.TryGetProperty("extensions", out var nodeExtensions) &&
                nodeExtensions.TryGetProperty("KHR_lights_punctual", out var nodePunctual) &&
                nodePunctual.TryGetProperty("light", out var nodeLightElement))
            {
                nodeLights[nodeIndex] = nodeLightElement.GetInt32();
            }

            nodeIndex++;
        }

        return new LightImportData(
            lights.ToArray(),
            nodeLights);
    }

    private static JsonDocument ReadGlbJsonDocument(
        byte[] source,
        string path)
    {
        if (source.Length < 20)
            throw new InvalidDataException(
                $"Model '{path}' is not a valid GLB file.");

        var offset = 12;
        while (offset + 8 <= source.Length)
        {
            var chunkLength = BinaryPrimitives.ReadInt32LittleEndian(source.AsSpan(offset, 4));
            var chunkType = BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(offset + 4, 4));
            offset += 8;

            if (chunkLength < 0 || offset + chunkLength > source.Length)
            {
                throw new InvalidDataException(
                    $"Model '{path}' contains an invalid GLB chunk.");
            }

            if (chunkType == 0x4E4F534A)
            {
                return JsonDocument.Parse(source.AsMemory(offset, chunkLength));
            }

            offset += chunkLength;
        }

        throw new InvalidDataException(
            $"Model '{path}' does not contain a JSON chunk.");
    }

    private static Vector3 ReadVector3(
        JsonElement element,
        Vector3 fallback)
    {
        if (element.ValueKind != JsonValueKind.Array)
            return fallback;

        var values = element.EnumerateArray().ToArray();
        if (values.Length != 3)
            return fallback;

        return new Vector3(
            values[0].GetSingle(),
            values[1].GetSingle(),
            values[2].GetSingle());
    }

    private static Matrix4x4 ReadMatrix(
        float[] matrix,
        int nodeIndex,
        string path)
    {
        if (matrix.Length != 16)
            throw new InvalidDataException(
                $"Node {nodeIndex} in '{path}' has an invalid transform matrix.");

        return new Matrix4x4(
            matrix[0], matrix[1], matrix[2], matrix[3],
            matrix[4], matrix[5], matrix[6], matrix[7],
            matrix[8], matrix[9], matrix[10], matrix[11],
            matrix[12], matrix[13], matrix[14], matrix[15]);
    }

    private static Matrix4x4 ReadTrs(
        Node node,
        int nodeIndex,
        string path)
    {
        if (node.Translation.Length != 3 ||
            node.Rotation.Length != 4 ||
            node.Scale.Length != 3)
        {
            throw new InvalidDataException(
                $"Node {nodeIndex} in '{path}' has invalid TRS data.");
        }

        var rotation = new Quaternion(
            node.Rotation[0],
            node.Rotation[1],
            node.Rotation[2],
            node.Rotation[3]);
        if (rotation.LengthSquared() <= float.Epsilon)
        {
            throw new InvalidDataException(
                $"Node {nodeIndex} in '{path}' has a zero rotation quaternion.");
        }

        return
            Matrix4x4.CreateScale(
                node.Scale[0],
                node.Scale[1],
                node.Scale[2]) *
            Matrix4x4.CreateFromQuaternion(
                Quaternion.Normalize(rotation)) *
            Matrix4x4.CreateTranslation(
                node.Translation[0],
                node.Translation[1],
                node.Translation[2]);
    }

    private static int[] GetRootNodes(
        Gltf gltf,
        IReadOnlyList<ModelNode> nodes,
        string path)
    {
        if (gltf.Scenes is { Length: > 0 })
        {
            var sceneIndex = gltf.Scene ?? 0;
            if (sceneIndex < 0 || sceneIndex >= gltf.Scenes.Length)
                throw new InvalidDataException(
                    $"Model '{path}' selects invalid scene {sceneIndex}.");

            var roots = gltf.Scenes[sceneIndex].Nodes ?? [];
            if (roots.Any(root => root < 0 || root >= nodes.Count))
                throw new InvalidDataException(
                    $"Default scene in '{path}' references an invalid root node.");

            return roots.ToArray();
        }

        var children = nodes
            .SelectMany(node => node.Children)
            .ToHashSet();
        return Enumerable.Range(0, nodes.Count)
            .Where(index => !children.Contains(index))
            .ToArray();
    }

    private static void ValidateHierarchy(
        IReadOnlyList<ModelNode> nodes,
        IReadOnlyList<int> roots,
        string path)
    {
        if (roots.Distinct().Count() != roots.Count)
        {
            throw new InvalidDataException(
                $"Default scene in '{path}' references the same root more than once.");
        }

        const int unassigned = int.MinValue;
        var parents = Enumerable
            .Repeat(unassigned, nodes.Count)
            .ToArray();
        var visiting = new HashSet<int>();
        var visited = new HashSet<int>();

        foreach (var root in roots)
            Visit(root, parent: null);

        return;

        void Visit(int nodeIndex, int? parent)
        {
            if (!visiting.Add(nodeIndex))
                throw new InvalidDataException(
                    $"Model '{path}' contains a node hierarchy cycle.");

            var parentIndex = parent ?? -1;
            if (parents[nodeIndex] != unassigned &&
                parents[nodeIndex] != parentIndex)
            {
                throw new InvalidDataException(
                    $"Node {nodeIndex} in '{path}' has multiple parents.");
            }

            parents[nodeIndex] = parentIndex;

            if (visited.Add(nodeIndex))
            {
                foreach (var child in nodes[nodeIndex].Children)
                    Visit(child, nodeIndex);
            }

            visiting.Remove(nodeIndex);
        }
    }

    private static Vector3[] GenerateNormals(
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<uint> indices)
    {
        var normals = new Vector3[positions.Count];

        for (var index = 0; index < indices.Count; index += 3)
        {
            var first = checked((int)indices[index]);
            var second = checked((int)indices[index + 1]);
            var third = checked((int)indices[index + 2]);

            var edgeA = positions[second] - positions[first];
            var edgeB = positions[third] - positions[first];
            var normal = Vector3.Cross(edgeA, edgeB);

            normals[first] += normal;
            normals[second] += normal;
            normals[third] += normal;
        }

        for (var index = 0; index < normals.Length; index++)
        {
            normals[index] = normals[index].LengthSquared() > float.Epsilon
                ? Vector3.Normalize(normals[index])
                : Vector3.UnitY;
        }

        return normals;
    }

    private static float ReadSingle(
        ReadOnlySpan<byte> data,
        int offset)
    {
        var bits = BinaryPrimitives.ReadInt32LittleEndian(
            data.Slice(offset, sizeof(float)));
        return BitConverter.Int32BitsToSingle(bits);
    }

    private static float ReadTexCoordComponent(
        byte[] data,
        int offset,
        Accessor.ComponentTypeEnum componentType)
    {
        return componentType switch
        {
            Accessor.ComponentTypeEnum.FLOAT =>
                ReadSingle(data, offset),
            Accessor.ComponentTypeEnum.UNSIGNED_BYTE =>
                data[offset] / (float)byte.MaxValue,
            Accessor.ComponentTypeEnum.UNSIGNED_SHORT =>
                BinaryPrimitives.ReadUInt16LittleEndian(
                    data.AsSpan(offset, sizeof(ushort))) /
                (float)ushort.MaxValue,
            _ => throw new InvalidOperationException(
                "Unexpected texture coordinate component type.")
        };
    }

    private readonly record struct ElementOffset(
        int Index,
        int ByteOffset);

    private readonly record struct LightImportData(
        ModelLight[] Lights,
        IReadOnlyDictionary<int, int> NodeLights)
    {
        public static LightImportData Empty { get; } =
            new([], new Dictionary<int, int>());
    }
}
