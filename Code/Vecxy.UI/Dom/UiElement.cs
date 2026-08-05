using System.Numerics;
using Facebook.Yoga;
using Vecxy.Kernel;
using static Facebook.Yoga.YGNodeAPI;

namespace Vecxy.UI;

public class UiElement
{
    internal Node YogaNode { get; }
    internal Vector2 IntrinsicSize
    {
        get => _intrinsicSize;
        set
        {
            if (_intrinsicSize == value)
                return;
            _intrinsicSize = value;
            InvalidateLayout();
        }
    }
    internal UiFontAsset? Font { get; set; }
    internal bool IsHovered { get => _isHovered; set => SetPseudoState(ref _isHovered, value); }
    internal bool IsActive { get => _isActive; set => SetPseudoState(ref _isActive, value); }
    internal bool IsFocused { get => _isFocused; set => SetPseudoState(ref _isFocused, value); }
    internal bool IsFocusVisible { get => _isFocusVisible; set => SetPseudoState(ref _isFocusVisible, value); }
    internal bool IsDragging { get => _isDragging; set => SetPseudoState(ref _isDragging, value); }
    internal bool IsDropTarget { get => _isDropTarget; set => SetPseudoState(ref _isDropTarget, value); }
    internal UiComputedStyle ComputedStyle { get; set; } = new();
    internal UiAnimationRuntime AnimationRuntime { get; } = new();
    internal UiTextLayoutCache TextLayoutCache { get; } = new();
    internal int StyleVersion => _styleVersion;
    internal int PseudoVersion => _pseudoVersion;
    internal int LayoutVersion => _layoutVersion;
    internal int VisualVersion => _visualVersion;
    internal int ScrollVersion => _scrollVersion;
    internal Vector4 RenderColor => AnimationRuntime.Color;
    internal Vector4 RenderBackgroundColor => AnimationRuntime.BackgroundColor;
    internal float RenderOpacity => AnimationRuntime.Opacity;
    internal UiTransform RenderTransform => AnimationRuntime.Transform;
    internal bool IsHitTestInteractive => IsInteractive;
    internal bool UsesVirtualization =>
        Attributes.TryGetValue("virtualize", out var virtualize) &&
        !virtualize.Equals("false", StringComparison.OrdinalIgnoreCase);

