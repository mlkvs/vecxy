using Vecxy.Assets;

namespace Vecxy.Animations;

public readonly record struct AnimationEventMarker(
    float NormalizedTime,
    string Name,
    object? Payload = null);

public sealed class AnimatorController
{
    public string Name { get; }
    public IReadOnlyList<AnimatorParameter> Parameters { get; }
    public IReadOnlyList<AnimationLayer> Layers { get; }

    internal AnimatorController(
        string name,
        AnimatorParameter[] parameters,
        AnimationLayer[] layers)
    {
        Name = name;
        Parameters = Array.AsReadOnly(parameters);
        Layers = Array.AsReadOnly(layers);
    }

    public static AnimatorController Build(string name, Action<AnimatorControllerBuilder> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new AnimatorControllerBuilder(name);
        configure(builder);
        return builder.Build();
    }
}

public sealed class AnimationLayer
{
    public string Name { get; }
    public IReadOnlyList<AnimationState> States { get; }
    public IReadOnlyList<AnimationTransition> AnyStateTransitions { get; }
    public AnimationState DefaultState { get; }
    public BoneMask? Mask { get; }
    public float Weight { get; }
    public bool Additive { get; }

    internal AnimationLayer(
        string name,
        AnimationState[] states,
        AnimationTransition[] anyStateTransitions,
        AnimationState defaultState,
        BoneMask? mask,
        float weight,
        bool additive)
    {
        Name = name;
        States = Array.AsReadOnly(states);
        AnyStateTransitions = Array.AsReadOnly(anyStateTransitions);
        DefaultState = defaultState;
        Mask = mask;
        Weight = weight;
        Additive = additive;
    }
}

public sealed class AnimationState
{
    public string Name { get; }
    public AnimationMotion Motion { get; }
    public IReadOnlyList<AnimationEventMarker> Events { get; }
    public IReadOnlyList<AnimationTransition> Transitions { get; internal set; } = [];

    internal AnimationState(string name, AnimationMotion motion, AnimationEventMarker[] events)
    {
        Name = name;
        Motion = motion;
        Events = Array.AsReadOnly(events);
    }
}

public sealed class AnimationTransition
{
    public AnimationState? Source { get; }
    public AnimationState Destination { get; }
    public IReadOnlyList<AnimatorCondition> Conditions { get; }
    public float Duration { get; }
    public float? ExitTime { get; }
    public int Priority { get; }
    public bool CanInterrupt { get; }

    internal AnimationTransition(
        AnimationState? source,
        AnimationState destination,
        AnimatorCondition[] conditions,
        float duration,
        float? exitTime,
        int priority,
        bool canInterrupt)
    {
        Source = source;
        Destination = destination;
        Conditions = Array.AsReadOnly(conditions);
        Duration = duration;
        ExitTime = exitTime;
        Priority = priority;
        CanInterrupt = canInterrupt;
    }
}

public sealed class AnimatorControllerBuilder
{
    private readonly string _name;
    private readonly List<AnimatorParameter> _parameters = [];
    private readonly List<AnimationLayerBuilder> _layers = [];

    internal AnimatorControllerBuilder(string name) => _name = name;

    public AnimatorControllerBuilder Parameter(AnimatorParameter parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        if (_parameters.Any(existing => string.Equals(existing.Name, parameter.Name, StringComparison.Ordinal)))
            throw new InvalidOperationException($"Animator parameter '{parameter.Name}' is already registered.");
        _parameters.Add(parameter);
        return this;
    }

    public AnimatorControllerBuilder Layer(string name, Action<AnimationLayerBuilder> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);
        if (_layers.Any(layer => string.Equals(layer.Name, name, StringComparison.Ordinal)))
            throw new InvalidOperationException($"Animation layer '{name}' is already registered.");
        var layer = new AnimationLayerBuilder(name);
        configure(layer);
        _layers.Add(layer);
        return this;
    }

    internal AnimatorController Build()
    {
        if (_layers.Count == 0)
            throw new InvalidOperationException("Animator controller must contain at least one layer.");
        var parameterSet = _parameters.ToHashSet();
        var layers = _layers.Select(layer => layer.Build(parameterSet)).ToArray();
        return new AnimatorController(_name, _parameters.ToArray(), layers);
    }
}

