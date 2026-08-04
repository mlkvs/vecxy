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
    IReadOnlyDictionary<string, string> Declarations);

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
                easing));
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
        return new UiAnimationDefinition(name, Math.Max(0.0f, duration), delay, iterations, easing, fill);
    }

    public static float Ease(string easing, float value)
    {
        value = Math.Clamp(value, 0.0f, 1.0f);
        return easing.ToLowerInvariant() switch
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
            element.IsVisible && element.ComputedStyle.Visibility != "hidden");
    }

    public bool Update(
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
        var visible = style.Display != "none" && style.Visibility != "hidden";
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
            if (!manual && (style.Animation != _animation || visible && !_wasVisible))
                StartAnimation(element, style.Animation, visible);
            else if (manual && style.Animation != _animation)
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
        return previousVisual != _visual;
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
            var definition = definitions.LastOrDefault(item =>
                item.Property.Equals(property, StringComparison.OrdinalIgnoreCase) || item.Property == "all");
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
        var before = frames.LastOrDefault(frame => frame.Offset <= progress) ?? frames[0];
        var after = frames.FirstOrDefault(frame => frame.Offset >= progress) ?? frames[^1];
        var range = after.Offset - before.Offset;
        var amount = range <= float.Epsilon ? 0.0f : (progress - before.Offset) / range;
        var first = Snapshot.FromDeclarations(
            before.Declarations,
            fallback,
            elementWidth,
            elementHeight,
            viewportWidth,
            viewportHeight);
        var second = Snapshot.FromDeclarations(
            after.Declarations,
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

        public static Snapshot FromDeclarations(
            IReadOnlyDictionary<string, string> declarations,
            Snapshot fallback,
            float elementWidth,
            float elementHeight,
            float viewportWidth,
            float viewportHeight)
        {
            var result = fallback;
            if (declarations.TryGetValue("color", out var color) && UiStyleResolver.TryColor(color, out var parsedColor))
                result = result with { Color = parsedColor };
            if (declarations.TryGetValue("background-color", out var background) && UiStyleResolver.TryColor(background, out var parsedBackground))
                result = result with { BackgroundColor = parsedBackground };
            if (declarations.TryGetValue("opacity", out var opacity) &&
                float.TryParse(opacity, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedOpacity))
                result = result with { Opacity = Math.Clamp(parsedOpacity, 0.0f, 1.0f) };
            if (declarations.TryGetValue("transform", out var transform))
                result = result with
                {
                    Transform = UiTransformParser.Parse(transform, fallback.Transform.Origin).Resolve(
                        elementWidth,
                        elementHeight,
                        viewportWidth,
                        viewportHeight)
                };
            return result;
        }

        public static Snapshot Lerp(Snapshot first, Snapshot second, float amount) =>
            new(
                Vector4.Lerp(first.Color, second.Color, amount),
                Vector4.Lerp(first.BackgroundColor, second.BackgroundColor, amount),
                float.Lerp(first.Opacity, second.Opacity, amount),
                UiTransform.Lerp(first.Transform, second.Transform, amount));

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
