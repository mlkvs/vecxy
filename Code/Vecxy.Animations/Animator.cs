using System.Numerics;
using Vecxy.Rendering;
using Vecxy.Scene;

namespace Vecxy.Animations;

public enum ERootMotionMode : byte
{
    Disabled,
    ExtractOnly,
    ApplyToTransform
}

public readonly record struct RootMotionDelta(Vector3 Translation, Quaternion Rotation)
{
    public static RootMotionDelta Identity { get; } = new(Vector3.Zero, Quaternion.Identity);
}

public readonly record struct AnimatorEvent(
    Animator Animator,
    string Layer,
    string State,
    string Name,
    object? Payload);

[SingleComponent]
public sealed class Animator : AComponent
{
    private readonly Dictionary<AnimatorParameter, object> _parameters = [];
    private readonly Dictionary<string, float> _layerWeights = new(StringComparer.Ordinal);
    private readonly List<LayerRuntime> _layers = [];
    private ModelInstance? _modelInstance;
    private NodePose[]? _bindPose;
    private NodePose[]? _finalPose;
    private readonly Dictionary<AnimationLayer, bool[]> _resolvedMasks = [];
    private readonly Matrix4x4[] _paletteScratch = new Matrix4x4[SkinnedMeshRenderer.MaximumBones];
    private AnimationSamplingWorkspace? _sampling;
    private SkinnedMeshRenderer[] _skinnedRenderers = [];
    private readonly Func<Vecxy.Assets.ModelAnimation, Vecxy.Assets.ModelAnimation> _clipResolver;
    private bool _hasPreviousRootPose;
    private NodePose _previousRootPose;
    private RootMotionDelta _pendingRootMotion = RootMotionDelta.Identity;
    private int _modelVersion;
    private float _speed = 1.0f;

    public AnimatorController Controller { get; }
    public AnimatorOverrideController? Overrides { get; set; }
    public ERootMotionMode RootMotionMode { get; set; }
    public string? RootMotionNode { get; set; }
    public float Speed
    {
        get => _speed;
        set
        {
            if (!float.IsFinite(value) || value < 0.0f)
                throw new ArgumentOutOfRangeException(nameof(value));
            _speed = value;
        }
    }
    public bool ApplyAnimations { get; set; } = true;

    public event Action<AnimatorEvent>? EventFired;
    public event Action<string, string>? StateEntered;
    public event Action<string, string>? StateExited;

    public Animator(AnimatorController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        Controller = controller;
        _clipResolver = ResolveClip;
        foreach (var parameter in controller.Parameters)
            _parameters.Add(parameter, parameter.DefaultValue);
        foreach (var layer in controller.Layers)
        {
            _layers.Add(new LayerRuntime(layer));
            _layerWeights.Add(layer.Name, layer.Weight);
        }
    }

    public override void Awake()
    {
        _modelInstance = SceneObject!.GetComponent<ModelInstance>() ??
            throw new InvalidOperationException("Animator must be attached to a model root containing ModelInstance.");
        var model = _modelInstance.Model;
        _bindPose = model.Nodes.Select(node => NodePose.FromMatrix(node.LocalTransform)).ToArray();
        _finalPose = new NodePose[_bindPose.Length];
        _sampling = new AnimationSamplingWorkspace(_bindPose);
        _skinnedRenderers = SceneObject.GetComponentsInChildren<SkinnedMeshRenderer>().ToArray();
        _modelVersion = model.Version;
        ValidateControllerModel();
        ResetPose();
    }

    public override void OnEnable()
    {
        _hasPreviousRootPose = false;
        _pendingRootMotion = RootMotionDelta.Identity;
    }

    public override void OnDisable() => ResetPose();