public sealed class AnimationLayerBuilder
{
    private readonly List<AnimationStateBuilder> _states = [];
    private readonly List<AnimationTransitionBuilder> _transitions = [];
    private string? _defaultState;
    private BoneMask? _mask;
    private float _weight = 1.0f;
    private bool _additive;

    internal string Name { get; }

    internal AnimationLayerBuilder(string name) => Name = name;

    public AnimationLayerBuilder DefaultState(string name, ModelAnimation clip, bool loop = true)
        => DefaultState(name, new ClipMotion(clip, loop));

    public AnimationLayerBuilder DefaultState(string name, AnimationMotion motion)
    {
        AddState(name, motion);
        _defaultState = name;
        return this;
    }

    public AnimationStateBuilder State(string name, ModelAnimation clip, bool loop = true)
        => State(name, new ClipMotion(clip, loop));

    public AnimationStateBuilder State(string name, AnimationMotion motion)
        => AddState(name, motion);

    public AnimationTransitionSourceBuilder From(string state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        return new AnimationTransitionSourceBuilder(this, state);
    }

    public AnimationTransitionSourceBuilder AnyState() => new(this, null);

    public AnimationLayerBuilder Mask(BoneMask mask)
    {
        ArgumentNullException.ThrowIfNull(mask);
        _mask = mask;
        return this;
    }

    public AnimationLayerBuilder Weight(float weight)
    {
        if (!float.IsFinite(weight) || weight is < 0.0f or > 1.0f)
            throw new ArgumentOutOfRangeException(nameof(weight));
        _weight = weight;
        return this;
    }

    public AnimationLayerBuilder Additive(bool additive = true)
    {
        _additive = additive;
        return this;
    }

    internal void AddTransition(AnimationTransitionBuilder transition) => _transitions.Add(transition);

    internal AnimationLayer Build(IReadOnlySet<AnimatorParameter> parameters)
    {
        if (_states.Count == 0)
            throw new InvalidOperationException($"Animation layer '{Name}' has no states.");
        var stateNames = _states.GroupBy(state => state.Name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (stateNames is not null)
            throw new InvalidOperationException($"Layer '{Name}' contains duplicate state '{stateNames.Key}'.");
        var states = _states.Select(state => state.Build(parameters)).ToArray();
        var byName = states.ToDictionary(state => state.Name, StringComparer.Ordinal);
        var defaultName = _defaultState ?? states[0].Name;
        if (!byName.TryGetValue(defaultName, out var defaultState))
            throw new InvalidOperationException($"Layer '{Name}' has unknown default state '{defaultName}'.");

        var transitions = _transitions
            .Select(transition => transition.Build(byName, parameters))
            .ToArray();
        foreach (var state in states)
        {
            state.Transitions = Array.AsReadOnly(transitions
                .Where(transition => ReferenceEquals(transition.Source, state))
                .OrderByDescending(transition => transition.Priority)
                .ToArray());
        }
        var any = transitions.Where(transition => transition.Source is null)
            .OrderByDescending(transition => transition.Priority)
            .ToArray();
        return new AnimationLayer(Name, states, any, defaultState, _mask, _weight, _additive);
    }

    private AnimationStateBuilder AddState(string name, AnimationMotion motion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(motion);
        var state = new AnimationStateBuilder(name, motion);
        _states.Add(state);
        return state;
    }
}

public sealed class AnimationStateBuilder
{
    private readonly List<AnimationEventMarker> _events = [];
    internal string Name { get; }
    internal AnimationMotion Motion { get; }

    internal AnimationStateBuilder(string name, AnimationMotion motion)
    {
        Name = name;
        Motion = motion;
    }

    public AnimationStateBuilder Event(float normalizedTime, string name, object? payload = null)
    {
        if (!float.IsFinite(normalizedTime) || normalizedTime is < 0.0f or > 1.0f)
            throw new ArgumentOutOfRangeException(nameof(normalizedTime));
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _events.Add(new AnimationEventMarker(normalizedTime, name, payload));
        return this;
    }

    internal AnimationState Build(IReadOnlySet<AnimatorParameter> parameters)
    {
        ValidateMotion(Motion, parameters);
        return new AnimationState(Name, Motion, _events.OrderBy(value => value.NormalizedTime).ToArray());
    }

