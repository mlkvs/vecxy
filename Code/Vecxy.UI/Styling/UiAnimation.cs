using System.Globalization;
using System.Numerics;

namespace Vecxy.UI;

internal readonly record struct UiTransitionDefinition(
    string Property,
    float Duration,
    float Delay,
    string Easing);

internal readonly record struct UiAnimationDefinition(
    string Name,
    float Duration,
    float Delay,
    float Iterations,
    string Easing,
    string FillMode)
{
    public static UiAnimationDefinition None { get; } =
        new("none", 0.0f, 0.0f, 1.0f, "ease", "none");
}

internal sealed record UiKeyframes(
    string Name,
    IReadOnlyList<UiKeyframe> Frames);

internal sealed record UiKeyframe(
    float Offset,
    UiKeyframeValues Values);

[Flags]
internal enum UiKeyframeProperties
{
    None = 0,
    Color = 1 << 0,
    BackgroundColor = 1 << 1,
    Opacity = 1 << 2,
    Transform = 1 << 3
}

/// <summary>
/// Numeric representation of the paint/composite properties supported by CSS
/// animations. Keyframes are compiled into this form while parsing the sheet so
/// animation updates never tokenize CSS or allocate transform parser objects.
/// </summary>
internal readonly record struct UiKeyframeValues(
    UiKeyframeProperties Properties,
    Vector4 Color,
    Vector4 BackgroundColor,
    float Opacity,
    UiTransformDefinition Transform)
{
    public static UiKeyframeValues Compile(IReadOnlyDictionary<string, string> declarations)
    {
        var properties = UiKeyframeProperties.None;
        var color = default(Vector4);
        var backgroundColor = default(Vector4);
        var opacity = 1.0f;
        var transform = UiTransformDefinition.Identity;

        if (declarations.TryGetValue("color", out var colorSource) &&
            UiStyleResolver.TryColor(colorSource, out color))
            properties |= UiKeyframeProperties.Color;
        if (declarations.TryGetValue("background-color", out var backgroundSource) &&
            UiStyleResolver.TryColor(backgroundSource, out backgroundColor))
            properties |= UiKeyframeProperties.BackgroundColor;
        if (declarations.TryGetValue("opacity", out var opacitySource) &&
            float.TryParse(
                opacitySource,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsedOpacity))
        {
            opacity = Math.Clamp(parsedOpacity, 0.0f, 1.0f);
            properties |= UiKeyframeProperties.Opacity;
        }
        if (declarations.TryGetValue("transform", out var transformSource))
        {
            transform = UiTransformParser.Parse(transformSource, UiTransform.Identity.Origin);
            properties |= UiKeyframeProperties.Transform;
        }

        return new UiKeyframeValues(properties, color, backgroundColor, opacity, transform);
    }
}

[Flags]
internal enum UiAnimationChange
{
    None = 0,
    Paint = 1,
    Composite = 2
}

public readonly record struct UiAnimationEvent(
    string Name,
    float ElapsedTime,
    int Iteration);

public readonly record struct UiTransitionEvent(
    string Property,
    float ElapsedTime);

