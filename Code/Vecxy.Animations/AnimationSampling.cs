using System.Numerics;
using Vecxy.Assets;

namespace Vecxy.Animations;

internal struct NodePose
{
    public Vector3 Position;
    public Quaternion Rotation;
    public Vector3 Scale;

    public static NodePose FromMatrix(Matrix4x4 matrix)
    {
        if (!Matrix4x4.Decompose(matrix, out var scale, out var rotation, out var position))
            throw new InvalidDataException("An animated model node transform cannot be decomposed.");
        return new NodePose
        {
            Position = position,
            Rotation = Quaternion.Normalize(rotation),
            Scale = scale
        };
    }
}

internal readonly record struct PoseSample(NodePose[] Pose, bool[] Written);

internal sealed class AnimationSamplingWorkspace
{
    private readonly NodePose[] _bindPose;
    private readonly List<PoseSample> _buffers = [];
    private int _next;

    public AnimationSamplingWorkspace(NodePose[] bindPose, int maximumBlendChildren = 32)
    {
        _bindPose = bindPose;
        MaximumBlendChildren = maximumBlendChildren;
    }

    public int MaximumBlendChildren { get; }

    public PoseSample Sample(
        AnimationMotion motion,
        float normalizedTime,
        IReadOnlyDictionary<AnimatorParameter, object> parameters,
        Func<ModelAnimation, ModelAnimation>? resolveClip)
    {
        return SampleRecursive(motion, normalizedTime, parameters, resolveClip);
    }

    public void Reset() => _next = 0;

    public PoseSample Blend(PoseSample left, PoseSample right, float weight)
    {
        var result = Rent();
        BlendInto(left, right, weight, result);
        return result;
    }

    private PoseSample SampleRecursive(
        AnimationMotion motion,
        float normalizedTime,
        IReadOnlyDictionary<AnimatorParameter, object> parameters,
        Func<ModelAnimation, ModelAnimation>? resolveClip)
    {
        switch (motion)
        {
            case ClipMotion clipMotion:
            {
                var result = Rent();
                SampleClip(resolveClip?.Invoke(clipMotion.Clip) ?? clipMotion.Clip, normalizedTime, result);
                return result;
            }
            case BlendTree1D tree:
                return Sample1D(tree, normalizedTime, parameters, resolveClip);
            case BlendTree2D tree:
                return Sample2D(tree, normalizedTime, parameters, resolveClip);
            default:
                throw new NotSupportedException($"Unknown animation motion '{motion.GetType().Name}'.");
        }
    }

    private PoseSample Sample1D(
        BlendTree1D tree,
        float normalizedTime,
        IReadOnlyDictionary<AnimatorParameter, object> parameters,
        Func<ModelAnimation, ModelAnimation>? resolveClip)
    {
        var value = (float)parameters[tree.Parameter];
        var right = 0;
        while (right < tree.Children.Count && tree.Children[right].Threshold < value)
            right++;
        if (right == 0)
            return SampleRecursive(tree.Children[0].Motion, normalizedTime, parameters, resolveClip);
        if (right == tree.Children.Count)
            return SampleRecursive(tree.Children[^1].Motion, normalizedTime, parameters, resolveClip);

        var leftChild = tree.Children[right - 1];
        var rightChild = tree.Children[right];
        var weight = (value - leftChild.Threshold) / (rightChild.Threshold - leftChild.Threshold);
        var left = SampleRecursive(leftChild.Motion, normalizedTime, parameters, resolveClip);
        var rightSample = SampleRecursive(rightChild.Motion, normalizedTime, parameters, resolveClip);
        var result = Rent();
        BlendInto(left, rightSample, weight, result);
        return result;
    }

    private PoseSample Sample2D(
        BlendTree2D tree,
        float normalizedTime,
        IReadOnlyDictionary<AnimatorParameter, object> parameters,
        Func<ModelAnimation, ModelAnimation>? resolveClip)
    {
        if (tree.Children.Count > MaximumBlendChildren)
            throw new NotSupportedException($"A 2D blend tree supports at most {MaximumBlendChildren} children.");
        var point = new Vector2((float)parameters[tree.X], (float)parameters[tree.Y]);
        foreach (var child in tree.Children)
        {
            if (Vector2.DistanceSquared(child.Position, point) <= 0.000001f)
                return SampleRecursive(child.Motion, normalizedTime, parameters, resolveClip);
        }

        Span<float> weights = stackalloc float[tree.Children.Count];
        var total = 0.0f;
        for (var index = 0; index < tree.Children.Count; index++)
        {
            weights[index] = 1.0f / MathF.Max(
                Vector2.DistanceSquared(tree.Children[index].Position, point),
                0.000001f);
            total += weights[index];
        }

        var accumulated = SampleRecursive(tree.Children[0].Motion, normalizedTime, parameters, resolveClip);
        var accumulatedWeight = weights[0] / total;
        for (var index = 1; index < tree.Children.Count; index++)
        {
            var child = SampleRecursive(tree.Children[index].Motion, normalizedTime, parameters, resolveClip);
            var childWeight = weights[index] / total;
            var result = Rent();
            BlendInto(accumulated, child, childWeight / (accumulatedWeight + childWeight), result);
            accumulated = result;
            accumulatedWeight += childWeight;
        }
        return accumulated;
    }

