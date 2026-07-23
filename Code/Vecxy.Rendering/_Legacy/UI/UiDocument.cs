using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Facebook.Yoga;
using static Facebook.Yoga.YGNodeAPI;
using static Facebook.Yoga.YGNodeLayoutAPI;
using static Facebook.Yoga.YGNodeStyleAPI;

namespace Vecxy.Rendering._Legacy.UI;

public sealed class UiDocument
{
    private readonly List<CssRule> _rules;
    private bool _stylesInitialized;
    public UiElement Root { get; }

    private UiDocument(UiElement root, List<CssRule> rules) { Root = root; _rules = rules; }

    public static UiDocument Load(string uxml, string css)
    {
        var xml = XDocument.Parse(uxml);
        return new UiDocument(Read(xml.Root ?? throw new InvalidDataException("UXML requires a root element.")), ParseCss(css));
    }

    public UiElement? Find(string id) => Root.Find(id);

    internal void Layout(float width, float height)
    {
        if (!_stylesInitialized) { ApplyStyles(Root); _stylesInitialized = true; }
        var yogaRoot = BuildYoga(Root);
        YGNodeStyleSetWidth(yogaRoot, width);
        YGNodeStyleSetHeight(yogaRoot, height);
        YGNodeCalculateLayout(yogaRoot, width, height, YGDirection.LTR);
        ReadLayout(Root, yogaRoot, 0, 0);
        YGNodeFreeRecursive(yogaRoot);
        Root.ClearDirty();
    }

    private void ApplyStyles(UiElement element)
    {
        var style = new UiStyle();
        foreach (var rule in _rules.Where(x => x.Matches(element))) rule.Apply(style);
        element.Style = style;
        foreach (var child in element.Children) ApplyStyles(child);
    }

    internal void RefreshStyle(UiElement? element)
    {
        if (element is null) return;
        var style = new UiStyle();
        foreach (var rule in _rules.Where(x => x.Matches(element))) rule.Apply(style);
        element.Style = style;
        element.MarkDirty(UiDirtyFlags.Style | UiDirtyFlags.Layout);
        if (element.Parent is { Type: "ScrollView" } scrollView) scrollView.VirtualOffsets = scrollView.VirtualHeights = null;
    }

    private static Node BuildYoga(UiElement element)
    {
        var node = YGNodeNew();
        YGNodeStyleSetFlexDirection(node, element.Style.Row ? YGFlexDirection.Row : YGFlexDirection.Column);
        SetLength(node, element.Style.Width, true);
        SetLength(node, element.Style.Height, false);
        SetEdges(node, element.Style.Padding, true);
        SetEdges(node, element.Style.Margin, false);
        YGNodeStyleSetGap(node, YGGutter.All, element.Style.Gap);
        YGNodeStyleSetFlexGrow(node, element.Style.FlexGrow);
        YGNodeStyleSetAlignItems(node, Align(element.Style.AlignItems));
        YGNodeStyleSetAlignSelf(node, Align(element.Style.AlignSelf));
        YGNodeStyleSetJustifyContent(node, Justify(element.Style.JustifyContent));
        if (element.Style.Absolute) YGNodeStyleSetPositionType(node, YGPositionType.Absolute);
        SetPosition(node, YGEdge.Left, element.Style.Left);
        SetPosition(node, YGEdge.Top, element.Style.Top);
        SetPosition(node, YGEdge.Right, element.Style.Right);
        SetPosition(node, YGEdge.Bottom, element.Style.Bottom);
        if (element.Children.Count == 0 && element.Text.Length > 0)
        {
            var measuredWidth = UiTextMetrics.Width(element.Text, element.Style.FontSize) +
                                element.Style.Padding.Left + element.Style.Padding.Right + element.Style.BorderWidth * 2;
            var measuredHeight = UiTextMetrics.Height(element.Style.FontSize) +
                                 element.Style.Padding.Top + element.Style.Padding.Bottom + element.Style.BorderWidth * 2;
            if (element.Style.Width.Unit == UiUnit.Auto) YGNodeStyleSetWidth(node, measuredWidth);
            if (element.Style.Height.Unit == UiUnit.Auto) YGNodeStyleSetHeight(node, measuredHeight);
        }
        else if (element.Type == "Icon")
        {
            if (element.Style.Width.Unit == UiUnit.Auto) YGNodeStyleSetWidth(node, element.Style.IconSize);
            if (element.Style.Height.Unit == UiUnit.Auto) YGNodeStyleSetHeight(node, element.Style.IconSize);
        }
        if (element.Type != "ScrollView")
            for (var i = 0; i < element.Children.Count; i++) YGNodeInsertChild(node, BuildYoga(element.Children[i]), (nuint)i);
        return node;
    }

