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
    Vector2 TexCoord,
    Vector4 Joints,
    Vector4 Weights)
{
    public ModelVertex(
        Vector3 position,
        Vector3 normal,
        Vector2 texCoord)
        : this(position, normal, texCoord, Vector4.Zero, Vector4.Zero)
    {
    }
}

public sealed class ModelPrimitive
{
    public IReadOnlyList<ModelVertex> Vertices { get; }
    public IReadOnlyList<uint> Indices { get; }
    public int? MaterialIndex { get; }
    public bool IsSkinned { get; }

    internal ModelPrimitive(
        ModelVertex[] vertices,
        uint[] indices,
        int? materialIndex,
        bool isSkinned)
    {
        Vertices = Array.AsReadOnly(vertices);
        Indices = Array.AsReadOnly(indices);
        MaterialIndex = materialIndex;
        IsSkinned = isSkinned;
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
    public int? SkinIndex { get; }
    public IReadOnlyList<int> Children { get; }

    internal ModelNode(
        string name,
        Matrix4x4 localTransform,
        int? meshIndex,
        int? lightIndex,
        int? skinIndex,
        int[] children)
    {
        Name = name;
        LocalTransform = localTransform;
        MeshIndex = meshIndex;
        LightIndex = lightIndex;
        SkinIndex = skinIndex;
        Children = Array.AsReadOnly(children);
    }
}

public sealed class ModelSkin
{
    public string Name { get; }
    public IReadOnlyList<int> Joints { get; }
    public IReadOnlyList<Matrix4x4> InverseBindMatrices { get; }
    public int? SkeletonRootNode { get; }

    internal ModelSkin(
        string name,
        int[] joints,
        Matrix4x4[] inverseBindMatrices,
        int? skeletonRootNode)
    {
        Name = name;
        Joints = Array.AsReadOnly(joints);
        InverseBindMatrices = Array.AsReadOnly(inverseBindMatrices);
        SkeletonRootNode = skeletonRootNode;
    }
}

public enum EModelAnimationPath : byte
{
    Translation,
    Rotation,
    Scale
}

public enum EModelAnimationInterpolation : byte
{
    Step,
    Linear,
    CubicSpline
}

public sealed class ModelAnimationChannel
{
    public int NodeIndex { get; }
    public EModelAnimationPath Path { get; }
    public EModelAnimationInterpolation Interpolation { get; }
    public IReadOnlyList<float> Times { get; }
    public IReadOnlyList<Vector4> Values { get; }
    public IReadOnlyList<Vector4>? InTangents { get; }
    public IReadOnlyList<Vector4>? OutTangents { get; }

    internal ModelAnimationChannel(
        int nodeIndex,
        EModelAnimationPath path,
        EModelAnimationInterpolation interpolation,
        float[] times,
        Vector4[] values,
        Vector4[]? inTangents = null,
        Vector4[]? outTangents = null)
    {
        NodeIndex = nodeIndex;
        Path = path;
        Interpolation = interpolation;
        Times = Array.AsReadOnly(times);
        Values = Array.AsReadOnly(values);
        InTangents = inTangents is null ? null : Array.AsReadOnly(inTangents);
        OutTangents = outTangents is null ? null : Array.AsReadOnly(outTangents);
    }
}

public sealed class ModelAnimation
{
    public string Name { get; }
    public float Duration { get; }
    public IReadOnlyList<ModelAnimationChannel> Channels { get; }

