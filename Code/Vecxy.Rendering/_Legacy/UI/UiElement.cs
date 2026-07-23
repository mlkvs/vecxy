using System.Numerics;

namespace Vecxy.UI;

[Flags]
public enum UiDirtyFlags { None = 0, Style = 1, Layout = 2, Visual = 4, Text = 8, Hierarchy = 16 }

public sealed class UiElement
{
    private string _text = string.Empty;
    public string Type { get; internal set; } = "Element";
    public string? Id { get; internal set; }
    public HashSet<string> Classes { get; } = [];
    public UiElement? Parent { get; private set; }
    public List<UiElement> Children { get; } = [];
    public UiStyle Style { get; internal set; } = new();
    public Vector4 Layout { get; internal set; }
    public UiDirtyFlags DirtyFlags { get; private set; } = (UiDirtyFlags)31;
    public float Value { get; set; }
    public bool IsHovered { get; internal set; }
    public bool IsPressed { get; internal set; }
    public bool IsFocused { get; internal set; }
    public bool IsEnabled { get; set; } = true;
    public bool IsHitTestVisible { get; internal set; } = true;
    public bool Draggable { get; internal set; }
    public bool DropTarget { get; internal set; }
    public bool DragVisual { get; internal set; } = true;
    public IReadOnlyList<string> Options { get; internal set; } = [];
    public string IconName { get; internal set; } = string.Empty;
    public float ScrollY { get; internal set; }
    internal Vector2 VisualOffset { get; set; }
    internal bool IsVirtualVisible { get; set; } = true;
    internal float VirtualContentHeight { get; set; }
    internal float[]? VirtualOffsets { get; set; }
    internal float[]? VirtualHeights { get; set; }
    internal int VirtualStart { get; set; }
    internal int VirtualEnd { get; set; }
    public int CaretIndex { get; internal set; }
    public int SelectionAnchor { get; internal set; }
    internal bool CaretVisible { get; set; }
    internal float TextScrollX { get; set; }
    public int SelectionStart => Math.Min(CaretIndex, SelectionAnchor);
    public int SelectionLength => Math.Abs(CaretIndex - SelectionAnchor);
    public event Action<UiElement>? Clicked;
    public event Action<UiElement, float>? ValueChanged;
    public event Action<UiElement, UiElement>? Dropped;
    public event Action<UiElement, Vector2>? Dragged;
    public string Text
    {
        get => _text;
        set { if (_text == value) return; _text = value; CaretIndex = Math.Min(CaretIndex, _text.Length); SelectionAnchor = Math.Min(SelectionAnchor, _text.Length); MarkDirty(UiDirtyFlags.Text | UiDirtyFlags.Layout); }
    }

    internal void Add(UiElement child) { child.Parent = this; Children.Add(child); MarkDirty(UiDirtyFlags.Hierarchy); }
    internal void MarkDirty(UiDirtyFlags flags) { DirtyFlags |= flags; Parent?.MarkDirty(flags); }
    internal void ClearDirty() { DirtyFlags = 0; foreach (var child in Children) child.ClearDirty(); }
    public UiElement? Find(string id) => Id == id ? this : Children.Select(x => x.Find(id)).FirstOrDefault(x => x is not null);
    internal void RaiseClick() => Clicked?.Invoke(this);
    internal void SetValue(float value)
    {
        value = Math.Clamp(value, 0, 1);
        if (MathF.Abs(Value - value) < .0001f) return;
        Value = value;
        ValueChanged?.Invoke(this, value);
    }
    internal void RaiseDrop(UiElement source) => Dropped?.Invoke(this, source);
    internal void RaiseDragged(Vector2 delta) => Dragged?.Invoke(this, delta);
    public void ModifyStyle(Action<UiStyle> change) { change(Style); MarkDirty(UiDirtyFlags.Layout | UiDirtyFlags.Visual); }
    internal void AcceptDrop(UiElement source)
    {
        if (Parent is not null && ReferenceEquals(Parent, source.Parent))
        {
            var sourceIndex = Parent.Children.IndexOf(source);
            var targetIndex = Parent.Children.IndexOf(this);
            if (sourceIndex >= 0 && targetIndex >= 0)
            {
                (Parent.Children[sourceIndex], Parent.Children[targetIndex]) =
                    (Parent.Children[targetIndex], Parent.Children[sourceIndex]);
                Parent.MarkDirty(UiDirtyFlags.Hierarchy | UiDirtyFlags.Layout);
            }
        }
        RaiseDrop(source);
    }
}

public enum UiUnit { Auto, Pixel, Percent }
public readonly record struct UiLength(UiUnit Unit, float Value)
{
    public static UiLength Auto => new(UiUnit.Auto, 0);
}
public readonly record struct UiEdges(float Left, float Top, float Right, float Bottom)
{
    public static UiEdges All(float value) => new(value, value, value, value);
}
public enum UiAlign { Auto, Start, Center, End, Stretch, SpaceBetween, SpaceAround }
public sealed class UiStyle
{
    public UiLength Width { get; set; } = UiLength.Auto;
    public UiLength Height { get; set; } = UiLength.Auto;
    public UiEdges Padding { get; set; }
    public UiEdges Margin { get; set; }
    public float Gap { get; set; }
    public float FlexGrow { get; set; }
    public bool Row { get; set; }
    public bool Absolute { get; set; }
    public UiLength Left { get; set; } = UiLength.Auto;
    public UiLength Top { get; set; } = UiLength.Auto;
    public UiLength Right { get; set; } = UiLength.Auto;
    public UiLength Bottom { get; set; } = UiLength.Auto;
    public UiAlign AlignItems { get; set; } = UiAlign.Stretch;
    public UiAlign AlignSelf { get; set; } = UiAlign.Auto;
    public UiAlign JustifyContent { get; set; } = UiAlign.Start;
    public UiAlign TextAlign { get; set; } = UiAlign.Start;
    public UiAlign VerticalAlign { get; set; } = UiAlign.Center;
    public float IconSize { get; set; } = 16;
    public Vecxy.Rendering.Color Background { get; set; } = new(0, 0, 0, 0);
    public Vecxy.Rendering.Color Color { get; set; } = Vecxy.Rendering.Color.White;
    public Vecxy.Rendering.Color BorderColor { get; set; } = new(0, 0, 0, 0);
    public Vecxy.Rendering.Color FillColor { get; set; } = Vecxy.Rendering.Color.White;
    public float BorderWidth { get; set; }
    public float FontSize { get; set; } = 14;
}