    private static void ValidateMotion(AnimationMotion motion, IReadOnlySet<AnimatorParameter> parameters)
    {
        if (motion.Duration <= 0.0f)
            throw new InvalidOperationException("Animation motions must have a positive duration.");
        switch (motion)
        {
            case ClipMotion:
                return;
            case BlendTree1D tree:
                if (!parameters.Contains(tree.Parameter))
                    throw new InvalidOperationException($"Blend parameter '{tree.Parameter.Name}' is not registered.");
                if (tree.Children.Count == 0)
                    throw new InvalidOperationException("A 1D blend tree must contain at least one child.");
                foreach (var child in tree.Children)
                    ValidateMotion(child.Motion, parameters);
                return;
            case BlendTree2D tree:
                if (!parameters.Contains(tree.X) || !parameters.Contains(tree.Y))
                    throw new InvalidOperationException("2D blend parameters must be registered.");
                if (tree.Children.Count == 0)
                    throw new InvalidOperationException("A 2D blend tree must contain at least one child.");
                foreach (var child in tree.Children)
                    ValidateMotion(child.Motion, parameters);
                return;
            default:
                throw new NotSupportedException($"Unknown animation motion '{motion.GetType().Name}'.");
        }
    }
}

public sealed class AnimationTransitionSourceBuilder
{
    private readonly AnimationLayerBuilder _layer;
    private readonly string? _source;

    internal AnimationTransitionSourceBuilder(AnimationLayerBuilder layer, string? source)
    {
        _layer = layer;
        _source = source;
    }

    public AnimationTransitionBuilder To(string destination)
    {
        var transition = new AnimationTransitionBuilder(_source, destination);
        _layer.AddTransition(transition);
        return transition;
    }
}

public sealed class AnimationTransitionBuilder
{
    private readonly List<AnimatorCondition> _conditions = [];
    private readonly string? _source;
    private readonly string _destination;
    private float _duration = 0.15f;
    private float? _exitTime;
    private int _priority;
    private bool _canInterrupt = true;

    internal AnimationTransitionBuilder(string? source, string destination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        _source = source;
        _destination = destination;
    }

    public AnimationTransitionBuilder When(AnimatorBool parameter, bool value)
        => Add(parameter, EAnimatorComparison.Equal, value);
    public AnimationTransitionBuilder When(AnimatorTrigger parameter)
        => Add(parameter, EAnimatorComparison.Equal, true);
    public AnimationTransitionBuilder When(AnimatorFloat parameter, EAnimatorComparison comparison, float value)
        => Add(parameter, comparison, value);
    public AnimationTransitionBuilder When(AnimatorInt parameter, EAnimatorComparison comparison, int value)
        => Add(parameter, comparison, value);

    public AnimationTransitionBuilder Duration(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0.0f)
            throw new ArgumentOutOfRangeException(nameof(seconds));
        _duration = seconds;
        return this;
    }

    public AnimationTransitionBuilder AfterNormalizedTime(float time)
    {
        if (!float.IsFinite(time) || time < 0.0f)
            throw new ArgumentOutOfRangeException(nameof(time));
        _exitTime = time;
        return this;
    }

    public AnimationTransitionBuilder Priority(int priority) { _priority = priority; return this; }
    public AnimationTransitionBuilder Interruptible(bool value = true) { _canInterrupt = value; return this; }

    internal AnimationTransition Build(
        IReadOnlyDictionary<string, AnimationState> states,
        IReadOnlySet<AnimatorParameter> parameters)
    {
        AnimationState? source = null;
        if (_source is not null && !states.TryGetValue(_source, out source))
            throw new InvalidOperationException($"Transition references unknown state '{_source}'.");
        if (!states.TryGetValue(_destination, out var destination))
            throw new InvalidOperationException($"Transition references unknown state '{_destination}'.");
        foreach (var condition in _conditions)
        {
            if (!parameters.Contains(condition.Parameter))
                throw new InvalidOperationException($"Transition parameter '{condition.Parameter.Name}' is not registered.");
        }
        if (source is null && _conditions.Count == 0)
            throw new InvalidOperationException("An AnyState transition must contain a condition.");
        return new AnimationTransition(source, destination, _conditions.ToArray(), _duration, _exitTime, _priority, _canInterrupt);
    }

    private AnimationTransitionBuilder Add(AnimatorParameter parameter, EAnimatorComparison comparison, object expected)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        _conditions.Add(new AnimatorCondition(parameter, comparison, expected));
        return this;
    }
}