internal static class UiAnimationParser
{
    public static IReadOnlyList<UiTransitionDefinition> ParseTransitions(string source)
    {
        var result = new List<UiTransitionDefinition>();
        foreach (var item in UiStyleSheet.SplitTopLevel(source, ','))
        {
            var parts = item.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2 || !TryTime(parts[1], out var duration))
                continue;
            var easing = parts.Skip(2).FirstOrDefault(IsEasing) ?? "ease";
            var delay = parts.Skip(2).Where(part => !IsEasing(part))
                .Select(part => TryTime(part, out var value) ? value : float.NaN)
                .FirstOrDefault(float.IsFinite);
            result.Add(new UiTransitionDefinition(
                parts[0].ToLowerInvariant(),
                Math.Max(0.0f, duration),
                float.IsFinite(delay) ? delay : 0.0f,
                NormalizeEasing(easing)));
        }
        return result;
    }

    public static UiAnimationDefinition ParseAnimation(string source)
    {
        var parts = source.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || parts[0].Equals("none", StringComparison.OrdinalIgnoreCase))
            return UiAnimationDefinition.None;

        var name = parts.FirstOrDefault(part =>
            !TryTime(part, out _) &&
            !IsEasing(part) &&
            !IsIteration(part) &&
            part is not ("none" or "forwards" or "backwards" or "both")) ?? parts[0];
        var times = parts.Where(part => TryTime(part, out _)).ToArray();
        var duration = times.Length > 0 && TryTime(times[0], out var parsedDuration) ? parsedDuration : 0.0f;
        var delay = times.Length > 1 && TryTime(times[1], out var parsedDelay) ? parsedDelay : 0.0f;
        var easing = parts.FirstOrDefault(IsEasing) ?? "ease";
        var iterationToken = parts.FirstOrDefault(IsIteration);
        var iterations = iterationToken?.Equals("infinite", StringComparison.OrdinalIgnoreCase) == true
            ? float.PositiveInfinity
            : iterationToken is not null && TryFloat(iterationToken, out var count)
                ? Math.Max(0.0f, count)
                : 1.0f;
        var fill = parts.FirstOrDefault(part => part is "forwards" or "backwards" or "both") ?? "none";
        return new UiAnimationDefinition(
            name,
            Math.Max(0.0f, duration),
            delay,
            iterations,
            NormalizeEasing(easing),
            fill);
    }

    public static float Ease(string easing, float value)
    {
        value = Math.Clamp(value, 0.0f, 1.0f);
        return easing switch
        {
            "linear" => value,
            "ease-in" => value * value,
            "ease-out" => 1.0f - (1.0f - value) * (1.0f - value),
            "ease-in-out" => value < 0.5f
                ? 2.0f * value * value
                : 1.0f - MathF.Pow(-2.0f * value + 2.0f, 2.0f) * 0.5f,
            "step-start" => value > 0.0f ? 1.0f : 0.0f,
            "step-end" => value >= 1.0f ? 1.0f : 0.0f,
            _ => 1.0f - MathF.Pow(1.0f - value, 3.0f)
        };
    }

    private static bool IsEasing(string value) =>
        value is "linear" or "ease" or "ease-in" or "ease-out" or "ease-in-out" or "step-start" or "step-end";

    private static string NormalizeEasing(string easing) => easing.ToLowerInvariant();

    private static bool IsIteration(string value) =>
        value.Equals("infinite", StringComparison.OrdinalIgnoreCase) ||
        TryFloat(value, out _);

    internal static bool TryTime(string source, out float seconds)
    {
        source = source.Trim().ToLowerInvariant();
        var multiplier = 1.0f;
        if (source.EndsWith("ms")) { source = source[..^2]; multiplier = 0.001f; }
        else if (source.EndsWith('s')) source = source[..^1];
        else { seconds = 0.0f; return false; }
        if (TryFloat(source, out var value))
        {
            seconds = value * multiplier;
            return true;
        }
        seconds = 0.0f;
        return false;
    }

    private static bool TryFloat(string source, out float value) =>
        float.TryParse(source, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}

internal sealed class UiAnimationRuntime
{
    private static readonly string[] AnimatedProperties =
        ["color", "background-color", "opacity", "transform"];
    private readonly Dictionary<string, Transition> _transitions =
        new(StringComparer.OrdinalIgnoreCase);
    private Snapshot _target = new(Vector4.One, Vector4.Zero, 1.0f, UiTransform.Identity);
    private Snapshot _visual = new(Vector4.One, Vector4.Zero, 1.0f, UiTransform.Identity);
    private UiAnimationDefinition _animation = UiAnimationDefinition.None;
    private float _animationElapsed;
    private int _lastIteration = -1;
    private bool _animationRunning;
    private bool _restartRequested;
    private bool _wasVisible;
    private bool _initialized;