    public string TagName { get; }
    public string? Id => _attributes.GetValueOrDefault("id");
    public IReadOnlySet<string> Classes => _classes;
    public IReadOnlyDictionary<string, string> Attributes => _attributes;
    public UiInlineStyle Style { get; }
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
            value ??= string.Empty;
            if (_text == value)
                return;
            _text = value;
            // Text cannot change a box whose width and height are explicitly
            // constrained. Avoid making Yoga recalculate the entire document for
            // counters, timers and other frequently updated labels.
            var hasFixedBox = ComputedStyle.Width.Unit != EUiLengthUnit.Auto &&
                              ComputedStyle.Height.Unit != EUiLengthUnit.Auto;
            if (!hasFixedBox)
            {
                if (YGNodeHasMeasureFunc(YogaNode))
                    YGNodeMarkDirty(YogaNode);
                InvalidateLayout();
            }
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Text content of this element. Containers update their first nested text element,
    /// matching the way a button label is addressed in web UI toolkits.
    /// </summary>
    public string TextContent
    {
        get => TagName == "text" ? Text : Query<UiText>("text")?.Text ?? Text;
        set
        {
            if (TagName == "text")
                Text = value;
            else if (Query<UiText>("text") is { } text)
                text.Text = value;
            else
                Text = value;
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

    /// <summary>
    /// Runtime progress in the 0..1 range used by progress-rendering elements.
    /// Updating it does not invalidate layout or resolved styles.
    /// </summary>
    public float Progress
    {
        get => _progress;
        set
        {
            var replacement = Math.Clamp(value, 0.0f, 1.0f);
            if (Math.Abs(_progress - replacement) <= float.Epsilon)
                return;
            _progress = replacement;
            InvalidateVisual();
        }
    }

    public bool IsSelected
    {
        get => HasBooleanAttribute("selected");
        set => SetBooleanAttribute("selected", value);
    }

    public bool IsDraggable => HasBooleanAttribute("draggable");
    public bool AcceptsDrop => HasBooleanAttribute("drop-target");

    public bool IsVisible
    {
        get => !HasBooleanAttribute("hidden");
        set => SetBooleanAttribute("hidden", !value);
    }

    public bool IsEnabled
    {
        get => !IsDisabled;
        set => SetBooleanAttribute("disabled", !value);
    }

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
    public event Action<UiElement, UiTouchEvent>? TouchStarted;
    public event Action<UiElement, UiTouchEvent>? TouchMoved;
    public event Action<UiElement, UiTouchEvent>? TouchEnded;
    public event Action<UiElement, UiTouchEvent>? TouchCancelled;
    
    private readonly List<UiElement> _children = [];
    private readonly Dictionary<string, string> _attributes;
    private readonly HashSet<string> _classes;
    private readonly Dictionary<string, string> _inlineStyles;
    private UiElement[] _paintOrder = [];
    private UiElement[] _hitTestOrder = [];
    private int _childOrderSignature = int.MinValue;
    private int _childrenRevision;
    private string _text = string.Empty;
    private float _progress;
    private Vector2 _intrinsicSize;
    private int _styleVersion;
    private int _pseudoVersion;
    private int _layoutVersion;
    private int _visualVersion;
    private int _scrollVersion;
    private bool _isHovered;
    private bool _isActive;
    private bool _isFocused;
    private bool _isFocusVisible;
    private bool _isDragging;
    private bool _isDropTarget;

    public void SetAttribute(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        if (_attributes.TryGetValue(name, out var current) && current == value)
            return;
        _attributes[name] = value;
        if (name.Equals("style", StringComparison.OrdinalIgnoreCase))
            ReplaceInlineStyles(value);
        InvalidateStyle();
        InvalidateLayout();
        InvalidateVisual();
        if (name.Equals("class", StringComparison.OrdinalIgnoreCase))
            ReplaceClasses(value);
        if (YGNodeHasMeasureFunc(YogaNode))
            YGNodeMarkDirty(YogaNode);
    }

    public bool RemoveAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var removed = _attributes.Remove(name);
        if (removed)
            InvalidateStyle();
        if (removed)
            InvalidateLayout();
        if (removed)
            InvalidateVisual();
        if (removed && name.Equals("class", StringComparison.OrdinalIgnoreCase))
            _classes.Clear();
        if (removed && name.Equals("style", StringComparison.OrdinalIgnoreCase))
            _inlineStyles.Clear();
        if (removed && YGNodeHasMeasureFunc(YogaNode))
            YGNodeMarkDirty(YogaNode);
        return removed;
    }

    public void AddClass(string className)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(className);
        if (_classes.Add(className))
        {
            SynchronizeClassAttribute();
            InvalidateStyle();
            InvalidateLayout();
            InvalidateVisual();
        }
    }