    public void SetFloat(AnimatorFloat parameter, float value)
    {
        if (!float.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        Set(parameter, value);
    }

    public float GetFloat(AnimatorFloat parameter) => Get<float>(parameter);
    public void SetInt(AnimatorInt parameter, int value) => Set(parameter, value);
    public int GetInt(AnimatorInt parameter) => Get<int>(parameter);
    public void SetBool(AnimatorBool parameter, bool value) => Set(parameter, value);
    public bool GetBool(AnimatorBool parameter) => Get<bool>(parameter);
    public void SetTrigger(AnimatorTrigger parameter) => Set(parameter, true);
    public void ResetTrigger(AnimatorTrigger parameter) => Set(parameter, false);
    public bool IsTriggerSet(AnimatorTrigger parameter) => Get<bool>(parameter);

    public string GetCurrentState(string layer = "Base") => FindLayer(layer).Current.Name;

    public float GetNormalizedTime(string layer = "Base")
    {
        var runtime = FindLayer(layer);
        return GetStateNormalizedTime(runtime.Time, runtime.Current.Motion);
    }

    public bool IsInTransition(string layer = "Base") => FindLayer(layer).Transition is not null;

    public void SetLayerWeight(string layer, float weight)
    {
        if (!float.IsFinite(weight) || weight is < 0.0f or > 1.0f)
            throw new ArgumentOutOfRangeException(nameof(weight));
        FindLayer(layer);
        _layerWeights[layer] = weight;
    }

    public float GetLayerWeight(string layer) => _layerWeights[FindLayer(layer).Definition.Name];

    public void Play(string state, string layer = "Base", float normalizedTime = 0.0f)
    {
        ValidateNormalizedTime(normalizedTime);
        var runtime = FindLayer(layer);
        var destination = FindState(runtime.Definition, state);
        ChangeState(runtime, destination, normalizedTime * destination.Motion.Duration);
    }

    public void CrossFade(string state, float duration, string layer = "Base", float normalizedTime = 0.0f)
    {
        if (!float.IsFinite(duration) || duration < 0.0f)
            throw new ArgumentOutOfRangeException(nameof(duration));
        ValidateNormalizedTime(normalizedTime);
        var runtime = FindLayer(layer);
        var destination = FindState(runtime.Definition, state);
        BeginTransition(runtime, new AnimationTransition(
            runtime.Current,
            destination,
            [],
            duration,
            null,
            int.MaxValue,
            true), normalizedTime * destination.Motion.Duration);
    }

    public RootMotionDelta ConsumeRootMotion()
    {
        var result = _pendingRootMotion;
        _pendingRootMotion = RootMotionDelta.Identity;
        return result;
    }

    internal void Evaluate(float deltaTime)
    {
        if (_modelInstance is null || _bindPose is null || _finalPose is null || _sampling is null || !IsActive)
            return;
        if (!float.IsFinite(deltaTime) || deltaTime < 0.0f)
            throw new ArgumentOutOfRangeException(nameof(deltaTime));

        RefreshModelIfNeeded();

        _bindPose.CopyTo(_finalPose, 0);
        for (var layerIndex = 0; layerIndex < _layers.Count; layerIndex++)
        {
            var runtime = _layers[layerIndex];
            AdvanceLayer(runtime, deltaTime * Speed);
            var sample = SampleLayer(runtime);
            ApplyLayer(runtime.Definition, sample, _layerWeights[runtime.Definition.Name]);
        }

        ProcessRootMotion();
        if (ApplyAnimations)
            ApplyPose();
        UpdateBonePalettes();
    }

    private void AdvanceLayer(LayerRuntime runtime, float deltaTime)
    {
        var previousTime = runtime.Time;
        runtime.Time = AdvanceMotionTime(runtime.Time, deltaTime, runtime.Current.Motion);
        DispatchEvents(runtime, runtime.Current, previousTime, runtime.Time);

        if (runtime.Transition is { } active)
        {
            var previousDestinationTime = runtime.DestinationTime;
            runtime.DestinationTime = AdvanceMotionTime(
                runtime.DestinationTime,
                deltaTime,
                active.Destination.Motion);
            DispatchEvents(
                runtime,
                active.Destination,
                previousDestinationTime,
                runtime.DestinationTime);
            runtime.TransitionTime += deltaTime;
            if (runtime.TransitionTime >= active.Duration)
            {
                StateExited?.Invoke(runtime.Definition.Name, runtime.Current.Name);
                runtime.Current = active.Destination;
                runtime.Time = runtime.DestinationTime;
                runtime.Transition = null;
                runtime.TransitionTime = 0.0f;
                StateEntered?.Invoke(runtime.Definition.Name, runtime.Current.Name);
            }
            else if (active.CanInterrupt)
            {
                var interrupt = FindTransition(runtime, runtime.Current);
                if (interrupt is not null && !ReferenceEquals(interrupt, active))
                    BeginTransition(runtime, interrupt, 0.0f);
            }
            return;
        }

        var transition = FindTransition(runtime, runtime.Current);
        if (transition is not null)
            BeginTransition(runtime, transition, 0.0f);
    }

    private AnimationTransition? FindTransition(LayerRuntime runtime, AnimationState state)
    {
        foreach (var transition in runtime.Definition.AnyStateTransitions.Concat(state.Transitions))
        {
            if (ReferenceEquals(transition.Destination, state) && transition.Source is null)
                continue;
            if (transition.ExitTime is { } exit && GetStateNormalizedTime(runtime.Time, state.Motion) < exit)
                continue;
            if (transition.Conditions.All(IsConditionMet))
                return transition;
        }
        return null;
    }

    private bool IsConditionMet(AnimatorCondition condition)
    {
        var actual = _parameters[condition.Parameter];
        return (actual, condition.Expected) switch
        {
            (float left, float right) => Compare(left, right, condition.Comparison),
            (int left, int right) => Compare(left, right, condition.Comparison),
            (bool left, bool right) => condition.Comparison switch
            {
                EAnimatorComparison.Equal => left == right,
                EAnimatorComparison.NotEqual => left != right,
                _ => throw new InvalidOperationException("Boolean parameters support only equality comparisons.")
            },
            _ => false
        };
    }

    private static bool Compare<T>(T left, T right, EAnimatorComparison comparison) where T : IComparable<T>
    {
        var value = left.CompareTo(right);
        return comparison switch
        {
            EAnimatorComparison.Equal => value == 0,
            EAnimatorComparison.NotEqual => value != 0,
            EAnimatorComparison.Less => value < 0,
            EAnimatorComparison.LessOrEqual => value <= 0,
            EAnimatorComparison.Greater => value > 0,
            EAnimatorComparison.GreaterOrEqual => value >= 0,
            _ => false
        };
    }

    private void BeginTransition(LayerRuntime runtime, AnimationTransition transition, float destinationTime)
    {
        foreach (var condition in transition.Conditions)
        {
            if (condition.Parameter is AnimatorTrigger)
                _parameters[condition.Parameter] = false;
        }
        if (transition.Duration <= float.Epsilon)
        {
            ChangeState(runtime, transition.Destination, destinationTime);
            return;
        }
        runtime.Transition = transition;
        runtime.TransitionTime = 0.0f;
        runtime.DestinationTime = destinationTime;
    }

    private void ChangeState(LayerRuntime runtime, AnimationState state, float time)
    {
        StateExited?.Invoke(runtime.Definition.Name, runtime.Current.Name);
        runtime.Current = state;
        runtime.Time = Math.Max(0.0f, time);
        runtime.Transition = null;
        runtime.TransitionTime = 0.0f;
        runtime.DestinationTime = 0.0f;
        StateEntered?.Invoke(runtime.Definition.Name, state.Name);
    }

    private PoseSample SampleLayer(LayerRuntime runtime)
    {
        _sampling!.Reset();
        var current = _sampling.Sample(runtime.Current.Motion, GetSampleNormalizedTime(runtime.Time, runtime.Current.Motion), _parameters, _clipResolver);
        if (runtime.Transition is not { } transition)
            return current;
        var destination = _sampling.Sample(transition.Destination.Motion,
            GetSampleNormalizedTime(runtime.DestinationTime, transition.Destination.Motion), _parameters, _clipResolver);
        var amount = transition.Duration <= float.Epsilon ? 1.0f : runtime.TransitionTime / transition.Duration;
        return _sampling.Blend(current, destination, amount);
    }

    private void ApplyLayer(AnimationLayer layer, PoseSample sample, float weight)
    {
        if (weight <= 0.0f)
            return;
        var mask = _resolvedMasks[layer];
        for (var index = 0; index < _finalPose!.Length; index++)
        {
            if (!sample.Written[index] || !mask[index])
                continue;
            if (layer.Additive)
            {
                var bind = _bindPose![index];
                var sampled = sample.Pose[index];
                _finalPose[index].Position += (sampled.Position - bind.Position) * weight;
                var scaleDelta = new Vector3(
                    SafeRatio(sampled.Scale.X, bind.Scale.X),
                    SafeRatio(sampled.Scale.Y, bind.Scale.Y),
                    SafeRatio(sampled.Scale.Z, bind.Scale.Z));
                _finalPose[index].Scale *= Vector3.Lerp(Vector3.One, scaleDelta, weight);
                var rotationDelta = Quaternion.Normalize(Quaternion.Inverse(bind.Rotation) * sampled.Rotation);
                _finalPose[index].Rotation = Quaternion.Normalize(
                    _finalPose[index].Rotation * Quaternion.Slerp(Quaternion.Identity, rotationDelta, weight));
            }
            else
            {
                _finalPose[index] = AnimationSamplingWorkspace.BlendPose(_finalPose[index], sample.Pose[index], weight);
            }
        }
    }

    private bool[] ResolveMask(BoneMask? mask)
    {
        var result = new bool[_modelInstance!.Nodes.Count];
        if (mask is null)
        {
            Array.Fill(result, true);
            return result;
        }
        var model = _modelInstance!.Model;
        foreach (var rootName in mask.Roots)
        {
            var matches = model.Nodes.Select((node, index) => (node, index))
                .Where(value => string.Equals(value.node.Name, rootName, StringComparison.Ordinal))
                .ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException($"Bone mask node '{rootName}' must identify exactly one model node.");
            Mark(matches[0].index);
        }
        return result;

        void Mark(int nodeIndex)
        {
            result[nodeIndex] = true;
            if (!mask.IncludeDescendants)
                return;
            foreach (var child in model.Nodes[nodeIndex].Children)
                Mark(child);
        }
    }

    private void ApplyPose()
    {
        for (var index = 0; index < _finalPose!.Length; index++)
        {
            if (!_modelInstance!.TryGetNode(index, out var node))
                continue;
            var transform = node.Transform;
            transform.LocalPosition = _finalPose[index].Position;
            transform.LocalRotation = _finalPose[index].Rotation;
            transform.LocalScale = _finalPose[index].Scale;
        }
    }

    private void UpdateBonePalettes()
    {
        foreach (var renderer in _skinnedRenderers)
        {
            var skin = _modelInstance!.Model.Skins[renderer.SkinIndex];
            var meshWorld = _modelInstance.GetNode(renderer.NodeIndex).Transform.WorldMatrix;
            if (!Matrix4x4.Invert(meshWorld, out var inverseMeshWorld))
                throw new InvalidOperationException("Skinned mesh transform is not invertible.");
            var palette = _paletteScratch.AsSpan(0, skin.Joints.Count);
            for (var jointIndex = 0; jointIndex < skin.Joints.Count; jointIndex++)
            {
                var jointWorld = _modelInstance.GetNode(skin.Joints[jointIndex]).Transform.WorldMatrix;
                palette[jointIndex] = skin.InverseBindMatrices[jointIndex] * jointWorld * inverseMeshWorld;
            }
            renderer.SetBoneMatrices(palette);
        }
    }

    private void ProcessRootMotion()
    {
        if (RootMotionMode == ERootMotionMode.Disabled || string.IsNullOrWhiteSpace(RootMotionNode))
        {
            _hasPreviousRootPose = false;
            return;
        }
        var node = _modelInstance!.GetNode(RootMotionNode);
        var nodeIndex = _modelInstance.Nodes.IndexOf(node);
        var current = _finalPose![nodeIndex];
        if (_hasPreviousRootPose)
        {
            var delta = new RootMotionDelta(
                current.Position - _previousRootPose.Position,
                Quaternion.Normalize(Quaternion.Inverse(_previousRootPose.Rotation) * current.Rotation));
            _pendingRootMotion = new RootMotionDelta(
                _pendingRootMotion.Translation + delta.Translation,
                Quaternion.Normalize(_pendingRootMotion.Rotation * delta.Rotation));
            if (RootMotionMode == ERootMotionMode.ApplyToTransform)
            {
                Transform.Translate(delta.Translation);
                Transform.Rotate(delta.Rotation);
                _finalPose[nodeIndex].Position = _bindPose![nodeIndex].Position;
                _finalPose[nodeIndex].Rotation = _bindPose[nodeIndex].Rotation;
            }
        }
        _previousRootPose = current;
        _hasPreviousRootPose = true;
    }

    private void DispatchEvents(LayerRuntime runtime, AnimationState state, float previous, float current)
    {
        if (state.Events.Count == 0 || state.Motion.Duration <= float.Epsilon || current <= previous)
            return;
        var duration = state.Motion.Duration;
        var startLoop = (int)MathF.Floor(previous / duration);
        var endLoop = (int)MathF.Floor(current / duration);
        for (var loop = startLoop; loop <= endLoop; loop++)
        {
            foreach (var marker in state.Events)
            {
                var eventTime = loop * duration + marker.NormalizedTime * duration;
                if (eventTime > previous && eventTime <= current)
                    EventFired?.Invoke(new AnimatorEvent(this, runtime.Definition.Name, state.Name, marker.Name, marker.Payload));
            }
        }
    }

    private void ResetPose()
    {
        if (_modelInstance is null || _bindPose is null)
            return;
        for (var index = 0; index < _bindPose.Length; index++)
        {
            if (!_modelInstance.TryGetNode(index, out var node))
                continue;
            var transform = node.Transform;
            transform.LocalPosition = _bindPose[index].Position;
            transform.LocalRotation = _bindPose[index].Rotation;
            transform.LocalScale = _bindPose[index].Scale;
        }
    }

    private void ValidateControllerModel()
    {
        _resolvedMasks.Clear();
        foreach (var layer in Controller.Layers)
        {
            _resolvedMasks.Add(layer, ResolveMask(layer.Mask));
            foreach (var state in layer.States)
                ValidateMotion(state.Motion);
        }
    }

    private void RefreshModelIfNeeded()
    {
        var model = _modelInstance!.Model;
        if (model.Version == _modelVersion)
            return;
        if (model.Nodes.Count != _modelInstance.Nodes.Count)
        {
            throw new InvalidOperationException(
                "A hot-reloaded animated model changed its node count; recreate the model instance.");
        }
        _bindPose = model.Nodes.Select(node => NodePose.FromMatrix(node.LocalTransform)).ToArray();
        _finalPose = new NodePose[_bindPose.Length];
        _sampling = new AnimationSamplingWorkspace(_bindPose);
        _modelVersion = model.Version;
        ValidateControllerModel();
    }

    private Vecxy.Assets.ModelAnimation ResolveClip(Vecxy.Assets.ModelAnimation original)
    {
        var overridden = Overrides?.Resolve(original) ?? original;
        if (!ReferenceEquals(overridden, original))
            return overridden;
        return _modelInstance!.Model.Animations.FirstOrDefault(animation =>
                   string.Equals(animation.Name, original.Name, StringComparison.Ordinal))
               ?? original;
    }

    private void ValidateMotion(AnimationMotion motion)
    {
        switch (motion)
        {
            case ClipMotion clip:
                foreach (var channel in clip.Clip.Channels)
                {
                    if (channel.NodeIndex < 0 || channel.NodeIndex >= _modelInstance!.Nodes.Count)
                        throw new InvalidOperationException($"Clip '{clip.Clip.Name}' is incompatible with this model.");
                }
                break;
            case BlendTree1D tree:
                foreach (var child in tree.Children) ValidateMotion(child.Motion);
                break;
            case BlendTree2D tree:
                foreach (var child in tree.Children) ValidateMotion(child.Motion);
                break;
        }
    }

    private LayerRuntime FindLayer(string name) => _layers.FirstOrDefault(layer =>
        string.Equals(layer.Definition.Name, name, StringComparison.Ordinal)) ??
        throw new KeyNotFoundException($"Controller '{Controller.Name}' has no layer '{name}'.");

    private static AnimationState FindState(AnimationLayer layer, string name) => layer.States.FirstOrDefault(state =>
        string.Equals(state.Name, name, StringComparison.Ordinal)) ??
        throw new KeyNotFoundException($"Layer '{layer.Name}' has no state '{name}'.");

    private static float AdvanceMotionTime(float time, float deltaTime, AnimationMotion motion)
    {
        var next = time + deltaTime * motion.Speed;
        return motion.Loop ? next : Math.Min(next, motion.Duration);
    }

    private static float GetStateNormalizedTime(float time, AnimationMotion motion)
    {
        if (motion.Duration <= float.Epsilon)
            return 0.0f;
        return time / motion.Duration;
    }

    private static float GetSampleNormalizedTime(float time, AnimationMotion motion)
    {
        var normalized = GetStateNormalizedTime(time, motion);
        return motion.Loop
            ? normalized - MathF.Floor(normalized)
            : Math.Clamp(normalized, 0.0f, 1.0f);
    }

    private static void ValidateNormalizedTime(float normalizedTime)
    {
        if (!float.IsFinite(normalizedTime) || normalizedTime < 0.0f)
            throw new ArgumentOutOfRangeException(nameof(normalizedTime));
    }

    private void Set<T>(AnimatorParameter<T> parameter, T value)
    {
        EnsureParameter(parameter);
        _parameters[parameter] = value!;
    }

    private T Get<T>(AnimatorParameter<T> parameter)
    {
        EnsureParameter(parameter);
        return (T)_parameters[parameter];
    }

    private void EnsureParameter(AnimatorParameter parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        if (!_parameters.ContainsKey(parameter))
            throw new KeyNotFoundException($"Parameter '{parameter.Name}' is not part of controller '{Controller.Name}'.");
    }

    private static float SafeRatio(float value, float basis) =>
        MathF.Abs(basis) <= float.Epsilon ? 1.0f : value / basis;

    private sealed class LayerRuntime
    {
        public AnimationLayer Definition { get; }
        public AnimationState Current { get; set; }
        public float Time { get; set; }
        public AnimationTransition? Transition { get; set; }
        public float TransitionTime { get; set; }
        public float DestinationTime { get; set; }

        public LayerRuntime(AnimationLayer definition)
        {
            Definition = definition;
            Current = definition.DefaultState;
        }
    }
}

internal static class ReadOnlyListExtensions
{
    public static int IndexOf<T>(this IReadOnlyList<T> values, T value)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (EqualityComparer<T>.Default.Equals(values[index], value))
                return index;
        }
        return -1;
    }
}