    public Vector4 Color => _visual.Color;
    public Vector4 BackgroundColor => _visual.BackgroundColor;
    public float Opacity => _visual.Opacity;
    public UiTransform Transform => _visual.Transform;
    public bool IsActive => _animationRunning || _transitions.Count > 0;

    public void Restart(UiElement element)
    {
        _restartRequested = true;
        StartAnimation(
            element,
            element.ComputedStyle.Animation,
            element.IsRendered);
    }

    public UiAnimationChange Update(
        UiElement element,
        IReadOnlyDictionary<string, UiKeyframes> keyframes,
        float deltaTime,
        float viewportWidth,
        float viewportHeight)
    {
        var previousVisual = _visual;
        var style = element.ComputedStyle;
        var transformWidth = element.Bounds.Width > 0.0f ? element.Bounds.Width : viewportWidth;
        var transformHeight = element.Bounds.Height > 0.0f ? element.Bounds.Height : viewportHeight;
        var target = Snapshot.FromStyle(
            style,
            transformWidth,
            transformHeight,
            viewportWidth,
            viewportHeight);
        var visible = element.IsRendered;
        var manual = element.Attributes.GetValueOrDefault("animation-trigger")
            ?.Equals("manual", StringComparison.OrdinalIgnoreCase) == true;
        if (!_initialized)
        {
            _initialized = true;
            _target = _visual = target;
            _wasVisible = visible;
            if (manual && !_restartRequested)
            {
                _animation = style.Animation;
                _animationRunning = false;
            }
            else
            {
                StartAnimation(element, style.Animation, visible);
            }
        }
        else
        {
            BeginTransitions(style.Transitions, _target, target);
            _target = target;
            if (!manual && (!SameAnimation(style.Animation, _animation) || visible && !_wasVisible))
                StartAnimation(element, style.Animation, visible);
            else if (manual && !SameAnimation(style.Animation, _animation))
            {
                _animation = style.Animation;
                _animationRunning = false;
            }
            _wasVisible = visible;
        }
        _restartRequested = false;

        UpdateTransitions(element, Math.Max(0.0f, deltaTime));
        ApplyAnimation(
            element,
            keyframes,
            Math.Max(0.0f, deltaTime),
            transformWidth,
            transformHeight,
            viewportWidth,
            viewportHeight);
        var changes = UiAnimationChange.None;
        if (previousVisual.Color != _visual.Color ||
            previousVisual.BackgroundColor != _visual.BackgroundColor)
            changes |= UiAnimationChange.Paint;
        if (previousVisual.Opacity != _visual.Opacity ||
            previousVisual.Transform != _visual.Transform)
            changes |= UiAnimationChange.Composite;
        return changes;
    }

    private void BeginTransitions(
        IReadOnlyList<UiTransitionDefinition> definitions,
        Snapshot previousTarget,
        Snapshot target)
    {
        foreach (var property in AnimatedProperties)
        {
            if (Snapshot.PropertyEquals(previousTarget, target, property))
                continue;
            var definition = default(UiTransitionDefinition);
            for (var index = definitions.Count - 1; index >= 0; index--)
            {
                var candidate = definitions[index];
                if (!candidate.Property.Equals(property, StringComparison.OrdinalIgnoreCase) &&
                    candidate.Property != "all")
                    continue;
                definition = candidate;
                break;
            }
            if (definition.Duration <= 0.0f)
            {
                _transitions.Remove(property);
                _visual = _visual.WithProperty(target, property);
                continue;
            }
            _transitions[property] = new Transition(
                property,
                _visual,
                target,
                -definition.Delay,
                definition.Duration,
                definition.Easing);
        }
    }