    private static void SetEdges(Node node, UiEdges edges, bool padding)
    {
        void Set(YGEdge edge, float value) { if (padding) YGNodeStyleSetPadding(node, edge, value); else YGNodeStyleSetMargin(node, edge, value); }
        Set(YGEdge.Left, edges.Left); Set(YGEdge.Top, edges.Top); Set(YGEdge.Right, edges.Right); Set(YGEdge.Bottom, edges.Bottom);
    }

    private static void SetPosition(Node node, YGEdge edge, UiLength length)
    {
        if (length.Unit == UiUnit.Pixel) YGNodeStyleSetPosition(node, edge, length.Value);
        else if (length.Unit == UiUnit.Percent) YGNodeStyleSetPositionPercent(node, edge, length.Value);
    }

    private static YGAlign Align(UiAlign value) => value switch
    {
        UiAlign.Auto => YGAlign.Auto, UiAlign.Center => YGAlign.Center, UiAlign.End => YGAlign.FlexEnd,
        UiAlign.Stretch => YGAlign.Stretch, UiAlign.SpaceBetween => YGAlign.SpaceBetween,
        UiAlign.SpaceAround => YGAlign.SpaceAround, _ => YGAlign.FlexStart
    };
    private static YGJustify Justify(UiAlign value) => value switch
    {
        UiAlign.Center => YGJustify.Center, UiAlign.End => YGJustify.FlexEnd,
        UiAlign.SpaceBetween => YGJustify.SpaceBetween, UiAlign.SpaceAround => YGJustify.SpaceAround,
        _ => YGJustify.FlexStart
    };

    private static void SetLength(Node node, UiLength length, bool width)
    {
        if (length.Unit == UiUnit.Auto) return;
        if (width)
        {
            if (length.Unit == UiUnit.Percent) YGNodeStyleSetWidthPercent(node, length.Value);
            else YGNodeStyleSetWidth(node, length.Value);
        }
        else if (length.Unit == UiUnit.Percent) YGNodeStyleSetHeightPercent(node, length.Value);
        else YGNodeStyleSetHeight(node, length.Value);
    }

    private static void ReadLayout(UiElement element, Node node, float parentX, float parentY)
    {
        var x = parentX + YGNodeLayoutGetLeft(node);
        var y = parentY + YGNodeLayoutGetTop(node);
        element.Layout = new(x, y, YGNodeLayoutGetWidth(node), YGNodeLayoutGetHeight(node));
        if (element.Type == "ScrollView")
        {
            LayoutVirtualChildren(element);
            return;
        }
        for (var i = 0; i < element.Children.Count; i++)
            ReadLayout(element.Children[i], YGNodeGetChild(node, (nuint)i)!, x, y - element.ScrollY);
    }

