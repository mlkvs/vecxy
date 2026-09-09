using System.Numerics;

namespace Vecxy.Rendering;

public sealed class SkinnedMeshRenderer : MeshRenderer
{
    public const int MaximumBones = 64;

    private Matrix4x4[] _boneMatrices = [];

    public int NodeIndex { get; private set; }
    public int SkinIndex { get; private set; }
    public IReadOnlyList<Matrix4x4> BoneMatrices => _boneMatrices;
    internal ReadOnlySpan<Matrix4x4> BoneMatrixSpan => _boneMatrices;

    internal void ConfigureSkin(int nodeIndex, int skinIndex, int jointCount)
    {
        if (jointCount is <= 0 or > MaximumBones)
        {
            throw new NotSupportedException(
                $"A skinned mesh must use between 1 and {MaximumBones} joints; received {jointCount}.");
        }

        NodeIndex = nodeIndex;
        SkinIndex = skinIndex;
        _boneMatrices = Enumerable.Repeat(Matrix4x4.Identity, jointCount).ToArray();
    }

    public void SetBoneMatrices(ReadOnlySpan<Matrix4x4> matrices)
    {
        ObjectDisposedException.ThrowIf(IsDestroyed, this);
        if (matrices.Length != _boneMatrices.Length)
            throw new ArgumentException("Bone palette size does not match the configured skin.", nameof(matrices));
        matrices.CopyTo(_boneMatrices);
    }
}