    private void UpdateTransitions(UiElement element, float deltaTime)
    {
        foreach (var property in AnimatedProperties)
        {
            if (!_transitions.TryGetValue(property, out var transition))
                continue;
            var updated = transition with { Elapsed = transition.Elapsed + deltaTime };
            _transitions[property] = updated;
            if (updated.Elapsed < 0.0f)
                continue;
            var amount = UiAnimationParser.Ease(
                updated.Easing,
                updated.Duration <= 0.0f ? 1.0f : updated.Elapsed / updated.Duration);
            _visual = _visual.InterpolateProperty(updated.From, updated.To, property, amount);
            if (updated.Elapsed < updated.Duration)
                continue;
            _visual = _visual.WithProperty(updated.To, property);
            _transitions.Remove(property);
            element.RaiseTransitionEnded(new UiTransitionEvent(property, updated.Duration));
        }

        foreach (var property in AnimatedProperties)
        {
            if (!_transitions.ContainsKey(property))
                _visual = _visual.WithProperty(_target, property);
        }
    }

    private void StartAnimation(UiElement element, UiAnimationDefinition definition, bool visible)
    {
        _animation = definition;
        _animationElapsed = -definition.Delay;
        _lastIteration = -1;
        _animationRunning = visible && definition.Name != "none" && definition.Duration > 0.0f;
        if (_animationRunning)
            element.RaiseAnimationStarted(new UiAnimationEvent(definition.Name, 0.0f, 0));
    }

    private static bool SameAnimation(
        in UiAnimationDefinition first,
        in UiAnimationDefinition second) =>
        first.Name == second.Name &&
        first.Duration == second.Duration &&
        first.Delay == second.Delay &&
        first.Iterations == second.Iterations &&
        first.Easing == second.Easing &&
        first.FillMode == second.FillMode;

    private void ApplyAnimation(
        UiElement element,
        IReadOnlyDictionary<string, UiKeyframes> keyframes,
        float deltaTime,
        float elementWidth,
        float elementHeight,
        float viewportWidth,
        float viewportHeight)
    {
        if (!_animationRunning || !keyframes.TryGetValue(_animation.Name, out var animation))
            return;
        _animationElapsed += deltaTime;
        if (_animationElapsed < 0.0f)
            return;

        var total = _animation.Duration * _animation.Iterations;
        var complete = float.IsFinite(total) && _animationElapsed >= total;
        var iteration = complete
            ? Math.Max(0, (int)MathF.Ceiling(_animation.Iterations) - 1)
            : (int)MathF.Floor(_animationElapsed / _animation.Duration);
        if (_lastIteration >= 0 && iteration != _lastIteration)
            element.RaiseAnimationIteration(new UiAnimationEvent(_animation.Name, _animationElapsed, iteration));
        _lastIteration = iteration;

        var progress = complete
            ? 1.0f
            : _animationElapsed / _animation.Duration - MathF.Floor(_animationElapsed / _animation.Duration);
        progress = UiAnimationParser.Ease(_animation.Easing, progress);
        _visual = Evaluate(
            animation,
            progress,
            _target,
            elementWidth,
            elementHeight,
            viewportWidth,
            viewportHeight);

        if (!complete)
            return;
        _animationRunning = false;
        if (_animation.FillMode is not ("forwards" or "both"))
            _visual = _target;
        element.RaiseAnimationEnded(new UiAnimationEvent(_animation.Name, total, iteration));
    }

    private static Snapshot Evaluate(
        UiKeyframes animation,
        float progress,
        Snapshot fallback,
        float elementWidth,
        float elementHeight,
        float viewportWidth,
        float viewportHeight)
    {
        var frames = animation.Frames;
        if (frames.Count == 0)
            return fallback;
        var before = frames[0];
        var after = frames[^1];
        for (var index = 0; index < frames.Count; index++)
        {
            var frame = frames[index];
            if (frame.Offset <= progress)
                before = frame;
            if (frame.Offset < progress)
                continue;
            after = frame;
            break;
        }
        var range = after.Offset - before.Offset;
        var amount = range <= float.Epsilon ? 0.0f : (progress - before.Offset) / range;
        var first = Snapshot.FromKeyframe(
            before.Values,
            fallback,
            elementWidth,
            elementHeight,
            viewportWidth,
            viewportHeight);
        var second = Snapshot.FromKeyframe(
            after.Values,
            fallback,
            elementWidth,
            elementHeight,
            viewportWidth,
            viewportHeight);
        return Snapshot.Lerp(first, second, amount);
    }

