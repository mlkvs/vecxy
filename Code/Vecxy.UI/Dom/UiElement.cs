using System.Numerics;
using Facebook.Yoga;
using Vecxy.Kernel;
using static Facebook.Yoga.YGNodeAPI;

namespace Vecxy.UI;

public sealed class UiElement
{
    private readonly List<UiElement> _children = [];
    private readonly Dictionary<string, string> _attributes;
    private string _text = string.Empty;

    internal Node YogaNode { get; }
    internal Vector2 IntrinsicSize { get; set; }
    internal UiFontAsset? Font { get; set; }
    internal bool IsHovered { get; set; }
    internal bool IsActive { get; set; }
    internal bool IsFocused { get; set; }
    internal bool IsFocusVisible { get; set; }
    internal bool IsDragging { get; set; }
    internal bool IsDropTarget { get; set; }
    internal UiComputedStyle ComputedStyle { get; set; } = new();
    internal UiAnimationRuntime AnimationRuntime { get; } = new();
    internal Vector4 RenderColor => AnimationRuntime.Color;
    internal Vector4 RenderBackgroundColor => AnimationRuntime.BackgroundColor;
    internal float RenderOpacity => AnimationRuntime.Opacity;
    internal UiTransform RenderTransform => AnimationRuntime.Transform;

    public string TagName { get; }
    public string? Id { get; }
    public IReadOnlySet<string> Classes { get; }
    public IReadOnlyDictionary<string, string> Attributes => _attributes;
    public UiElement? Parent { get; private set; }
    public IReadOnlyList<UiElement> Children => _children;
    public Rect Bounds { get; internal set; }
    public Vector2 ScrollOffset { get; private set; }
    public Vector2 ScrollExtent { get; private set; }
    public bool CanScrollHorizontally =>
        ComputedStyle.OverflowX is "auto" or "scroll" &&
        ScrollExtent.X > Bounds.Width + 0.01f;
    public bool CanScrollVertically =>
        ComputedStyle.OverflowY is "auto" or "scroll" &&
        ScrollExtent.Y > Bounds.Height + 0.01f;
    public string Text
    {
        get => _text;
        set
        {
            _text = value ?? string.Empty;
            if (YGNodeHasMeasureFunc(YogaNode))
                YGNodeMarkDirty(YogaNode);
        }
    }

    public bool IsDisabled =>
        HasBooleanAttribute("disabled") ||
        Attributes.TryGetValue("aria-disabled", out var value) &&
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    public bool IsChecked
    {
        get => HasBooleanAttribute("checked");
        set => SetBooleanAttribute("checked", value);
    }

    public bool IsSelected
    {
        get => HasBooleanAttribute("selected");
        set => SetBooleanAttribute("selected", value);
    }

    public bool IsDraggable => HasBooleanAttribute("draggable");
    public bool AcceptsDrop => HasBooleanAttribute("drop-target");

    public event Action<UiElement>? Clicked;
    public event Action<UiElement>? Focused;
    public event Action<UiElement>? Blurred;
    public event Action<UiElement>? Scrolled;
    public event Action<UiElement>? DragStarted;
    public event Action<UiElement>? DragEnded;
    public event Action<UiElement, UiDragEvent>? Dropped;
    public event Action<UiElement, UiAnimationEvent>? AnimationStarted;
    public event Action<UiElement, UiAnimationEvent>? AnimationIteration;
    public event Action<UiElement, UiAnimationEvent>? AnimationEnded;
    public event Action<UiElement, UiTransitionEvent>? TransitionEnded;

    public void SetAttribute(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        _attributes[name] = value;
        if (YGNodeHasMeasureFunc(YogaNode))
            YGNodeMarkDirty(YogaNode);
    }

