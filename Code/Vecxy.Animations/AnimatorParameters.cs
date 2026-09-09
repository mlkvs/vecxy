namespace Vecxy.Animations;

public abstract class AnimatorParameter
{
    public string Name { get; }
    internal abstract object DefaultValue { get; }

    protected AnimatorParameter(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    public override string ToString() => Name;
}

public abstract class AnimatorParameter<T>(string name, T defaultValue) : AnimatorParameter(name)
{
    public T Default { get; } = defaultValue;
    internal sealed override object DefaultValue => Default!;
}

public sealed class AnimatorFloat(string name, float defaultValue = 0.0f)
    : AnimatorParameter<float>(name, defaultValue);

public sealed class AnimatorInt(string name, int defaultValue = 0)
    : AnimatorParameter<int>(name, defaultValue);

public sealed class AnimatorBool(string name, bool defaultValue = false)
    : AnimatorParameter<bool>(name, defaultValue);

public sealed class AnimatorTrigger(string name)
    : AnimatorParameter<bool>(name, false);

public enum EAnimatorComparison : byte
{
    Equal,
    NotEqual,
    Less,
    LessOrEqual,
    Greater,
    GreaterOrEqual
}

public readonly record struct AnimatorCondition(
    AnimatorParameter Parameter,
    EAnimatorComparison Comparison,
    object Expected);