    internal ModelAnimation(
        string name,
        float duration,
        ModelAnimationChannel[] channels)
    {
        Name = name;
        Duration = duration;
        Channels = Array.AsReadOnly(channels);
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
    public IReadOnlyList<ModelSkin> Skins { get; }
    public IReadOnlyList<ModelAnimation> Animations { get; }
    public IReadOnlyList<int> RootNodes { get; }

    internal ModelAsset(
        ModelNode[] nodes,
        ModelMesh[] meshes,
        ModelMaterial[] materials,
        ModelLight[] lights,
        ModelSkin[] skins,
        ModelAnimation[] animations,
        int[] rootNodes)
    {
        Nodes = Array.AsReadOnly(nodes);
        Meshes = Array.AsReadOnly(meshes);
        Materials = Array.AsReadOnly(materials);
        Lights = Array.AsReadOnly(lights);
        Skins = Array.AsReadOnly(skins);
        Animations = Array.AsReadOnly(animations);
        RootNodes = Array.AsReadOnly(rootNodes);
    }
}

public sealed class ModelMaterial
{
    public string Name { get; }
    public Vector4 BaseColor { get; }
    public TextureAsset? BaseColorTexture { get; }
    public int BaseColorTexCoord { get; }
    public TextureAsset? NormalTexture { get; }
    public int NormalTexCoord { get; }
    public TextureAsset? MetallicRoughnessTexture { get; }
    public int MetallicRoughnessTexCoord { get; }
    public float MetallicFactor { get; }
    public float RoughnessFactor { get; }
    public Vector3 EmissiveColor { get; }

    internal ModelMaterial(
        string name,
        Vector4 baseColor,
        TextureAsset? baseColorTexture,
        int baseColorTexCoord,
        TextureAsset? normalTexture,
        int normalTexCoord,
        TextureAsset? metallicRoughnessTexture,
        int metallicRoughnessTexCoord,
        float metallicFactor,
        float roughnessFactor,
        Vector3 emissiveColor)
    {
        Name = name;
        BaseColor = baseColor;
        BaseColorTexture = baseColorTexture;
        BaseColorTexCoord = baseColorTexCoord;
        NormalTexture = normalTexture;
        NormalTexCoord = normalTexCoord;
        MetallicRoughnessTexture = metallicRoughnessTexture;
        MetallicRoughnessTexCoord = metallicRoughnessTexCoord;
        MetallicFactor = metallicFactor;
        RoughnessFactor = roughnessFactor;
        EmissiveColor = emissiveColor;
    }
}

public sealed class ModelAssetImporter : IAssetImporter<ModelAsset>
{
    private const string PositionAttribute = "POSITION";
    private const string NormalAttribute = "NORMAL";
    private const string TexCoordAttribute = "TEXCOORD_0";
    private const string JointsAttribute = "JOINTS_0";
    private const string WeightsAttribute = "WEIGHTS_0";

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
        var emissiveStrengths = ParseEmissiveStrengths(
            source,
            gltf.Materials?.Length ?? 0,
            metadata.Path);
        var materials = ImportMaterials(gltf, images, emissiveStrengths);
        var skins = ImportSkins(gltf, binaryBuffer, source, metadata.Path);
        var animations = ImportAnimations(gltf, binaryBuffer, source, metadata.Path);
        var nodeSkins = ParseNodeSkins(source, gltf.Nodes?.Length ?? 0, skins.Length, metadata.Path);

        var meshes = (gltf.Meshes ?? [])
            .Select((mesh, index) =>
                ImportMesh(
                    gltf,
                    binaryBuffer,
                    mesh,
                    index,
                    metadata.Path,
                    materials))
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
                    lightImport.NodeLights,
                    nodeSkins))
            .ToArray();

        var roots = GetRootNodes(gltf, nodes, metadata.Path);
        ValidateHierarchy(nodes, roots, metadata.Path);

