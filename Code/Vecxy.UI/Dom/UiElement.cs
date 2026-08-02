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
    internal UiComputedStyle ComputedStyle { get; set; } = new();

    public string TagName { get; }
    public string? Id { get; }
    public IReadOnlySet<string> Classes { get; }
    public IReadOnlyDictionary<string, string> Attributes => _attributes;
    public UiElement? Parent { get; private set; }
    public IReadOnlyList<UiElement> Children => _children;
    public Rect Bounds { get; internal set; }
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
        Attributes.ContainsKey("disabled") ||
        Attributes.TryGetValue("aria-disabled", out var value) &&
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    public event Action<UiElement>? Clicked;

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

    internal UiElement? HitTest(Vector2 point)
    {
        if (ComputedStyle.Display == "none" ||
            ComputedStyle.Visibility == "hidden" ||
            !Bounds.Contains(point))
            return null;

        foreach (var child in _children
                     .Select((value, index) => (value, index))
                     .OrderByDescending(item => item.value.ComputedStyle.ZIndex)
                     .ThenByDescending(item => item.index)
                     .Select(item => item.value))
        {
            var hit = child.HitTest(point);
            if (hit is not null)
                return hit;
        }

        return ComputedStyle.PointerEvents != "none" && IsInteractive
            ? this
            : null;
    }

    internal bool IsInteractive =>
        !IsDisabled &&
        (TagName is "button" or "input" or "select" or "slider" ||
         Clicked is not null ||
         Attributes.ContainsKey("action"));

    internal void RaiseClicked() => Clicked?.Invoke(this);

    internal void ReleaseLayout()
    {
        YGNodeFreeRecursive(YogaNode);
        _children.Clear();
        Parent = null;
    }
}