    public bool RemoveAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var removed = _attributes.Remove(name);
        if (removed && YGNodeHasMeasureFunc(YogaNode))
            YGNodeMarkDirty(YogaNode);
        return removed;
    }

    public bool RemoveFromParent()
    {
        if (Parent is not { } parent)
            return false;

        YGNodeRemoveChild(parent.YogaNode, YogaNode);
        parent._children.Remove(this);
        Parent = null;
        YGNodeFreeRecursive(YogaNode);
        return true;
    }

    internal UiElement(
        Config config,
        string tagName,
        IReadOnlyDictionary<string, string> attributes,
        string? text = null)
    {
        TagName = tagName.ToLowerInvariant();
        _attributes = new Dictionary<string, string>(
            attributes,
            StringComparer.OrdinalIgnoreCase);
        Id = _attributes.GetValueOrDefault("id");
        Classes = new HashSet<string>(
            _attributes.GetValueOrDefault("class")?
                .Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [],
            StringComparer.Ordinal);
        _text = text ?? string.Empty;
        YogaNode = YGNodeNewWithConfig(config);
        YGNodeSetContext(YogaNode, this);
    }

    internal void Add(UiElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (child.Parent is not null)
            throw new InvalidOperationException("UI element already has a parent.");

        child.Parent = this;
        _children.Add(child);
        YGNodeInsertChild(YogaNode, child.YogaNode, (nuint)(_children.Count - 1));
    }

    internal IEnumerable<UiElement> DescendantsAndSelf()
    {
        yield return this;
        foreach (var child in _children)
        foreach (var descendant in child.DescendantsAndSelf())
            yield return descendant;
    }

    internal UiElement? HitTest(Vector2 point) =>
        HitTest(point, Vector2.Zero, Matrix3x2.Identity);

    private UiElement? HitTest(
        Vector2 point,
        Vector2 translation,
        Matrix3x2 parentTransform)
    {
        var visualBounds = new Rect(
            Bounds.X + translation.X,
            Bounds.Y + translation.Y,
            Bounds.Width,
            Bounds.Height);
        if (ComputedStyle.Display == "none" ||
            ComputedStyle.Visibility == "hidden")
            return null;

        var transform = RenderTransform.ToMatrix(visualBounds) * parentTransform;
        var localPoint = Matrix3x2.Invert(transform, out var inverse)
            ? Vector2.Transform(point, inverse)
            : point;
        var insideX = localPoint.X >= visualBounds.Left && localPoint.X <= visualBounds.Right;
        var insideY = localPoint.Y >= visualBounds.Top && localPoint.Y <= visualBounds.Bottom;
        var inside = insideX && insideY;
        var clipsX = ComputedStyle.OverflowX is "hidden" or "scroll" or "auto";
        var clipsY = ComputedStyle.OverflowY is "hidden" or "scroll" or "auto";
        if ((!insideX && clipsX) || (!insideY && clipsY))
            return null;

        var childTranslation = translation - ScrollOffset;

        foreach (var child in _children
                     .Select((value, index) => (value, index))
                     .OrderByDescending(item => item.value.ComputedStyle.ZIndex)
                     .ThenByDescending(item => item.index)
                     .Select(item => item.value))
        {
            var hit = child.HitTest(point, childTranslation, transform);
            if (hit is not null)
                return hit;
        }

        return inside && ComputedStyle.PointerEvents != "none" && IsInteractive
            ? this
            : null;
    }

    internal bool IsInteractive =>
        !IsDisabled &&
        (TagName is "button" or "input" or "select" or "slider" ||
         Clicked is not null ||
         CanScrollHorizontally || CanScrollVertically ||
         Attributes.ContainsKey("action"));

    internal bool IsFocusable =>
        !IsDisabled &&
        (!Attributes.TryGetValue("tabindex", out var tabIndex) || tabIndex != "-1") &&
        (TagName is "button" or "input" or "select" or "slider" ||
         Clicked is not null || Attributes.ContainsKey("action") || Attributes.ContainsKey("tabindex"));

    internal void RaiseClicked()
    {
        if (TagName == "input" &&
            Attributes.GetValueOrDefault("type")?.Equals("checkbox", StringComparison.OrdinalIgnoreCase) == true)
            IsChecked = !IsChecked;
        Clicked?.Invoke(this);
    }

    public void ScrollTo(Vector2 offset)
    {
        var maximum = new Vector2(
            CanScrollHorizontally ? Math.Max(0.0f, ScrollExtent.X - Bounds.Width) : 0.0f,
            CanScrollVertically ? Math.Max(0.0f, ScrollExtent.Y - Bounds.Height) : 0.0f);
        var replacement = Vector2.Clamp(offset, Vector2.Zero, maximum);
        if (replacement == ScrollOffset)
            return;
        ScrollOffset = replacement;
        Scrolled?.Invoke(this);
    }

    public void ScrollBy(Vector2 delta) => ScrollTo(ScrollOffset + delta);

    internal void UpdateScrollExtent(Vector2 extent)
    {
        ScrollExtent = Vector2.Max(extent, new Vector2(Bounds.Width, Bounds.Height));
        ScrollTo(ScrollOffset);
    }

    internal void RaiseFocused() => Focused?.Invoke(this);
    internal void RaiseBlurred() => Blurred?.Invoke(this);
    internal void RaiseDragStarted() => DragStarted?.Invoke(this);
    internal void RaiseDragEnded() => DragEnded?.Invoke(this);
    internal void RaiseDropped(UiElement source) =>
        Dropped?.Invoke(this, new UiDragEvent(source, this));
    internal void RaiseAnimationStarted(UiAnimationEvent eventData) =>
        AnimationStarted?.Invoke(this, eventData);
    internal void RaiseAnimationIteration(UiAnimationEvent eventData) =>
        AnimationIteration?.Invoke(this, eventData);
    internal void RaiseAnimationEnded(UiAnimationEvent eventData) =>
        AnimationEnded?.Invoke(this, eventData);
    internal void RaiseTransitionEnded(UiTransitionEvent eventData) =>
        TransitionEnded?.Invoke(this, eventData);

    private bool HasBooleanAttribute(string name) =>
        Attributes.TryGetValue(name, out var value) &&
        !value.Equals("false", StringComparison.OrdinalIgnoreCase);

    private void SetBooleanAttribute(string name, bool value)
    {
        if (value)
            SetAttribute(name, "true");
        else
            RemoveAttribute(name);
    }

    internal void ReleaseLayout()
    {
        YGNodeFreeRecursive(YogaNode);
        _children.Clear();
        Parent = null;
    }
}

public readonly record struct UiDragEvent(UiElement Source, UiElement Target);