    private static void LayoutVirtualChildren(UiElement scrollView)
    {
        var view = scrollView.Layout;
        var contentWidth = MathF.Max(0, view.Z - scrollView.Style.Padding.Left - scrollView.Style.Padding.Right);
        const float overscan = 80;
        if (scrollView.VirtualOffsets is null || scrollView.VirtualOffsets.Length != scrollView.Children.Count)
        {
            scrollView.VirtualOffsets = new float[scrollView.Children.Count];
            scrollView.VirtualHeights = new float[scrollView.Children.Count];
            var cursor = scrollView.Style.Padding.Top;
            for (var i = 0; i < scrollView.Children.Count; i++)
            {
                var child = scrollView.Children[i];
                cursor += child.Style.Margin.Top;
                scrollView.VirtualOffsets[i] = cursor;
                var itemHeight = child.Style.Height.Unit == UiUnit.Pixel ? child.Style.Height.Value :
                    MathF.Max(18, UiTextMetrics.Height(child.Style.FontSize) + child.Style.Padding.Top +
                                  child.Style.Padding.Bottom + child.Style.BorderWidth * 2);
                scrollView.VirtualHeights[i] = itemHeight;
                cursor += itemHeight + child.Style.Margin.Bottom + scrollView.Style.Gap;
            }
            scrollView.VirtualContentHeight = cursor + scrollView.Style.Padding.Bottom;
        }
        for (var i = scrollView.VirtualStart; i < scrollView.VirtualEnd; i++) scrollView.Children[i].IsVirtualVisible = false;
        var offsets = scrollView.VirtualOffsets;
        var heights = scrollView.VirtualHeights!;
        var first = LowerBound(offsets, scrollView.ScrollY - overscan);
        while (first > 0 && offsets[first] > scrollView.ScrollY - overscan) first--;
        var end = first;
        while (end < offsets.Length && offsets[end] <= scrollView.ScrollY + view.W + overscan) end++;
        scrollView.VirtualStart = first;
        scrollView.VirtualEnd = end;
        for (var i = first; i < end; i++)
        {
            var child = scrollView.Children[i];
            var width = child.Style.Width.Unit == UiUnit.Pixel ? child.Style.Width.Value : contentWidth - child.Style.Margin.Left - child.Style.Margin.Right;
            child.Layout = new(view.X + scrollView.Style.Padding.Left + child.Style.Margin.Left,
                view.Y + offsets[i] - scrollView.ScrollY, MathF.Max(0, width), heights[i]);
            child.IsVirtualVisible = true;
        }
    }

    private static int LowerBound(float[] values, float target)
    {
        var low = 0; var high = values.Length;
        while (low < high) { var middle = low + (high - low) / 2; if (values[middle] < target) low = middle + 1; else high = middle; }
        return Math.Min(low, Math.Max(0, values.Length - 1));
    }