    private PoseSample Rent()
    {
        if (_next == _buffers.Count)
            _buffers.Add(new PoseSample(new NodePose[_bindPose.Length], new bool[_bindPose.Length]));
        var result = _buffers[_next++];
        _bindPose.CopyTo(result.Pose, 0);
        Array.Clear(result.Written);
        return result;
    }

    private void SampleClip(ModelAnimation clip, float normalizedTime, PoseSample result)
    {
        var time = Math.Clamp(normalizedTime, 0.0f, 1.0f) * clip.Duration;
        foreach (var channel in clip.Channels)
        {
            var value = SampleChannel(channel, time);
            ref var pose = ref result.Pose[channel.NodeIndex];
            switch (channel.Path)
            {
                case EModelAnimationPath.Translation:
                    pose.Position = new Vector3(value.X, value.Y, value.Z);
                    break;
                case EModelAnimationPath.Rotation:
                    pose.Rotation = Quaternion.Normalize(new Quaternion(value.X, value.Y, value.Z, value.W));
                    break;
                case EModelAnimationPath.Scale:
                    pose.Scale = new Vector3(value.X, value.Y, value.Z);
                    break;
            }
            result.Written[channel.NodeIndex] = true;
        }
    }

    private void BlendInto(PoseSample left, PoseSample right, float weight, PoseSample result)
    {
        weight = Math.Clamp(weight, 0.0f, 1.0f);
        for (var index = 0; index < result.Pose.Length; index++)
        {
            var a = left.Written[index] ? left.Pose[index] : _bindPose[index];
            var b = right.Written[index] ? right.Pose[index] : _bindPose[index];
            result.Pose[index] = BlendPose(a, b, weight);
            result.Written[index] = left.Written[index] || right.Written[index];
        }
    }

    public static NodePose BlendPose(NodePose left, NodePose right, float weight) => new()
    {
        Position = Vector3.Lerp(left.Position, right.Position, weight),
        Rotation = Quaternion.Normalize(Quaternion.Slerp(left.Rotation, right.Rotation, weight)),
        Scale = Vector3.Lerp(left.Scale, right.Scale, weight)
    };

    private static Vector4 SampleChannel(ModelAnimationChannel channel, float time)
    {
        if (time <= channel.Times[0])
            return channel.Values[0];
        if (time >= channel.Times[^1])
            return channel.Values[^1];

        var upper = 1;
        while (upper < channel.Times.Count && channel.Times[upper] < time)
            upper++;
        var lower = upper - 1;
        var span = channel.Times[upper] - channel.Times[lower];
        var amount = (time - channel.Times[lower]) / span;
        if (channel.Interpolation == EModelAnimationInterpolation.Step)
            return channel.Values[lower];

        var left = channel.Values[lower];
        var right = channel.Values[upper];
        Vector4 value;
        if (channel.Interpolation == EModelAnimationInterpolation.CubicSpline)
        {
            var t2 = amount * amount;
            var t3 = t2 * amount;
            value = (2 * t3 - 3 * t2 + 1) * left +
                    (t3 - 2 * t2 + amount) * span * channel.OutTangents![lower] +
                    (-2 * t3 + 3 * t2) * right +
                    (t3 - t2) * span * channel.InTangents![upper];
        }
        else if (channel.Path == EModelAnimationPath.Rotation)
        {
            var a = new Quaternion(left.X, left.Y, left.Z, left.W);
            var b = new Quaternion(right.X, right.Y, right.Z, right.W);
            var rotation = Quaternion.Normalize(Quaternion.Slerp(a, b, amount));
            return new Vector4(rotation.X, rotation.Y, rotation.Z, rotation.W);
        }
        else
        {
            value = Vector4.Lerp(left, right, amount);
        }

        if (channel.Path != EModelAnimationPath.Rotation)
            return value;
        var normalized = Quaternion.Normalize(new Quaternion(value.X, value.Y, value.Z, value.W));
        return new Vector4(normalized.X, normalized.Y, normalized.Z, normalized.W);
    }
}