        return new ModelAsset(
            nodes,
            meshes,
            materials,
            lightImport.Lights,
            skins,
            animations,
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
        string path,
        IReadOnlyList<ModelMaterial> materials)
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
                    path,
                    materials))
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
        string path,
        IReadOnlyList<ModelMaterial> materials)
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

        var texCoordSet = primitive.Material is { } materialIndex &&
            materialIndex >= 0 &&
            materialIndex < materials.Count
            ? materials[materialIndex].BaseColorTexCoord
            : 0;
        var texCoordAttribute = $"TEXCOORD_{texCoordSet}";

        var texCoords = primitive.Attributes.TryGetValue(
            texCoordAttribute,
            out var texCoordAccessor)
            ? ReadVectors2(
                gltf,
                buffer,
                texCoordAccessor,
                texCoordAttribute,
                path)
            : null;

        var hasJoints = primitive.Attributes.TryGetValue(
            JointsAttribute,
            out var jointsAccessor);
        var hasWeights = primitive.Attributes.TryGetValue(
            WeightsAttribute,
            out var weightsAccessor);
        if (hasJoints != hasWeights)
        {
            throw new InvalidDataException(
                $"Mesh {meshIndex}, primitive {primitiveIndex} in '{path}' must provide both JOINTS_0 and WEIGHTS_0.");
        }

        var joints = hasJoints
            ? ReadJointVectors(gltf, buffer, jointsAccessor, path)
            : null;
        var weights = hasWeights
            ? ReadWeightVectors(gltf, buffer, weightsAccessor, path)
            : null;

        if (normals is not null && normals.Length != positions.Length)
        {
            throw new InvalidDataException(
                $"NORMAL count does not match POSITION count in mesh {meshIndex}, primitive {primitiveIndex} of '{path}'.");
        }

        if (texCoords is not null && texCoords.Length != positions.Length)
        {
            throw new InvalidDataException(
                $"{texCoordAttribute} count does not match POSITION count in mesh {meshIndex}, primitive {primitiveIndex} of '{path}'.");
        }

        if (joints is not null && joints.Length != positions.Length ||
            weights is not null && weights.Length != positions.Length)
        {
            throw new InvalidDataException(
                $"Skin attribute count does not match POSITION count in mesh {meshIndex}, primitive {primitiveIndex} of '{path}'.");
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
                texCoords?[index] ?? Vector2.Zero,
                joints?[index] ?? Vector4.Zero,
                weights is null ? Vector4.Zero : NormalizeWeights(weights[index]));
        }

        return new ModelPrimitive(
            vertices,
            indices,
            primitive.Material,
            hasJoints);
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
        IReadOnlyList<TextureAsset?> images,
        IReadOnlyList<float> emissiveStrengths)
    {
        if (gltf.Materials is not { Length: > 0 })
            return [];

        var result = new ModelMaterial[gltf.Materials.Length];

        for (var materialIndex = 0; materialIndex < gltf.Materials.Length; materialIndex++)
        {
            var material = gltf.Materials[materialIndex];
            var pbr = material.PbrMetallicRoughness;
            var baseColor = Vector4.One;
            var emissiveColor = material.EmissiveFactor is { Length: 3 } emissive
                ? new Vector3(emissive[0], emissive[1], emissive[2])
                : Vector3.Zero;
            emissiveColor *= emissiveStrengths[materialIndex];

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
            var baseColorTexCoord = pbr?.BaseColorTexture?.TexCoord ?? 0;
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

            TextureAsset? normalTexture = null;
            var normalTexCoord = material.NormalTexture?.TexCoord ?? 0;
            if (material.NormalTexture?.Index is { } normalIndex &&
                gltf.Textures is { } normalTextures &&
                normalIndex >= 0 && normalIndex < normalTextures.Length)
            {
                var sourceIndex = normalTextures[normalIndex].Source;
                if (sourceIndex is >= 0 && sourceIndex < images.Count)
                    normalTexture = images[sourceIndex.Value];
            }

            TextureAsset? metallicRoughnessTexture = null;
            var metallicRoughnessTexCoord = pbr?.MetallicRoughnessTexture?.TexCoord ?? 0;
            if (pbr?.MetallicRoughnessTexture?.Index is { } mrIndex &&
                gltf.Textures is { } mrTextures &&
                mrIndex >= 0 && mrIndex < mrTextures.Length)
            {
                var sourceIndex = mrTextures[mrIndex].Source;
                if (sourceIndex is >= 0 && sourceIndex < images.Count)
                    metallicRoughnessTexture = images[sourceIndex.Value];
            }

            result[materialIndex] = new ModelMaterial(
                string.IsNullOrWhiteSpace(material.Name)
                    ? $"Material {materialIndex}"
                    : material.Name,
                baseColor,
                baseColorTexture,
                baseColorTexCoord,
                normalTexture,
                normalTexCoord,
                metallicRoughnessTexture,
                metallicRoughnessTexCoord,
                pbr?.MetallicFactor ?? 1.0f,
                pbr?.RoughnessFactor ?? 1.0f,
                emissiveColor);
        }

        return result;
    }

    private static ModelSkin[] ImportSkins(
        Gltf gltf,
        byte[] buffer,
        byte[] source,
        string path)
    {
        using var document = ReadGlbJsonDocument(source, path);
        if (!document.RootElement.TryGetProperty("skins", out var skinsElement))
            return [];

        var result = new List<ModelSkin>();
        var skinIndex = 0;
        foreach (var skinElement in skinsElement.EnumerateArray())
        {
            if (!skinElement.TryGetProperty("joints", out var jointsElement))
                throw new InvalidDataException($"Skin {skinIndex} in '{path}' has no joints.");

            var joints = jointsElement.EnumerateArray()
                .Select(value => value.GetInt32())
                .ToArray();
            if (joints.Length == 0 ||
                joints.Any(joint => joint < 0 || joint >= (gltf.Nodes?.Length ?? 0)) ||
                joints.Distinct().Count() != joints.Length)
            {
                throw new InvalidDataException($"Skin {skinIndex} in '{path}' contains invalid joints.");
            }

            Matrix4x4[] inverseBindMatrices;
            if (skinElement.TryGetProperty("inverseBindMatrices", out var inverseBindAccessor))
            {
                inverseBindMatrices = ReadMatrices4(
                    gltf,
                    buffer,
                    inverseBindAccessor.GetInt32(),
                    "inverse bind matrix",
                    path);
                if (inverseBindMatrices.Length != joints.Length)
                {
                    throw new InvalidDataException(
                        $"Skin {skinIndex} in '{path}' has {joints.Length} joints but {inverseBindMatrices.Length} inverse bind matrices.");
                }
            }
            else
            {
                inverseBindMatrices = Enumerable.Repeat(Matrix4x4.Identity, joints.Length).ToArray();
            }

            int? skeletonRoot = skinElement.TryGetProperty("skeleton", out var skeletonElement)
                ? skeletonElement.GetInt32()
                : null;
            if (skeletonRoot is { } root && (root < 0 || root >= (gltf.Nodes?.Length ?? 0)))
                throw new InvalidDataException($"Skin {skinIndex} in '{path}' has an invalid skeleton root.");

            var name = skinElement.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString()
                : null;
            result.Add(new ModelSkin(
                string.IsNullOrWhiteSpace(name) ? $"Skin {skinIndex}" : name,
                joints,
                inverseBindMatrices,
                skeletonRoot));
            skinIndex++;
        }

        return result.ToArray();
    }

    private static ModelAnimation[] ImportAnimations(
        Gltf gltf,
        byte[] buffer,
        byte[] source,
        string path)
    {
        using var document = ReadGlbJsonDocument(source, path);
        if (!document.RootElement.TryGetProperty("animations", out var animationsElement))
            return [];

        var animations = new List<ModelAnimation>();
        var animationIndex = 0;
        foreach (var animationElement in animationsElement.EnumerateArray())
        {
            if (!animationElement.TryGetProperty("samplers", out var samplersElement) ||
                !animationElement.TryGetProperty("channels", out var channelsElement))
            {
                throw new InvalidDataException($"Animation {animationIndex} in '{path}' is incomplete.");
            }

            var samplers = samplersElement.EnumerateArray().ToArray();
            var channels = new List<ModelAnimationChannel>();
            foreach (var channelElement in channelsElement.EnumerateArray())
            {
                var samplerIndex = channelElement.GetProperty("sampler").GetInt32();
                if (samplerIndex < 0 || samplerIndex >= samplers.Length)
                    throw new InvalidDataException($"Animation {animationIndex} in '{path}' references an invalid sampler.");

                var target = channelElement.GetProperty("target");
                if (!target.TryGetProperty("node", out var nodeElement))
                    throw new NotSupportedException($"Animation {animationIndex} in '{path}' contains a channel without a node target.");
                var nodeIndex = nodeElement.GetInt32();
                if (nodeIndex < 0 || nodeIndex >= (gltf.Nodes?.Length ?? 0))
                    throw new InvalidDataException($"Animation {animationIndex} in '{path}' targets an invalid node.");

                var targetPath = target.GetProperty("path").GetString();
                var animationPath = targetPath switch
                {
                    "translation" => EModelAnimationPath.Translation,
                    "rotation" => EModelAnimationPath.Rotation,
                    "scale" => EModelAnimationPath.Scale,
                    "weights" => throw new NotSupportedException(
                        $"Animation {animationIndex} in '{path}' uses morph target weights."),
                    _ => throw new InvalidDataException(
                        $"Animation {animationIndex} in '{path}' uses unknown target path '{targetPath}'.")
                };

                var sampler = samplers[samplerIndex];
                var interpolationName = sampler.TryGetProperty("interpolation", out var interpolationElement)
                    ? interpolationElement.GetString()
                    : "LINEAR";
                var interpolation = interpolationName switch
                {
                    "STEP" => EModelAnimationInterpolation.Step,
                    "LINEAR" => EModelAnimationInterpolation.Linear,
                    "CUBICSPLINE" => EModelAnimationInterpolation.CubicSpline,
                    _ => throw new NotSupportedException(
                        $"Animation {animationIndex} in '{path}' uses interpolation '{interpolationName}'.")
                };

                var times = ReadFloatScalars(
                    gltf,
                    buffer,
                    sampler.GetProperty("input").GetInt32(),
                    "animation input",
                    path);
                ValidateAnimationTimes(times, animationIndex, path);

                var outputAccessor = sampler.GetProperty("output").GetInt32();
                var rawValues = animationPath == EModelAnimationPath.Rotation
                    ? ReadVectors4(gltf, buffer, outputAccessor, "animation rotation", path)
                    : ReadVectors3(gltf, buffer, outputAccessor, "animation vector", path)
                        .Select(value => new Vector4(value, 0.0f))
                        .ToArray();
                if (rawValues.Any(value =>
                        !float.IsFinite(value.X) || !float.IsFinite(value.Y) ||
                        !float.IsFinite(value.Z) || !float.IsFinite(value.W)))
                {
                    throw new InvalidDataException(
                        $"Animation {animationIndex} in '{path}' contains a non-finite output value.");
                }

                Vector4[] values;
                Vector4[]? inTangents = null;
                Vector4[]? outTangents = null;
                if (interpolation == EModelAnimationInterpolation.CubicSpline)
                {
                    if (rawValues.Length != checked(times.Length * 3))
                        throw new InvalidDataException($"Cubic animation channel in '{path}' has an invalid output count.");
                    values = new Vector4[times.Length];
                    inTangents = new Vector4[times.Length];
                    outTangents = new Vector4[times.Length];
                    for (var index = 0; index < times.Length; index++)
                    {
                        inTangents[index] = rawValues[index * 3];
                        values[index] = rawValues[index * 3 + 1];
                        outTangents[index] = rawValues[index * 3 + 2];
                    }
                }
                else
                {
                    if (rawValues.Length != times.Length)
                        throw new InvalidDataException($"Animation channel in '{path}' has an invalid output count.");
                    values = rawValues;
                }

                if (animationPath == EModelAnimationPath.Rotation)
                {
                    for (var index = 0; index < values.Length; index++)
                    {
                        var rotation = new Quaternion(values[index].X, values[index].Y, values[index].Z, values[index].W);
                        if (rotation.LengthSquared() <= float.Epsilon)
                            throw new InvalidDataException($"Animation {animationIndex} in '{path}' contains a zero quaternion.");
                        rotation = Quaternion.Normalize(rotation);
                        values[index] = new Vector4(rotation.X, rotation.Y, rotation.Z, rotation.W);
                    }
                }

                channels.Add(new ModelAnimationChannel(
                    nodeIndex,
                    animationPath,
                    interpolation,
                    times,
                    values,
                    inTangents,
                    outTangents));
            }

            var name = animationElement.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString()
                : null;
            var duration = channels.Count == 0
                ? 0.0f
                : channels.Max(channel => channel.Times[^1]);
            animations.Add(new ModelAnimation(
                string.IsNullOrWhiteSpace(name) ? $"Animation {animationIndex}" : name,
                duration,
                channels.ToArray()));
            animationIndex++;
        }

        var duplicate = animations.GroupBy(animation => animation.Name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidDataException($"Model '{path}' contains duplicate animation name '{duplicate.Key}'.");

        return animations.ToArray();
    }

    private static IReadOnlyDictionary<int, int> ParseNodeSkins(
        byte[] source,
        int nodeCount,
        int skinCount,
        string path)
    {
        using var document = ReadGlbJsonDocument(source, path);
        if (!document.RootElement.TryGetProperty("nodes", out var nodesElement))
            return new Dictionary<int, int>();

        var result = new Dictionary<int, int>();
        var nodeIndex = 0;
        foreach (var nodeElement in nodesElement.EnumerateArray())
        {
            if (nodeElement.TryGetProperty("skin", out var skinElement))
            {
                var skinIndex = skinElement.GetInt32();
                if (skinIndex < 0 || skinIndex >= skinCount)
                    throw new InvalidDataException($"Node {nodeIndex} in '{path}' references invalid skin {skinIndex}.");
                result.Add(nodeIndex, skinIndex);
            }
            nodeIndex++;
        }

        if (nodeIndex != nodeCount)
            throw new InvalidDataException($"Model '{path}' contains an inconsistent node list.");
        return result;
    }

    private static void ValidateAnimationTimes(float[] times, int animationIndex, string path)
    {
        if (times.Length == 0)
            throw new InvalidDataException($"Animation {animationIndex} in '{path}' has no keyframes.");
        for (var index = 0; index < times.Length; index++)
        {
            if (!float.IsFinite(times[index]) || times[index] < 0.0f ||
                index > 0 && times[index] <= times[index - 1])
            {
                throw new InvalidDataException($"Animation {animationIndex} in '{path}' has invalid keyframe times.");
            }
        }
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

    private static Vector4[] ReadVectors4(
        Gltf gltf,
        byte[] buffer,
        int accessorIndex,
        string attribute,
        string path)
    {
        var accessor = GetAccessor(
            gltf,
            accessorIndex,
            Accessor.TypeEnum.VEC4,
            Accessor.ComponentTypeEnum.FLOAT,
            attribute,
            path);
        var result = new Vector4[accessor.Count];
        ForEachElement(
            gltf,
            accessor,
            sizeof(float) * 4,
            buffer.Length,
            path,
            offset => result[offset.Index] = new Vector4(
                ReadSingle(buffer, offset.ByteOffset),
                ReadSingle(buffer, offset.ByteOffset + sizeof(float)),
                ReadSingle(buffer, offset.ByteOffset + sizeof(float) * 2),
                ReadSingle(buffer, offset.ByteOffset + sizeof(float) * 3)));
        return result;
    }

    private static float[] ReadFloatScalars(
        Gltf gltf,
        byte[] buffer,
        int accessorIndex,
        string attribute,
        string path)
    {
        var accessor = GetAccessor(
            gltf,
            accessorIndex,
            Accessor.TypeEnum.SCALAR,
            Accessor.ComponentTypeEnum.FLOAT,
            attribute,
            path);
        var result = new float[accessor.Count];
        ForEachElement(
            gltf,
            accessor,
            sizeof(float),
            buffer.Length,
            path,
            offset => result[offset.Index] = ReadSingle(buffer, offset.ByteOffset));
        return result;
    }

    private static Matrix4x4[] ReadMatrices4(
        Gltf gltf,
        byte[] buffer,
        int accessorIndex,
        string attribute,
        string path)
    {
        var accessor = GetAccessor(
            gltf,
            accessorIndex,
            Accessor.TypeEnum.MAT4,
            Accessor.ComponentTypeEnum.FLOAT,
            attribute,
            path);
        var result = new Matrix4x4[accessor.Count];
        ForEachElement(
            gltf,
            accessor,
            sizeof(float) * 16,
            buffer.Length,
            path,
            offset =>
            {
                Span<float> values = stackalloc float[16];
                for (var component = 0; component < values.Length; component++)
                    values[component] = ReadSingle(buffer, offset.ByteOffset + component * sizeof(float));
                result[offset.Index] = new Matrix4x4(
                    values[0], values[1], values[2], values[3],
                    values[4], values[5], values[6], values[7],
                    values[8], values[9], values[10], values[11],
                    values[12], values[13], values[14], values[15]);
            });
        return result;
    }

    private static Vector4[] ReadJointVectors(
        Gltf gltf,
        byte[] buffer,
        int accessorIndex,
        string path)
    {
        if (gltf.Accessors is null || accessorIndex < 0 || accessorIndex >= gltf.Accessors.Length)
            throw new InvalidDataException($"Model '{path}' references invalid JOINTS_0 accessor {accessorIndex}.");
        var accessor = gltf.Accessors[accessorIndex];
        if (accessor.Type != Accessor.TypeEnum.VEC4 ||
            accessor.ComponentType is not (Accessor.ComponentTypeEnum.UNSIGNED_BYTE or Accessor.ComponentTypeEnum.UNSIGNED_SHORT))
        {
            throw new NotSupportedException($"JOINTS_0 accessor {accessorIndex} in '{path}' must use VEC4/UNSIGNED_BYTE or UNSIGNED_SHORT.");
        }

        var componentSize = accessor.ComponentType == Accessor.ComponentTypeEnum.UNSIGNED_BYTE
            ? sizeof(byte)
            : sizeof(ushort);
        var result = new Vector4[accessor.Count];
        ForEachElement(
            gltf,
            accessor,
            componentSize * 4,
            buffer.Length,
            path,
            offset => result[offset.Index] = new Vector4(
                ReadUnsignedComponent(buffer, offset.ByteOffset, accessor.ComponentType),
                ReadUnsignedComponent(buffer, offset.ByteOffset + componentSize, accessor.ComponentType),
                ReadUnsignedComponent(buffer, offset.ByteOffset + componentSize * 2, accessor.ComponentType),
                ReadUnsignedComponent(buffer, offset.ByteOffset + componentSize * 3, accessor.ComponentType)));
        return result;
    }

    private static Vector4[] ReadWeightVectors(
        Gltf gltf,
        byte[] buffer,
        int accessorIndex,
        string path)
    {
        if (gltf.Accessors is null || accessorIndex < 0 || accessorIndex >= gltf.Accessors.Length)
            throw new InvalidDataException($"Model '{path}' references invalid WEIGHTS_0 accessor {accessorIndex}.");
        var accessor = gltf.Accessors[accessorIndex];
        if (accessor.Type != Accessor.TypeEnum.VEC4)
            throw new NotSupportedException($"WEIGHTS_0 accessor {accessorIndex} in '{path}' must use VEC4.");

        var componentSize = accessor.ComponentType switch
        {
            Accessor.ComponentTypeEnum.FLOAT => sizeof(float),
            Accessor.ComponentTypeEnum.UNSIGNED_BYTE when accessor.Normalized => sizeof(byte),
            Accessor.ComponentTypeEnum.UNSIGNED_SHORT when accessor.Normalized => sizeof(ushort),
            _ => throw new NotSupportedException($"WEIGHTS_0 accessor {accessorIndex} in '{path}' uses an unsupported component type.")
        };
        var result = new Vector4[accessor.Count];
        ForEachElement(
            gltf,
            accessor,
            componentSize * 4,
            buffer.Length,
            path,
            offset => result[offset.Index] = new Vector4(
                ReadWeightComponent(buffer, offset.ByteOffset, accessor.ComponentType),
                ReadWeightComponent(buffer, offset.ByteOffset + componentSize, accessor.ComponentType),
                ReadWeightComponent(buffer, offset.ByteOffset + componentSize * 2, accessor.ComponentType),
                ReadWeightComponent(buffer, offset.ByteOffset + componentSize * 3, accessor.ComponentType)));
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
        IReadOnlyDictionary<int, int> nodeLights,
        IReadOnlyDictionary<int, int> nodeSkins)
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


        int? skinIndex = null;
        if (nodeSkins.TryGetValue(nodeIndex, out var currentSkinIndex))
            skinIndex = currentSkinIndex;

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
            skinIndex,
            children.ToArray());
    }

    private static float[] ParseEmissiveStrengths(
        byte[] source,
        int materialCount,
        string path)
    {
        var strengths = Enumerable.Repeat(1.0f, materialCount).ToArray();
        if (materialCount == 0)
            return strengths;

        using var document = ReadGlbJsonDocument(source, path);
        if (!document.RootElement.TryGetProperty("materials", out var materials))
            return strengths;

        var index = 0;
        foreach (var material in materials.EnumerateArray())
        {
            if (index >= strengths.Length)
                break;

            if (material.TryGetProperty("extensions", out var extensions) &&
                extensions.TryGetProperty(
                    "KHR_materials_emissive_strength",
                    out var emissive) &&
                emissive.TryGetProperty("emissiveStrength", out var strength))
            {
                var value = strength.GetSingle();
                strengths[index] = float.IsFinite(value) && value >= 0.0f
                    ? value
                    : 1.0f;
            }

            index++;
        }

        return strengths;
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

    private static float ReadUnsignedComponent(
        byte[] data,
        int offset,
        Accessor.ComponentTypeEnum componentType)
    {
        return componentType switch
        {
            Accessor.ComponentTypeEnum.UNSIGNED_BYTE => data[offset],
            Accessor.ComponentTypeEnum.UNSIGNED_SHORT =>
                BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, sizeof(ushort))),
            _ => throw new InvalidOperationException("Unexpected joint component type.")
        };
    }

    private static float ReadWeightComponent(
        byte[] data,
        int offset,
        Accessor.ComponentTypeEnum componentType)
    {
        return componentType switch
        {
            Accessor.ComponentTypeEnum.FLOAT => ReadSingle(data, offset),
            Accessor.ComponentTypeEnum.UNSIGNED_BYTE => data[offset] / (float)byte.MaxValue,
            Accessor.ComponentTypeEnum.UNSIGNED_SHORT =>
                BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, sizeof(ushort))) / (float)ushort.MaxValue,
            _ => throw new InvalidOperationException("Unexpected weight component type.")
        };
    }

    private static Vector4 NormalizeWeights(Vector4 weights)
    {
        if (!float.IsFinite(weights.X) || !float.IsFinite(weights.Y) ||
            !float.IsFinite(weights.Z) || !float.IsFinite(weights.W) ||
            weights.X < 0.0f || weights.Y < 0.0f || weights.Z < 0.0f || weights.W < 0.0f)
        {
            throw new InvalidDataException("A skinned vertex has invalid bone weights.");
        }
        var sum = weights.X + weights.Y + weights.Z + weights.W;
        if (sum <= float.Epsilon)
            throw new InvalidDataException("A skinned vertex has no positive bone weights.");
        return weights / sum;
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
