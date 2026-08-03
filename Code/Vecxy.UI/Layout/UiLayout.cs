using System.Numerics;
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
        ApplyRecursive(root, width, height);
        YGNodeStyleSetWidth(root.YogaNode, width);
        YGNodeStyleSetHeight(root.YogaNode, height);
        YGNodeCalculateLayout(root.YogaNode, width, height, YGDirection.LTR);
        ReadRecursive(root, 0.0f, 0.0f, width, height);
        for (var pass = 0; pass < 3 && UiGridLayout.PlaceGrids(root, width, height); pass++)
        {
            YGNodeCalculateLayout(root.YogaNode, width, height, YGDirection.LTR);
            ReadRecursive(root, 0.0f, 0.0f, width, height);
        }
        UpdateScrollExtents(root);
    }

    private static void ApplyRecursive(UiElement element, float viewportWidth, float viewportHeight)
    {
        var node = element.YogaNode;
        var style = element.ComputedStyle;
        style.FontSize = Math.Max(1.0f, ResolvePoints(style.FontSizeLength, viewportWidth, viewportHeight));
        style.BorderWidth = Math.Max(0.0f, ResolvePoints(style.BorderWidthLength, viewportWidth, viewportHeight));
        style.BoxShadows = style.BoxShadowDefinitions.Select(shadow => new UiResolvedBoxShadow(
            ResolvePoints(shadow.OffsetX, viewportWidth, viewportHeight),
            ResolvePoints(shadow.OffsetY, viewportWidth, viewportHeight),
            Math.Max(0.0f, ResolvePoints(shadow.BlurRadius, viewportWidth, viewportHeight)),
            ResolvePoints(shadow.SpreadRadius, viewportWidth, viewportHeight),
            shadow.Color,
            shadow.Inset)).ToArray();

        YGNodeStyleSetDisplay(node, style.Display switch
        {
            "none" => YGDisplay.None,
            "grid" => YGDisplay.Flex,
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
        YGNodeStyleSetOverflow(
            node,
            style.OverflowX is "scroll" or "auto" || style.OverflowY is "scroll" or "auto"
                ? YGOverflow.Scroll
                : style.OverflowX == "hidden" || style.OverflowY == "hidden"
                    ? YGOverflow.Hidden
                    : YGOverflow.Visible);
        YGNodeStyleSetFlexGrow(node, style.FlexGrow);
        YGNodeStyleSetFlexShrink(node, style.FlexShrink);
        SetFlexBasis(node, style.FlexBasis, viewportWidth, viewportHeight);
        SetSize(node, style.Width, YGNodeStyleSetWidth, YGNodeStyleSetWidthPercent, YGNodeStyleSetWidthAuto, viewportWidth, viewportHeight);
        SetSize(node, style.Height, YGNodeStyleSetHeight, YGNodeStyleSetHeightPercent, YGNodeStyleSetHeightAuto, viewportWidth, viewportHeight);
        SetOptionalSize(node, style.MinWidth, YGNodeStyleSetMinWidth, YGNodeStyleSetMinWidthPercent, viewportWidth, viewportHeight);
        SetOptionalSize(node, style.MinHeight, YGNodeStyleSetMinHeight, YGNodeStyleSetMinHeightPercent, viewportWidth, viewportHeight);
        SetOptionalSize(node, style.MaxWidth, YGNodeStyleSetMaxWidth, YGNodeStyleSetMaxWidthPercent, viewportWidth, viewportHeight);
        SetOptionalSize(node, style.MaxHeight, YGNodeStyleSetMaxHeight, YGNodeStyleSetMaxHeightPercent, viewportWidth, viewportHeight);
        ApplyEdges(node, style.Margin, (target, edge, value) => SetMargin(target, edge, value, viewportWidth, viewportHeight));
        ApplyEdges(node, style.Padding, (target, edge, value) => SetPadding(target, edge, value, viewportWidth, viewportHeight));
        ApplyEdges(node, style.Inset, (target, edge, value) => SetPosition(target, edge, value, viewportWidth, viewportHeight));
        SetGap(node, style.Gap, viewportWidth, viewportHeight);
        if (style.RowGap is { } rowGap)
            SetGap(node, YGGutter.Row, rowGap, viewportWidth, viewportHeight);
        if (style.ColumnGap is { } columnGap)
            SetGap(node, YGGutter.Column, columnGap, viewportWidth, viewportHeight);
        UiGridLayout.ResetNativeGrid(node);
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
            ApplyRecursive(child, viewportWidth, viewportHeight);
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

    private static void ReadRecursive(
        UiElement element,
        float parentX,
        float parentY,
        float viewportWidth,
        float viewportHeight)
    {
        var left = parentX + YGNodeLayoutGetLeft(element.YogaNode);
        var top = parentY + YGNodeLayoutGetTop(element.YogaNode);
        element.Bounds = new Rect(
            left,
            top,
            Math.Max(0.0f, YGNodeLayoutGetWidth(element.YogaNode)),
            Math.Max(0.0f, YGNodeLayoutGetHeight(element.YogaNode)));
        var style = element.ComputedStyle;
        style.BorderRadius = Math.Max(0.0f, style.BorderRadiusLength.Unit == EUiLengthUnit.Percent
            ? Math.Min(element.Bounds.Width, element.Bounds.Height) * style.BorderRadiusLength.Value * 0.01f
            : ResolvePoints(style.BorderRadiusLength, element.Bounds.Width, element.Bounds.Height));
        style.Transform = style.TransformDefinition.Resolve(
            element.Bounds.Width,
            element.Bounds.Height,
            viewportWidth,
            viewportHeight);

        foreach (var child in element.Children)
            ReadRecursive(child, left, top, viewportWidth, viewportHeight);
    }

    private static void UpdateScrollExtents(UiElement element)
    {
        foreach (var child in element.Children)
            UpdateScrollExtents(child);

        var width = element.Bounds.Width;
        var height = element.Bounds.Height;
        foreach (var child in element.Children.Where(child => child.ComputedStyle.Display != "none"))
        {
            width = Math.Max(width, child.Bounds.Right - element.Bounds.Left);
            height = Math.Max(height, child.Bounds.Bottom - element.Bounds.Top);
        }
        element.UpdateScrollExtent(new Vector2(width, height));
    }

    private static void SetFlexBasis(Node node, UiLength value, float viewportWidth, float viewportHeight)
    {
        switch (value.Unit)
        {
            case EUiLengthUnit.Percent: YGNodeStyleSetFlexBasisPercent(node, value.Value); break;
            case EUiLengthUnit.Auto: YGNodeStyleSetFlexBasisAuto(node); break;
            default: YGNodeStyleSetFlexBasis(node, ResolvePoints(value, viewportWidth, viewportHeight)); break;
        }
    }

    private static void SetSize(
        Node node,
        UiLength value,
        Action<Node, float> points,
        Action<Node, float> percent,
        Action<Node> auto,
        float viewportWidth,
        float viewportHeight)
    {
        switch (value.Unit)
        {
            case EUiLengthUnit.Percent: percent(node, value.Value); break;
            case EUiLengthUnit.Auto: auto(node); break;
            default: points(node, ResolvePoints(value, viewportWidth, viewportHeight)); break;
        }
    }

    private static void SetOptionalSize(
        Node node,
        UiLength value,
        Action<Node, float> points,
        Action<Node, float> percent,
        float viewportWidth,
        float viewportHeight)
    {
        if (value.Unit == EUiLengthUnit.Percent)
            percent(node, value.Value);
        else
            points(node, value.Unit == EUiLengthUnit.Auto
                ? float.NaN
                : ResolvePoints(value, viewportWidth, viewportHeight));
    }

    private static void ApplyEdges(Node node, UiEdges edges, Action<Node, YGEdge, UiLength> setter)
    {
        setter(node, YGEdge.Top, edges.Top);
        setter(node, YGEdge.Right, edges.Right);
        setter(node, YGEdge.Bottom, edges.Bottom);
        setter(node, YGEdge.Left, edges.Left);
    }

    private static void SetMargin(Node node, YGEdge edge, UiLength value, float viewportWidth, float viewportHeight)
    {
        if (value.Unit == EUiLengthUnit.Auto)
            YGNodeStyleSetMarginAuto(node, edge);
        else if (value.Unit == EUiLengthUnit.Percent)
            YGNodeStyleSetMarginPercent(node, edge, value.Value);
        else
            YGNodeStyleSetMargin(node, edge, ResolvePoints(value, viewportWidth, viewportHeight));
    }

    private static void SetPadding(Node node, YGEdge edge, UiLength value, float viewportWidth, float viewportHeight)
    {
        if (value.Unit == EUiLengthUnit.Percent)
            YGNodeStyleSetPaddingPercent(node, edge, value.Value);
        else
            YGNodeStyleSetPadding(node, edge, value.Unit == EUiLengthUnit.Auto ? 0.0f : ResolvePoints(value, viewportWidth, viewportHeight));
    }

    private static void SetPosition(Node node, YGEdge edge, UiLength value, float viewportWidth, float viewportHeight)
    {
        if (value.Unit == EUiLengthUnit.Auto)
            YGNodeStyleSetPositionAuto(node, edge);
        else if (value.Unit == EUiLengthUnit.Percent)
            YGNodeStyleSetPositionPercent(node, edge, value.Value);
        else
            YGNodeStyleSetPosition(node, edge, ResolvePoints(value, viewportWidth, viewportHeight));
    }

    private static void SetGap(Node node, UiLength value, float viewportWidth, float viewportHeight)
        => SetGap(node, YGGutter.All, value, viewportWidth, viewportHeight);

    private static void SetGap(Node node, YGGutter gutter, UiLength value, float viewportWidth, float viewportHeight)
    {
        if (value.Unit == EUiLengthUnit.Percent)
            YGNodeStyleSetGapPercent(node, gutter, value.Value);
        else
            YGNodeStyleSetGap(node, gutter, value.Unit == EUiLengthUnit.Auto ? 0.0f : ResolvePoints(value, viewportWidth, viewportHeight));
    }

    internal static float ResolvePoints(UiLength value, float viewportWidth, float viewportHeight) =>
        value.Unit switch
        {
            EUiLengthUnit.Pixel or EUiLengthUnit.Ui => value.Value,
            EUiLengthUnit.ViewportWidth => viewportWidth * value.Value * 0.01f,
            EUiLengthUnit.ViewportHeight => viewportHeight * value.Value * 0.01f,
            _ => value.Value
        };

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
