using System.Numerics;
using Facebook.Yoga;
using Vecxy.Kernel;
using static Facebook.Yoga.YGNodeAPI;
using static Facebook.Yoga.YGNodeLayoutAPI;
using static Facebook.Yoga.YGNodeStyleAPI;

namespace Vecxy.UI;

internal static class UiLayout
{
    public static void Calculate(UiElement root, int width, int height, bool enableShadows = true)
    {
        ApplyRecursive(root, width, height, enableShadows, null);
        YGNodeStyleSetWidth(root.YogaNode, width);
        YGNodeStyleSetHeight(root.YogaNode, height);
        YGNodeCalculateLayout(root.YogaNode, width, height, YGDirection.LTR);
        ReadRecursive(root, 0.0f, 0.0f, width, height, null);
        for (var pass = 0; pass < 3 && UiGridLayout.PlaceGrids(root, width, height); pass++)
        {
            YGNodeCalculateLayout(root.YogaNode, width, height, YGDirection.LTR);
            ReadRecursive(root, 0.0f, 0.0f, width, height, null);
        }
        UpdateScrollExtents(root, null);
    }

    private static void ApplyRecursive(
        UiElement element,
        float viewportWidth,
        float viewportHeight,
        bool enableShadows,
        UiElement? virtualViewport)
    {
        if (virtualViewport is not null && IsOutsideVirtualViewport(element, virtualViewport))
            return;
        var node = element.YogaNode;
        var style = element.ComputedStyle;
        YGNodeStyleSetDisplay(node, style.Display switch
        {
            "none" => YGDisplay.None,
            "grid" => YGDisplay.Flex,
            "contents" => YGDisplay.Contents,
            _ => YGDisplay.Flex
        });
        if (style.Display == "none")
            return;
        style.FontSize = Math.Max(1.0f, ResolvePoints(style.FontSizeLength, viewportWidth, viewportHeight));
        style.BorderWidth = Math.Max(0.0f, ResolvePoints(style.BorderWidthLength, viewportWidth, viewportHeight));
        if (!enableShadows || style.BoxShadowDefinitions.Count == 0)
        {
            style.BoxShadows = Array.Empty<UiResolvedBoxShadow>();
        }
        else
        {
            var resolvedShadows = new UiResolvedBoxShadow[style.BoxShadowDefinitions.Count];
            for (var index = 0; index < resolvedShadows.Length; index++)
            {
                var shadow = style.BoxShadowDefinitions[index];
                resolvedShadows[index] = new UiResolvedBoxShadow(
                    ResolvePoints(shadow.OffsetX, viewportWidth, viewportHeight),
                    ResolvePoints(shadow.OffsetY, viewportWidth, viewportHeight),
                    Math.Max(0.0f, ResolvePoints(shadow.BlurRadius, viewportWidth, viewportHeight)),
                    ResolvePoints(shadow.SpreadRadius, viewportWidth, viewportHeight),
                    shadow.Color,
                    shadow.Inset);
            }
            style.BoxShadows = resolvedShadows;
        }

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

        var childVirtualViewport = element.UsesVirtualization ? element : virtualViewport;
        foreach (var child in element.Children)
            ApplyRecursive(child, viewportWidth, viewportHeight, enableShadows, childVirtualViewport);
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

        var wraps = element.ComputedStyle.WhiteSpace is "normal" or "pre-wrap" &&
                    widthMode is MeasureMode.AtMost or MeasureMode.Exactly;
        var wrappingWidth = wraps ? availableWidth : float.PositiveInfinity;
        var size = element.Font is { } font
            ? UiBitmapFont.Measure(element, font, element.Text, element.ComputedStyle.FontSize, wrappingWidth)
            : UiFallbackFont.Measure(element, element.Text, element.ComputedStyle.FontSize, wrappingWidth);
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
        float viewportHeight,
        UiElement? virtualViewport)
    {
        var left = parentX + YGNodeLayoutGetLeft(element.YogaNode);
        var top = parentY + YGNodeLayoutGetTop(element.YogaNode);
        element.Bounds = new Rect(
            left,
            top,
            Math.Max(0.0f, YGNodeLayoutGetWidth(element.YogaNode)),
            Math.Max(0.0f, YGNodeLayoutGetHeight(element.YogaNode)));
        var style = element.ComputedStyle;
        if (style.Display == "none")
            return;
        if (virtualViewport is not null && IsOutsideVirtualViewport(element, virtualViewport))
            return;
        style.BorderRadius = Math.Max(0.0f, style.BorderRadiusLength.Unit == EUiLengthUnit.Percent
            ? Math.Min(element.Bounds.Width, element.Bounds.Height) * style.BorderRadiusLength.Value * 0.01f
            : ResolvePoints(style.BorderRadiusLength, element.Bounds.Width, element.Bounds.Height));
        style.Transform = style.TransformDefinition.Resolve(
            element.Bounds.Width,
            element.Bounds.Height,
            viewportWidth,
            viewportHeight);

        var childVirtualViewport = element.UsesVirtualization ? element : virtualViewport;
        foreach (var child in element.Children)
            ReadRecursive(child, left, top, viewportWidth, viewportHeight, childVirtualViewport);
    }

