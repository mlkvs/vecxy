using System.Numerics;
using Vecxy.Assets;

namespace Vecxy.Animations;

public abstract class AnimationMotion
{
    public float Speed { get; init; } = 1.0f;
    public bool Loop { get; init; } = true;
    public abstract float Duration { get; }
}

public sealed class ClipMotion : AnimationMotion
{
    public ModelAnimation Clip { get; }
    public override float Duration => Clip.Duration;

    public ClipMotion(ModelAnimation clip, bool loop = true, float speed = 1.0f)
    {
        ArgumentNullException.ThrowIfNull(clip);
        if (!float.IsFinite(speed) || speed < 0.0f)
            throw new ArgumentOutOfRangeException(nameof(speed));
        Clip = clip;
        Loop = loop;
        Speed = speed;
    }
}

public sealed class BlendTree1D : AnimationMotion
{
    private readonly List<Child> _children = [];

    public AnimatorFloat Parameter { get; }
    public IReadOnlyList<Child> Children => _children;
    public override float Duration => _children.Count == 0 ? 0.0f : _children.Max(child => child.Motion.Duration);

    public BlendTree1D(AnimatorFloat parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        Parameter = parameter;
    }

    public BlendTree1D At(float threshold, ModelAnimation clip, bool loop = true)
        => At(threshold, new ClipMotion(clip, loop));

    public BlendTree1D At(float threshold, AnimationMotion motion)
    {
        if (!float.IsFinite(threshold))
            throw new ArgumentOutOfRangeException(nameof(threshold));
        ArgumentNullException.ThrowIfNull(motion);
        if (_children.Any(child => child.Threshold == threshold))
            throw new InvalidOperationException($"Blend tree already contains threshold {threshold}.");
        _children.Add(new Child(threshold, motion));
        _children.Sort((left, right) => left.Threshold.CompareTo(right.Threshold));
        return this;
    }

    public readonly record struct Child(float Threshold, AnimationMotion Motion);
}

public sealed class BlendTree2D : AnimationMotion
{
    private readonly List<Child> _children = [];

    public AnimatorFloat X { get; }
    public AnimatorFloat Y { get; }
    public IReadOnlyList<Child> Children => _children;
    public override float Duration => _children.Count == 0 ? 0.0f : _children.Max(child => child.Motion.Duration);

    public BlendTree2D(AnimatorFloat x, AnimatorFloat y)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        X = x;
        Y = y;
    }

    public BlendTree2D At(float x, float y, ModelAnimation clip, bool loop = true)
        => At(new Vector2(x, y), new ClipMotion(clip, loop));

    public BlendTree2D At(Vector2 position, AnimationMotion motion)
    {
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y))
            throw new ArgumentOutOfRangeException(nameof(position));
        ArgumentNullException.ThrowIfNull(motion);
        if (_children.Any(child => child.Position == position))
            throw new InvalidOperationException($"Blend tree already contains point {position}.");
        _children.Add(new Child(position, motion));
        return this;
    }

    public readonly record struct Child(Vector2 Position, AnimationMotion Motion);
}

public sealed class BoneMask
{
    public IReadOnlyList<string> Roots { get; }
    public bool IncludeDescendants { get; }

    private BoneMask(string[] roots, bool includeDescendants)
    {
        if (roots.Length == 0 || roots.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("A bone mask must contain at least one valid node name.", nameof(roots));
        Roots = Array.AsReadOnly(roots.Distinct(StringComparer.Ordinal).ToArray());
        IncludeDescendants = includeDescendants;
    }

    public static BoneMask From(params string[] roots) => new(roots, true);
    public static BoneMask Exact(params string[] nodes) => new(nodes, false);
}

public sealed class AnimatorOverrideController
{
    private readonly Dictionary<string, ModelAnimation> _clips = new(StringComparer.Ordinal);

    public AnimatorOverrideController Override(string originalClip, ModelAnimation replacement)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalClip);
        ArgumentNullException.ThrowIfNull(replacement);
        _clips[originalClip] = replacement;
        return this;
    }

    internal ModelAnimation Resolve(ModelAnimation clip) =>
        _clips.GetValueOrDefault(clip.Name) ?? clip;
}