    private static UiElement Read(XElement xml)
    {
        var result = new UiElement { Type = xml.Name.LocalName, Id = (string?)xml.Attribute("id"), Text = (string?)xml.Attribute("text") ?? "" };
        result.Value = float.TryParse((string?)xml.Attribute("value"), CultureInfo.InvariantCulture, out var value) ? value : 0;
        result.Draggable = bool.TryParse((string?)xml.Attribute("draggable"), out var draggable) && draggable;
        result.DragVisual = !bool.TryParse((string?)xml.Attribute("drag-visual"), out var dragVisual) || dragVisual;
        result.DropTarget = bool.TryParse((string?)xml.Attribute("drop-target"), out var dropTarget) && dropTarget;
        result.Options = ((string?)xml.Attribute("options") ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        result.IconName = (string?)xml.Attribute("name") ?? string.Empty;
        result.IsHitTestVisible = !string.Equals((string?)xml.Attribute("picking-mode"), "ignore", StringComparison.OrdinalIgnoreCase);
        if (result.Type == "Icon" && xml.Attribute("picking-mode") is null) result.IsHitTestVisible = false;
        foreach (var name in ((string?)xml.Attribute("class") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)) result.Classes.Add(name);
        foreach (var child in xml.Elements()) result.Add(Read(child));
        return result;
    }

    private static List<CssRule> ParseCss(string css) => Regex.Matches(css, @"(?<s>[^{}]+)\{(?<b>[^}]*)\}")
        .Select(x => new CssRule(x.Groups["s"].Value.Trim(), x.Groups["b"].Value.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(d => d.Split(':', 2)).Where(d => d.Length == 2).ToDictionary(d => d[0].Trim(), d => d[1].Trim(), StringComparer.OrdinalIgnoreCase))).ToList();

    private sealed record CssRule(string Selector, Dictionary<string, string> Values)
    {
        public bool Matches(UiElement e)
        {
            var selector = Selector;
            if (selector.EndsWith(":hover")) { if (!e.IsHovered) return false; selector = selector[..^6]; }
            else if (selector.EndsWith(":active")) { if (!e.IsPressed) return false; selector = selector[..^7]; }
            else if (selector.EndsWith(":focus")) { if (!e.IsFocused) return false; selector = selector[..^6]; }
            var parts = selector.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0 || !MatchesSimple(parts[^1], e)) return false;
            var ancestor = e.Parent;
            for (var index = parts.Length - 2; index >= 0; index--)
            {
                while (ancestor is not null && !MatchesSimple(parts[index], ancestor)) ancestor = ancestor.Parent;
                if (ancestor is null) return false;
                ancestor = ancestor.Parent;
            }
            return true;
        }
        private static bool MatchesSimple(string selector, UiElement element) =>
            selector.StartsWith('#') ? element.Id == selector[1..] :
            selector.StartsWith('.') ? element.Classes.Contains(selector[1..]) :
            element.Type.Equals(selector, StringComparison.OrdinalIgnoreCase);
        public void Apply(UiStyle s)
        {
            foreach (var (key, value) in Values) switch (key.ToLowerInvariant())
            {
                case "width": s.Width = Length(value); break;
                case "height": s.Height = Length(value); break;
                case "padding": s.Padding = Edges(value); break;
                case "padding-left": s.Padding = s.Padding with { Left = Number(value) }; break;
                case "padding-top": s.Padding = s.Padding with { Top = Number(value) }; break;
                case "padding-right": s.Padding = s.Padding with { Right = Number(value) }; break;
                case "padding-bottom": s.Padding = s.Padding with { Bottom = Number(value) }; break;
                case "margin": s.Margin = Edges(value); break;
                case "margin-left": s.Margin = s.Margin with { Left = Number(value) }; break;
                case "margin-top": s.Margin = s.Margin with { Top = Number(value) }; break;
                case "margin-right": s.Margin = s.Margin with { Right = Number(value) }; break;
                case "margin-bottom": s.Margin = s.Margin with { Bottom = Number(value) }; break;
                case "gap": s.Gap = Number(value); break;
                case "flex-grow": s.FlexGrow = Number(value); break;
                case "flex-direction": s.Row = value.Equals("row", StringComparison.OrdinalIgnoreCase); break;
                case "align-items": s.AlignItems = ParseAlign(value); break;
                case "align-self": s.AlignSelf = ParseAlign(value); break;
                case "justify-content": s.JustifyContent = ParseAlign(value); break;
                case "text-align": s.TextAlign = ParseAlign(value); break;
                case "vertical-align": s.VerticalAlign = ParseAlign(value); break;
                case "icon-size": s.IconSize = Number(value); break;
                case "position": s.Absolute = value.Equals("absolute", StringComparison.OrdinalIgnoreCase); break;
                case "left": s.Left = Length(value); break;
                case "top": s.Top = Length(value); break;
                case "right": s.Right = Length(value); break;
                case "bottom": s.Bottom = Length(value); break;
                case "font-size": s.FontSize = Number(value); break;
                case "border-width": s.BorderWidth = Number(value); break;
                case "background-color": s.Background = ParseColor(value); break;
                case "border-color": s.BorderColor = ParseColor(value); break;
                case "fill-color": s.FillColor = ParseColor(value); break;
                case "color": s.Color = ParseColor(value); break;
            }
        }
        private static UiEdges Edges(string value)
        {
            var p = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Number).ToArray();
            return p.Length switch
            {
                1 => UiEdges.All(p[0]), 2 => new(p[1], p[0], p[1], p[0]),
                3 => new(p[1], p[0], p[1], p[2]), 4 => new(p[3], p[0], p[1], p[2]), _ => default
            };
        }
        private static UiAlign ParseAlign(string value) => value.Trim().ToLowerInvariant() switch
        {
            "auto" => UiAlign.Auto, "center" => UiAlign.Center, "end" or "flex-end" => UiAlign.End,
            "stretch" => UiAlign.Stretch, "space-between" => UiAlign.SpaceBetween,
            "space-around" => UiAlign.SpaceAround, _ => UiAlign.Start
        };
        private static UiLength Length(string value) => value.Trim() switch { "auto" => UiLength.Auto, var x when x.EndsWith('%') => new(UiUnit.Percent, Number(x)), _ => new(UiUnit.Pixel, Number(value)) };
        private static float Number(string value) => float.TryParse(value.Trim().TrimEnd('p', 'x', '%'), CultureInfo.InvariantCulture, out var n) ? n : 0;
        private static Color ParseColor(string text)
        {
            var value = text.Trim().TrimStart('#');
            if (value.Length is 6 or 8 && uint.TryParse(value, NumberStyles.HexNumber, null, out var rgba))
            {
                if (value.Length == 6) rgba = rgba << 8 | 255;
                return new((rgba >> 24 & 255) / 255f, (rgba >> 16 & 255) / 255f, (rgba >> 8 & 255) / 255f, (rgba & 255) / 255f);
            }
            return new(0, 0, 0, 0);
        }
    }
}