    private readonly record struct Transition(
        string Property,
        Snapshot From,
        Snapshot To,
        float Elapsed,
        float Duration,
        string Easing);

    private readonly record struct Snapshot(
        Vector4 Color,
        Vector4 BackgroundColor,
        float Opacity,
        UiTransform Transform)
    {
        public static Snapshot FromStyle(
            UiComputedStyle style,
            float elementWidth,
            float elementHeight,
            float viewportWidth,
            float viewportHeight) =>
            new(
                style.Color,
                style.BackgroundColor,
                style.Opacity,
                style.TransformDefinition.Resolve(
                    elementWidth,
                    elementHeight,
                    viewportWidth,
                    viewportHeight));

        public static Snapshot FromKeyframe(
            UiKeyframeValues values,
            Snapshot fallback,
            float elementWidth,
            float elementHeight,
            float viewportWidth,
            float viewportHeight)
        {
            var result = fallback;
            if ((values.Properties & UiKeyframeProperties.Color) != 0)
                result = result with { Color = values.Color };
            if ((values.Properties & UiKeyframeProperties.BackgroundColor) != 0)
                result = result with { BackgroundColor = values.BackgroundColor };
            if ((values.Properties & UiKeyframeProperties.Opacity) != 0)
                result = result with { Opacity = values.Opacity };
            if ((values.Properties & UiKeyframeProperties.Transform) != 0)
                result = result with
                {
                    Transform = (values.Transform with { Origin = fallback.Transform.Origin }).Resolve(
                        elementWidth,
                        elementHeight,
                        viewportWidth,
                        viewportHeight)
                };
            return result;
        }

        public static Snapshot Lerp(Snapshot first, Snapshot second, float amount) =>
            new(
                first.Color == second.Color
                    ? first.Color
                    : Vector4.Lerp(first.Color, second.Color, amount),
                first.BackgroundColor == second.BackgroundColor
                    ? first.BackgroundColor
                    : Vector4.Lerp(first.BackgroundColor, second.BackgroundColor, amount),
                first.Opacity == second.Opacity
                    ? first.Opacity
                    : float.Lerp(first.Opacity, second.Opacity, amount),
                first.Transform == second.Transform
                    ? first.Transform
                    : UiTransform.Lerp(first.Transform, second.Transform, amount));

        public static bool PropertyEquals(Snapshot first, Snapshot second, string property) =>
            property switch
            {
                "color" => first.Color == second.Color,
                "background-color" => first.BackgroundColor == second.BackgroundColor,
                "opacity" => first.Opacity == second.Opacity,
                "transform" => first.Transform == second.Transform,
                _ => true
            };

        public Snapshot WithProperty(Snapshot source, string property) =>
            property switch
            {
                "color" => this with { Color = source.Color },
                "background-color" => this with { BackgroundColor = source.BackgroundColor },
                "opacity" => this with { Opacity = source.Opacity },
                "transform" => this with { Transform = source.Transform },
                _ => this
            };

        public Snapshot InterpolateProperty(Snapshot from, Snapshot to, string property, float amount) =>
            property switch
            {
                "color" => this with { Color = Vector4.Lerp(from.Color, to.Color, amount) },
                "background-color" => this with { BackgroundColor = Vector4.Lerp(from.BackgroundColor, to.BackgroundColor, amount) },
                "opacity" => this with { Opacity = float.Lerp(from.Opacity, to.Opacity, amount) },
                "transform" => this with { Transform = UiTransform.Lerp(from.Transform, to.Transform, amount) },
                _ => this
            };
    }
}