    private static void UpdateScrollExtents(UiElement element, UiElement? virtualViewport)
    {
        if (element.ComputedStyle.Display == "none")
            return;
        if (virtualViewport is not null && IsOutsideVirtualViewport(element, virtualViewport))
            return;
        var childVirtualViewport = element.UsesVirtualization ? element : virtualViewport;
        foreach (var child in element.Children)
            UpdateScrollExtents(child, childVirtualViewport);

        var width = element.Bounds.Width;
        var height = element.Bounds.Height;
        var rightPadding = YGNodeLayoutGetPadding(element.YogaNode, YGEdge.Right);
        var bottomPadding = YGNodeLayoutGetPadding(element.YogaNode, YGEdge.Bottom);
        foreach (var child in element.Children.Where(child => child.ComputedStyle.Display != "none"))
        {
            var childClipsX = child.ComputedStyle.OverflowX is "hidden" or "scroll" or "auto";
            var childClipsY = child.ComputedStyle.OverflowY is "hidden" or "scroll" or "auto";
            var childWidth = childClipsX ? child.Bounds.Width : Math.Max(child.Bounds.Width, child.ScrollExtent.X);
            var childHeight = childClipsY ? child.Bounds.Height : Math.Max(child.Bounds.Height, child.ScrollExtent.Y);
            width = Math.Max(width, child.Bounds.Left - element.Bounds.Left + childWidth + rightPadding);
            height = Math.Max(height, child.Bounds.Top - element.Bounds.Top + childHeight + bottomPadding);
        }
        element.UpdateScrollExtent(new Vector2(width, height));
    }

    private static bool IsOutsideVirtualViewport(UiElement element, UiElement viewport)
    {
        if (element.Bounds.Width <= 0.0f || element.Bounds.Height <= 0.0f ||
            viewport.Bounds.Width <= 0.0f || viewport.Bounds.Height <= 0.0f)
            return false;

        var bounds = element.Bounds with
        {
            X = element.Bounds.X - viewport.ScrollOffset.X,
            Y = element.Bounds.Y - viewport.ScrollOffset.Y
        };
        var clipsX = viewport.ComputedStyle.OverflowX is "scroll" or "auto";
        var clipsY = viewport.ComputedStyle.OverflowY is "scroll" or "auto";
        if (clipsX &&
            (bounds.Right < viewport.Bounds.Left - viewport.Bounds.Width ||
             bounds.Left > viewport.Bounds.Right + viewport.Bounds.Width))
            return true;
        if (clipsY &&
            (bounds.Bottom < viewport.Bounds.Top - viewport.Bounds.Height ||
             bounds.Top > viewport.Bounds.Bottom + viewport.Bounds.Height))
            return true;
        return false;
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
