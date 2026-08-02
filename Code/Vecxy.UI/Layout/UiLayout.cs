using Facebook.Yoga;
using Vecxy.Kernel;
using static Facebook.Yoga.YGNodeAPI;
using static Facebook.Yoga.YGNodeLayoutAPI;
using static Facebook.Yoga.YGNodeStyleAPI;

namespace Vecxy.UI;

internal static class UiLayout
{
    public static void Calculate(UiElement root, int width, int height)
    {
        ApplyRecursive(root);
        YGNodeStyleSetWidth(root.YogaNode, width);
        YGNodeStyleSetHeight(root.YogaNode, height);
        YGNodeCalculateLayout(root.YogaNode, width, height, YGDirection.LTR);
        ReadRecursive(root, 0.0f, 0.0f);
    }

    private static void ApplyRecursive(UiElement element)
    {
        var node = element.YogaNode;
        var style = element.ComputedStyle;

        YGNodeStyleSetDisplay(node, style.Display switch
        {
            "none" => YGDisplay.None,
            "grid" => YGDisplay.Grid,
            "contents" => YGDisplay.Contents,
            _ => YGDisplay.Flex
        });
        YGNodeStyleSetPositionType(node, style.Position switch
        {
            "absolute" or "fixed" => YGPositionType.Absolute,
            "static" => YGPositionType.Static,
            _ => YGPositionType.Relative
        });
        YGNodeStyleSetFlexDirection(node, style.FlexDirection switch
        {
            "row" => YGFlexDirection.Row,
            "row-reverse" => YGFlexDirection.RowReverse,
            "column-reverse" => YGFlexDirection.ColumnReverse,
            _ => YGFlexDirection.Column
        });
        YGNodeStyleSetFlexWrap(node, style.FlexWrap switch
        {
            "wrap" => YGWrap.Wrap,
            "wrap-reverse" => YGWrap.WrapReverse,
            _ => YGWrap.NoWrap
        });
        YGNodeStyleSetJustifyContent(node, ToJustify(style.JustifyContent));
        YGNodeStyleSetAlignItems(node, ToAlign(style.AlignItems, YGAlign.Stretch));
        YGNodeStyleSetAlignSelf(node, ToAlign(style.AlignSelf, YGAlign.Auto));
        YGNodeStyleSetOverflow(node, style.Overflow switch
        {
            "hidden" => YGOverflow.Hidden,
            "scroll" or "auto" => YGOverflow.Scroll,
            _ => YGOverflow.Visible
        });
        YGNodeStyleSetFlexGrow(node, style.FlexGrow);
        YGNodeStyleSetFlexShrink(node, style.FlexShrink);
        SetFlexBasis(node, style.FlexBasis);
        SetSize(node, style.Width, YGNodeStyleSetWidth, YGNodeStyleSetWidthPercent, YGNodeStyleSetWidthAuto);
        SetSize(node, style.Height, YGNodeStyleSetHeight, YGNodeStyleSetHeightPercent, YGNodeStyleSetHeightAuto);
        SetOptionalSize(node, style.MinWidth, YGNodeStyleSetMinWidth, YGNodeStyleSetMinWidthPercent);
        SetOptionalSize(node, style.MinHeight, YGNodeStyleSetMinHeight, YGNodeStyleSetMinHeightPercent);
        SetOptionalSize(node, style.MaxWidth, YGNodeStyleSetMaxWidth, YGNodeStyleSetMaxWidthPercent);
        SetOptionalSize(node, style.MaxHeight, YGNodeStyleSetMaxHeight, YGNodeStyleSetMaxHeightPercent);
        ApplyEdges(node, style.Margin, SetMargin);
        ApplyEdges(node, style.Padding, SetPadding);
        ApplyEdges(node, style.Inset, SetPosition);
        SetGap(node, style.Gap);
        YGNodeStyleSetBorder(node, YGEdge.All, Math.Max(0.0f, style.BorderWidth));
        YGNodeStyleSetAspectRatio(node, style.AspectRatio ?? float.NaN);

        if (element.Children.Count == 0 && element.TagName is "text" or "image")
            YGNodeSetMeasureFunc(
                node,
                element.TagName == "text"
                    ? new YGMeasureFunc(MeasureText)
                    : new YGMeasureFunc(MeasureImage));
        else if (YGNodeHasMeasureFunc(node))
            YGNodeSetMeasureFunc(node, null);

        foreach (var child in element.Children)
            ApplyRecursive(child);
    }

    private static YGSize MeasureText(
        Node node,
        float availableWidth,
        MeasureMode widthMode,
        float availableHeight,
        MeasureMode heightMode)
    {
        var element = (UiElement?)YGNodeGetContext(node);
        if (element is null)
            return default;

        var size = element.Font is { } font
            ? UiBitmapFont.Measure(font, element.Text, element.ComputedStyle.FontSize)
            : UiFallbackFont.Measure(element.Text, element.ComputedStyle.FontSize);
        if (widthMode == MeasureMode.Exactly)
            size.X = availableWidth;
        else if (widthMode == MeasureMode.AtMost)
            size.X = Math.Min(size.X, availableWidth);
        if (heightMode == MeasureMode.Exactly)
            size.Y = availableHeight;
        else if (heightMode == MeasureMode.AtMost)
            size.Y = Math.Min(size.Y, availableHeight);
        return new YGSize { Width = size.X, Height = size.Y };
    }