    public void RemoveClass(string className)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(className);
        if (_classes.Remove(className))
        {
            SynchronizeClassAttribute();
            InvalidateStyle();
            InvalidateLayout();
            InvalidateVisual();
        }
    }

    public void ToggleClass(string className, bool enabled)
    {
        if (enabled)
            AddClass(className);
        else
            RemoveClass(className);
    }

    public void SetStyle(string propertyName, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(value);
        if (_inlineStyles.TryGetValue(propertyName, out var current) && current == value)
            return;
        _inlineStyles[propertyName] = value;
        SynchronizeInlineStyleAttribute();
        InvalidateStyle();
        InvalidateLayout();
        InvalidateVisual();
    }

    public bool RemoveStyle(string propertyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        if (!_inlineStyles.Remove(propertyName))
            return false;
        SynchronizeInlineStyleAttribute();
        InvalidateStyle();
        InvalidateLayout();
        InvalidateVisual();
        return true;
    }

    public bool RemoveFromParent()
    {
        if (Parent is not { } parent)
            return false;

        YGNodeRemoveChild(parent.YogaNode, YogaNode);
        parent._children.Remove(this);
        unchecked { parent._childrenRevision++; }
        parent.InvalidateStyle();
        parent.InvalidateLayout();
        parent.InvalidateVisual();
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
        _classes = new HashSet<string>(
            _attributes.GetValueOrDefault("class")?
                .Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [],
            StringComparer.Ordinal);
        _inlineStyles = new Dictionary<string, string>(
            UiStyleSheet.ParseDeclarations(_attributes.GetValueOrDefault("style") ?? string.Empty),
            StringComparer.OrdinalIgnoreCase);
        _text = text ?? string.Empty;
        YogaNode = YGNodeNewWithConfig(config);
        YGNodeSetContext(YogaNode, this);
        Style = new UiInlineStyle(this);
    }

    public void Add(UiElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (child.Parent is not null)
            throw new InvalidOperationException("UI element already has a parent.");

        child.Parent = this;
        _children.Add(child);
        unchecked { _childrenRevision++; }
        YGNodeInsertChild(YogaNode, child.YogaNode, (nuint)(_children.Count - 1));
        InvalidateStyle();
        InvalidateLayout();
        InvalidateVisual();
    }

    public void Clear()
    {
        foreach (var child in _children.ToArray())
            child.RemoveFromParent();
    }

    public UiElement? Query(string selector) =>
        QueryAll(selector).FirstOrDefault();

    public T? Query<T>(string selector) where T : UiElement =>
        QueryAll(selector).OfType<T>().FirstOrDefault();

    public IReadOnlyList<UiElement> QueryAll(string selector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);
        return DescendantsAndSelf().Where(element =>
        {
            if (selector[0] == '#')
                return element.Id == selector[1..];
            if (selector[0] == '.')
                return element.Classes.Contains(selector[1..]);
            return string.Equals(element.TagName, selector, StringComparison.OrdinalIgnoreCase);
        }).ToArray();
    }

    public IReadOnlyList<T> QueryAll<T>(string selector) where T : UiElement =>
        QueryAll(selector).OfType<T>().ToArray();

    internal IEnumerable<UiElement> DescendantsAndSelf()
    {
        yield return this;
        foreach (var child in _children)
        foreach (var descendant in child.DescendantsAndSelf())
            yield return descendant;
    }

    internal IEnumerable<UiElement> VisibleDescendantsAndSelf()
    {
        if (ComputedStyle.Display == "none" || ComputedStyle.Visibility == "hidden")
            yield break;
        yield return this;
        foreach (var child in _children)
        foreach (var descendant in child.VisibleDescendantsAndSelf())
            yield return descendant;
    }

    internal IReadOnlyList<UiElement> ChildrenInPaintOrder(bool descending = false)
    {
        var signature = _childrenRevision;
        foreach (var child in _children)
            signature = HashCode.Combine(signature, child.ComputedStyle.ZIndex);

        if (signature != _childOrderSignature)
        {
            _paintOrder = _children
                .Select((element, index) => (element, index))
                .OrderBy(item => item.element.ComputedStyle.ZIndex)
                .ThenBy(item => item.index)
                .Select(item => item.element)
                .ToArray();
            _hitTestOrder = _paintOrder.Reverse().ToArray();
            _childOrderSignature = signature;
        }

        return descending ? _hitTestOrder : _paintOrder;
    }

    private void InvalidateLayout()
    {
        unchecked { _layoutVersion++; }
        Parent?.InvalidateLayout();
    }

    internal void InvalidateVisual()
    {
        unchecked { _visualVersion++; }
        Parent?.InvalidateVisual();
    }

    private void InvalidateScroll()
    {
        unchecked { _scrollVersion++; }
        Parent?.InvalidateScroll();
    }

    private void InvalidateStyle()
    {
        unchecked { _styleVersion++; }
        Parent?.InvalidateStyle();
    }

    private void InvalidatePseudoState()
    {
        unchecked { _pseudoVersion++; }
        Parent?.InvalidatePseudoState();
    }

    private void SetPseudoState(ref bool field, bool value)
    {
        if (field == value)
            return;
        field = value;
        InvalidatePseudoState();
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
        if ((clipsX || clipsY) && inside && ComputedStyle.BorderRadius > 0.0f &&
            !ContainsRounded(visualBounds, ComputedStyle.BorderRadius, localPoint))
            return null;

        var childTranslation = translation - ScrollOffset;

        foreach (var child in ChildrenInPaintOrder(descending: true))
        {
            var hit = child.HitTest(point, childTranslation, transform);
            if (hit is not null)
                return hit;
        }

        return inside && ComputedStyle.PointerEvents != "none" && IsInteractive
            ? this
            : null;
    }

    private static bool ContainsRounded(Rect bounds, float radius, Vector2 point)
    {
        radius = Math.Clamp(radius, 0.0f, Math.Min(bounds.Width, bounds.Height) * 0.5f);
        if (radius <= 0.0f)
            return true;
        var nearest = Vector2.Clamp(
            point,
            new Vector2(bounds.Left + radius, bounds.Top + radius),
            new Vector2(bounds.Right - radius, bounds.Bottom - radius));
        return Vector2.DistanceSquared(point, nearest) <= radius * radius;
    }

    internal bool IsInteractive =>
        !IsDisabled &&
        IsVisible &&
        (TagName is "button" or "input" or "select" or "slider" ||
         Clicked is not null ||
         TouchStarted is not null || TouchMoved is not null ||
         TouchEnded is not null || TouchCancelled is not null ||
         CanScrollHorizontally || CanScrollVertically ||
         Attributes.ContainsKey("action"));

    internal bool IsFocusable =>
        !IsDisabled &&
        IsVisible &&
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
        var virtualWindowChanged = UsesVirtualization &&
                                   VirtualScrollWindow(ScrollOffset) != VirtualScrollWindow(replacement);
        ScrollOffset = replacement;
        if (virtualWindowChanged)
        {
            // Recalculate only when entering another overscan window. Movement
            // inside the window remains a shader-only translation.
            InvalidateLayout();
            InvalidateVisual();
        }
        InvalidateScroll();
        Scrolled?.Invoke(this);
    }

    public void ScrollBy(Vector2 delta) => ScrollTo(ScrollOffset + delta);

    private (int X, int Y) VirtualScrollWindow(Vector2 offset) =>
        (
            (int)MathF.Floor(offset.X / Math.Max(1.0f, Bounds.Width * 0.5f)),
            (int)MathF.Floor(offset.Y / Math.Max(1.0f, Bounds.Height * 0.5f))
        );

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
    internal void RaiseTouchStarted(UiTouchEvent eventData) =>
        TouchStarted?.Invoke(this, eventData);
    internal void RaiseTouchMoved(UiTouchEvent eventData) =>
        TouchMoved?.Invoke(this, eventData);
    internal void RaiseTouchEnded(UiTouchEvent eventData) =>
        TouchEnded?.Invoke(this, eventData);
    internal void RaiseTouchCancelled(UiTouchEvent eventData) =>
        TouchCancelled?.Invoke(this, eventData);

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

    private void ReplaceClasses(string value)
    {
        _classes.Clear();
        foreach (var className in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            _classes.Add(className);
    }

    private void SynchronizeClassAttribute()
    {
        if (_classes.Count == 0)
            _attributes.Remove("class");
        else
            _attributes["class"] = string.Join(' ', _classes);
    }

    private void ReplaceInlineStyles(string value)
    {
        _inlineStyles.Clear();
        foreach (var (name, declaration) in UiStyleSheet.ParseDeclarations(value))
            _inlineStyles[name] = declaration;
    }

    private void SynchronizeInlineStyleAttribute()
    {
        if (_inlineStyles.Count == 0)
            _attributes.Remove("style");
        else
            _attributes["style"] = string.Join("; ", _inlineStyles.Select(pair => $"{pair.Key}: {pair.Value}"));
    }

    internal string? GetInlineStyle(string propertyName) =>
        _inlineStyles.GetValueOrDefault(propertyName);

    internal void ReleaseLayout()
    {
        YGNodeFreeRecursive(YogaNode);
        _children.Clear();
        Parent = null;
    }
}

public readonly record struct UiDragEvent(UiElement Source, UiElement Target);

public readonly record struct UiTouchEvent(
    int Id,
    Vector2 Position,
    Vector2 Delta,
    float Pressure,
    bool IsPrimary);