    private static YGSize MeasureImage(
        Node node,
        float availableWidth,
        MeasureMode widthMode,
        float availableHeight,
        MeasureMode heightMode)
    {
        var element = (UiElement?)YGNodeGetContext(node);
        var size = element?.IntrinsicSize ?? default;
        if (widthMode == MeasureMode.Exactly)
            size.X = availableWidth;
        else if (widthMode == MeasureMode.AtMost)
            size.X = Math.Min(size.X, availableWidth);
        if (heightMode == MeasureMode.Exactly)
            size.Y = availableHeight;
        else if (heightMode == MeasureMode.AtMost)
            size.Y = Math.Min(size.Y, availableHeight);
        return new YGSize { Width = size.X, Height = size.Y };
    }

    private static void ReadRecursive(UiElement element, float parentX, float parentY)
    {
        var left = parentX + YGNodeLayoutGetLeft(element.YogaNode);
        var top = parentY + YGNodeLayoutGetTop(element.YogaNode);
        element.Bounds = new Rect(
            left,
            top,
            Math.Max(0.0f, YGNodeLayoutGetWidth(element.YogaNode)),
            Math.Max(0.0f, YGNodeLayoutGetHeight(element.YogaNode)));

        foreach (var child in element.Children)
            ReadRecursive(child, left, top);
    }

    private static void SetFlexBasis(Node node, UiLength value)
    {
        switch (value.Unit)
        {
            case EUiLengthUnit.Pixel: YGNodeStyleSetFlexBasis(node, value.Value); break;
            case EUiLengthUnit.Percent: YGNodeStyleSetFlexBasisPercent(node, value.Value); break;
            default: YGNodeStyleSetFlexBasisAuto(node); break;
        }
    }

    private static void SetSize(
        Node node,
        UiLength value,
        Action<Node, float> points,
        Action<Node, float> percent,
        Action<Node> auto)
    {
        switch (value.Unit)
        {
            case EUiLengthUnit.Pixel: points(node, value.Value); break;
            case EUiLengthUnit.Percent: percent(node, value.Value); break;
            default: auto(node); break;
        }
    }

    private static void SetOptionalSize(
        Node node,
        UiLength value,
        Action<Node, float> points,
        Action<Node, float> percent)
    {
        if (value.Unit == EUiLengthUnit.Percent)
            percent(node, value.Value);
        else
            points(node, value.Unit == EUiLengthUnit.Pixel ? value.Value : float.NaN);
    }

    private static void ApplyEdges(Node node, UiEdges edges, Action<Node, YGEdge, UiLength> setter)
    {
        setter(node, YGEdge.Top, edges.Top);
        setter(node, YGEdge.Right, edges.Right);
        setter(node, YGEdge.Bottom, edges.Bottom);
        setter(node, YGEdge.Left, edges.Left);
    }

    private static void SetMargin(Node node, YGEdge edge, UiLength value)
    {
        if (value.Unit == EUiLengthUnit.Auto)
            YGNodeStyleSetMarginAuto(node, edge);
        else if (value.Unit == EUiLengthUnit.Percent)
            YGNodeStyleSetMarginPercent(node, edge, value.Value);
        else
            YGNodeStyleSetMargin(node, edge, value.Value);
    }

    private static void SetPadding(Node node, YGEdge edge, UiLength value)
    {
        if (value.Unit == EUiLengthUnit.Percent)
            YGNodeStyleSetPaddingPercent(node, edge, value.Value);
        else
            YGNodeStyleSetPadding(node, edge, value.Unit == EUiLengthUnit.Pixel ? value.Value : 0.0f);
    }

    private static void SetPosition(Node node, YGEdge edge, UiLength value)
    {
        if (value.Unit == EUiLengthUnit.Auto)
            YGNodeStyleSetPositionAuto(node, edge);
        else if (value.Unit == EUiLengthUnit.Percent)
            YGNodeStyleSetPositionPercent(node, edge, value.Value);
        else
            YGNodeStyleSetPosition(node, edge, value.Value);
    }

    private static void SetGap(Node node, UiLength value)
    {
        if (value.Unit == EUiLengthUnit.Percent)
            YGNodeStyleSetGapPercent(node, YGGutter.All, value.Value);
        else
            YGNodeStyleSetGap(node, YGGutter.All, value.Unit == EUiLengthUnit.Pixel ? value.Value : 0.0f);
    }

    private static YGJustify ToJustify(string value) => value switch
    {
        "center" => YGJustify.Center,
        "flex-end" or "end" => YGJustify.FlexEnd,
        "space-between" => YGJustify.SpaceBetween,
        "space-around" => YGJustify.SpaceAround,
        "space-evenly" => YGJustify.SpaceEvenly,
        _ => YGJustify.FlexStart
    };

    private static YGAlign ToAlign(string value, YGAlign fallback) => value switch
    {
        "auto" => YGAlign.Auto,
        "flex-start" or "start" => YGAlign.FlexStart,
        "center" => YGAlign.Center,
        "flex-end" or "end" => YGAlign.FlexEnd,
        "stretch" => YGAlign.Stretch,
        "baseline" => YGAlign.Baseline,
        "space-between" => YGAlign.SpaceBetween,
        "space-around" => YGAlign.SpaceAround,
        _ => fallback
    };
}
