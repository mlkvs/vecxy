using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Vecxy.UI;

public enum EUiLengthUnit : byte
{
    Auto,
    Pixel,
    Percent,
    Ui,
    ViewportWidth,
    ViewportHeight
}

public readonly record struct UiLength(float Value, EUiLengthUnit Unit)
{
    public static UiLength Auto => new(0.0f, EUiLengthUnit.Auto);
    public static UiLength Pixels(float value) => new(value, EUiLengthUnit.Pixel);
    public static UiLength Percent(float value) => new(value, EUiLengthUnit.Percent);
    public static UiLength Ui(float value) => new(value, EUiLengthUnit.Ui);

    public static bool TryParse(string source, out UiLength result)
    {
        source = source.Trim();
        if (string.Equals(source, "auto", StringComparison.OrdinalIgnoreCase))
        {
            result = Auto;
            return true;
        }

        var unit = EUiLengthUnit.Pixel;
        if (source.EndsWith('%'))
        {
            unit = EUiLengthUnit.Percent;
            source = source[..^1];
        }
        else if (source.EndsWith("ui", StringComparison.OrdinalIgnoreCase))
        {
            unit = EUiLengthUnit.Ui;
            source = source[..^2];
        }
        else if (source.EndsWith("vw", StringComparison.OrdinalIgnoreCase))
        {
            unit = EUiLengthUnit.ViewportWidth;
            source = source[..^2];
        }
        else if (source.EndsWith("vh", StringComparison.OrdinalIgnoreCase))
        {
            unit = EUiLengthUnit.ViewportHeight;
            source = source[..^2];
        }
        else if (source.EndsWith("px", StringComparison.OrdinalIgnoreCase))
        {
            source = source[..^2];
        }

        if (float.TryParse(
                source,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var value))
        {
            result = new UiLength(value, unit);
            return true;
        }

        result = Auto;
        return false;
    }
}

public readonly record struct UiEdges(
    UiLength Top,
    UiLength Right,
    UiLength Bottom,
    UiLength Left)
{
    public static UiEdges Zero { get; } = new(
        UiLength.Pixels(0),
        UiLength.Pixels(0),
        UiLength.Pixels(0),
        UiLength.Pixels(0));
}

public readonly record struct UiBoxShadow(
    UiLength OffsetX,
    UiLength OffsetY,
    UiLength BlurRadius,
    UiLength SpreadRadius,
    Vector4 Color,
    bool Inset);

public readonly record struct UiResolvedBoxShadow(
    float OffsetX,
    float OffsetY,
    float BlurRadius,
    float SpreadRadius,
    Vector4 Color,
    bool Inset);

public sealed class UiComputedStyle
{
    public string Display { get; set; } = "flex";
    public string Position { get; set; } = "relative";
    public string FlexDirection { get; set; } = "column";
    public string FlexWrap { get; set; } = "nowrap";
    public string JustifyContent { get; set; } = "flex-start";
    public string AlignItems { get; set; } = "stretch";
    public string AlignSelf { get; set; } = "auto";
    public string TextAlign { get; set; } = "left";
    public string VerticalAlign { get; set; } = "top";
    public string WhiteSpace { get; set; } = "nowrap";
    public string TextFit { get; set; } = "none";
    public string OverflowX { get; set; } = "visible";
    public string OverflowY { get; set; } = "visible";
    public string PointerEvents { get; set; } = "auto";
    public string Visibility { get; set; } = "visible";
    public UiLength Width { get; set; } = UiLength.Auto;
    public UiLength Height { get; set; } = UiLength.Auto;
    public UiLength MinWidth { get; set; } = UiLength.Auto;
    public UiLength MinHeight { get; set; } = UiLength.Auto;
    public UiLength MaxWidth { get; set; } = UiLength.Auto;
    public UiLength MaxHeight { get; set; } = UiLength.Auto;
    public UiEdges Margin { get; set; } = UiEdges.Zero;
    public UiEdges Padding { get; set; } = UiEdges.Zero;
    public UiEdges Inset { get; set; } = new(
        UiLength.Auto,
        UiLength.Auto,
        UiLength.Auto,
        UiLength.Auto);
    public UiLength Gap { get; set; } = UiLength.Pixels(0);
    public UiLength? RowGap { get; set; }
    public UiLength? ColumnGap { get; set; }
    public UiLength FlexBasis { get; set; } = UiLength.Auto;
    public float FlexGrow { get; set; }
    public float FlexShrink { get; set; }
    public float? AspectRatio { get; set; }
    public Vector4 Color { get; set; } = Vector4.One;
    public Vector4 BackgroundColor { get; set; } = Vector4.Zero;
    public Vector4 BorderColor { get; set; } = Vector4.Zero;
    public Vector4 PlaceholderColor { get; set; } = new(0.55f, 0.55f, 0.55f, 1.0f);
    public Vector4 SelectionBackgroundColor { get; set; } = new(0.23f, 0.46f, 0.84f, 0.75f);
    public Vector4 CaretColor { get; set; } = Vector4.One;
    public float CaretWidth { get; set; } = 2.0f;
    public UiLength CaretWidthLength { get; set; } = UiLength.Pixels(2.0f);
    public float BorderWidth { get; set; }
    public UiLength BorderWidthLength { get; set; } = UiLength.Pixels(0.0f);
    public float BorderRadius { get; set; }
    public UiLength BorderRadiusLength { get; set; } = UiLength.Pixels(0.0f);
    public IReadOnlyList<UiBoxShadow> BoxShadowDefinitions { get; set; } = [];
    public IReadOnlyList<UiResolvedBoxShadow> BoxShadows { get; set; } = [];
    public float FontSize { get; set; } = 16.0f;
    public UiLength FontSizeLength { get; set; } = UiLength.Ui(16.0f);
    public float MinFontSize { get; set; } = 1.0f;
    public UiLength MinFontSizeLength { get; set; } = UiLength.Pixels(1.0f);
    public string FontFamily { get; set; } = "Vecxy Fallback";
    public float Opacity { get; set; } = 1.0f;
    public int ZIndex { get; set; }
    public string? BackgroundImage { get; set; }
    public string BackgroundPattern { get; set; } = "none";
    public string BackgroundSize { get; set; } = "fill";
    public string BackgroundPosition { get; set; } = "center";
    public UiLength BackgroundSlice { get; set; } = UiLength.Pixels(0.0f);
    public Vector4 ImageTint { get; set; } = Vector4.One;
    internal UiTransformDefinition TransformDefinition { get; set; } = UiTransformDefinition.Identity;
    public UiTransform Transform { get; set; } = UiTransform.Identity;
    public Vector2 TransformOrigin { get; set; } = new(0.5f);
    internal IReadOnlyList<UiTransitionDefinition> Transitions { get; set; } = [];
    internal UiAnimationDefinition Animation { get; set; } = UiAnimationDefinition.None;
    public string ObjectFit { get; set; } = "fill";
    public string ImageRendering { get; set; } = "auto";
    public UiLength ScrollbarWidth { get; set; } = UiLength.Ui(8.0f);
    public Vector4 ScrollbarColor { get; set; } = new(1.0f, 1.0f, 1.0f, 0.55f);
    public Vector4 ScrollbarTrackColor { get; set; } = new(0.0f, 0.0f, 0.0f, 0.22f);
    public string GridTemplateColumns { get; set; } = string.Empty;
    public string GridTemplateRows { get; set; } = string.Empty;
    public string GridAutoColumns { get; set; } = string.Empty;
    public string GridAutoRows { get; set; } = string.Empty;
    public string GridColumnStart { get; set; } = "auto";
    public string GridColumnEnd { get; set; } = "auto";
    public string GridRowStart { get; set; } = "auto";
    public string GridRowEnd { get; set; } = "auto";
    public Dictionary<string, string> Variables { get; } =
        new(StringComparer.Ordinal);

    internal bool HasSameLayout(UiComputedStyle other) =>
        Display == other.Display && Position == other.Position &&
        FlexDirection == other.FlexDirection && FlexWrap == other.FlexWrap &&
        JustifyContent == other.JustifyContent && AlignItems == other.AlignItems &&
        AlignSelf == other.AlignSelf && WhiteSpace == other.WhiteSpace &&
        TextFit == other.TextFit && OverflowX == other.OverflowX && OverflowY == other.OverflowY &&
        Width == other.Width && Height == other.Height &&
        MinWidth == other.MinWidth && MinHeight == other.MinHeight &&
        MaxWidth == other.MaxWidth && MaxHeight == other.MaxHeight &&
        Margin == other.Margin && Padding == other.Padding && Inset == other.Inset &&
        Gap == other.Gap && RowGap == other.RowGap && ColumnGap == other.ColumnGap &&
        FlexBasis == other.FlexBasis && FlexGrow.Equals(other.FlexGrow) &&
        FlexShrink.Equals(other.FlexShrink) && AspectRatio == other.AspectRatio &&
        BorderWidthLength == other.BorderWidthLength && CaretWidthLength == other.CaretWidthLength && FontSizeLength == other.FontSizeLength &&
        MinFontSizeLength == other.MinFontSizeLength && FontFamily == other.FontFamily &&
        GridTemplateColumns == other.GridTemplateColumns && GridTemplateRows == other.GridTemplateRows &&
        GridAutoColumns == other.GridAutoColumns && GridAutoRows == other.GridAutoRows &&
        GridColumnStart == other.GridColumnStart && GridColumnEnd == other.GridColumnEnd &&
        GridRowStart == other.GridRowStart && GridRowEnd == other.GridRowEnd;

    internal bool HasSameSource(UiComputedStyle other) =>
        HasSameLayout(other) && TextAlign == other.TextAlign &&
        VerticalAlign == other.VerticalAlign && PointerEvents == other.PointerEvents &&
        Visibility == other.Visibility && Color == other.Color &&
        BackgroundColor == other.BackgroundColor && BorderColor == other.BorderColor &&
        PlaceholderColor == other.PlaceholderColor && SelectionBackgroundColor == other.SelectionBackgroundColor && CaretColor == other.CaretColor &&
        BorderRadiusLength == other.BorderRadiusLength &&
        BoxShadowDefinitions.SequenceEqual(other.BoxShadowDefinitions) &&
        Opacity.Equals(other.Opacity) && ZIndex == other.ZIndex &&
        BackgroundImage == other.BackgroundImage && BackgroundPattern == other.BackgroundPattern && BackgroundSize == other.BackgroundSize &&
        BackgroundPosition == other.BackgroundPosition && BackgroundSlice == other.BackgroundSlice &&
        ImageTint == other.ImageTint &&
        TransformDefinition == other.TransformDefinition && TransformOrigin == other.TransformOrigin &&
        Transitions.SequenceEqual(other.Transitions) && Animation == other.Animation &&
        ObjectFit == other.ObjectFit && ImageRendering == other.ImageRendering && ScrollbarWidth == other.ScrollbarWidth &&
        ScrollbarColor == other.ScrollbarColor && ScrollbarTrackColor == other.ScrollbarTrackColor &&
        Variables.Count == other.Variables.Count &&
        Variables.All(pair => other.Variables.TryGetValue(pair.Key, out var value) && value == pair.Value);

    internal bool HasSameSourceExceptComposite(UiComputedStyle other) =>
        HasSameLayout(other) && TextAlign == other.TextAlign &&
        VerticalAlign == other.VerticalAlign && PointerEvents == other.PointerEvents &&
        Color == other.Color && BackgroundColor == other.BackgroundColor &&
        PlaceholderColor == other.PlaceholderColor && SelectionBackgroundColor == other.SelectionBackgroundColor && CaretColor == other.CaretColor &&
        BorderColor == other.BorderColor && BorderRadiusLength == other.BorderRadiusLength &&
        BoxShadowDefinitions.SequenceEqual(other.BoxShadowDefinitions) &&
        ZIndex == other.ZIndex &&
        BackgroundImage == other.BackgroundImage && BackgroundPattern == other.BackgroundPattern && BackgroundSize == other.BackgroundSize &&
        BackgroundPosition == other.BackgroundPosition && BackgroundSlice == other.BackgroundSlice &&
        ImageTint == other.ImageTint &&
        Transitions.SequenceEqual(other.Transitions) && Animation == other.Animation &&
        ObjectFit == other.ObjectFit && ImageRendering == other.ImageRendering && ScrollbarWidth == other.ScrollbarWidth &&
        ScrollbarColor == other.ScrollbarColor && ScrollbarTrackColor == other.ScrollbarTrackColor &&
        Variables.Count == other.Variables.Count &&
        Variables.All(pair => other.Variables.TryGetValue(pair.Key, out var value) && value == pair.Value);

    internal bool HasSameInherited(UiComputedStyle other) =>
        Color == other.Color &&
        FontSizeLength == other.FontSizeLength &&
        FontFamily == other.FontFamily &&
        TextAlign == other.TextAlign &&
        WhiteSpace == other.WhiteSpace &&
        TextFit == other.TextFit &&
        MinFontSizeLength == other.MinFontSizeLength &&
        Variables.Count == other.Variables.Count &&
        Variables.All(pair => other.Variables.TryGetValue(pair.Key, out var value) && value == pair.Value);

    internal static UiComputedStyle Inherit(UiComputedStyle? parent)
    {
        var style = new UiComputedStyle();
        if (parent is null)
            return style;

        style.Color = parent.Color;
        style.FontSize = parent.FontSize;
        style.FontSizeLength = parent.FontSizeLength;
        style.FontFamily = parent.FontFamily;
        style.TextAlign = parent.TextAlign;
        style.WhiteSpace = parent.WhiteSpace;
        style.TextFit = parent.TextFit;
        style.MinFontSize = parent.MinFontSize;
        style.MinFontSizeLength = parent.MinFontSizeLength;
        foreach (var (name, value) in parent.Variables)
            style.Variables[name] = value;

        return style;
    }
}

internal sealed record UiStyleRule(
    UiSelector Selector,
    IReadOnlyDictionary<string, string> Declarations,
    int Order);

internal sealed class UiStyleSheet
{
    private static readonly IReadOnlyList<UiStyleRule> EmptyRules = Array.Empty<UiStyleRule>();
    private readonly Dictionary<string, IReadOnlyList<UiStyleRule>> _rulesById;
    private readonly Dictionary<string, IReadOnlyList<UiStyleRule>> _rulesByClass;
    private readonly Dictionary<string, IReadOnlyList<UiStyleRule>> _rulesByTag;
    private readonly IReadOnlyList<UiStyleRule> _universalRules;
    public IReadOnlyList<UiStyleRule> Rules { get; }
    public IReadOnlyList<UiFontFace> FontFaces { get; }
    public IReadOnlyDictionary<string, UiKeyframes> Keyframes { get; }

    private UiStyleSheet(
        IReadOnlyList<UiStyleRule> rules,
        IReadOnlyList<UiFontFace> fontFaces,
        IReadOnlyDictionary<string, UiKeyframes> keyframes)
    {
        Rules = rules;
        FontFaces = fontFaces;
        Keyframes = keyframes;
        (_rulesById, _rulesByClass, _rulesByTag, _universalRules) = BuildRuleIndex(rules);
    }

    internal IReadOnlyList<UiStyleRule> UniversalRules => _universalRules;

    internal IReadOnlyList<UiStyleRule> RulesForId(string? id) =>
        id is not null && _rulesById.TryGetValue(id, out var rules) ? rules : EmptyRules;

    internal IReadOnlyList<UiStyleRule> RulesForClass(string className) =>
        _rulesByClass.TryGetValue(className, out var rules) ? rules : EmptyRules;

    internal IReadOnlyList<UiStyleRule> RulesForTag(string tagName) =>
        _rulesByTag.TryGetValue(tagName, out var rules) ? rules : EmptyRules;

    internal int DescendantDependencySignature(UiElement element)
    {
        var signature = 0;
        for (var index = 0; index < Rules.Count; index++)
        {
            var match = Rules[index].Selector.AncestorMatchSignature(element);
            if (match != 0)
                signature = HashCode.Combine(signature, index, match);
        }
        return signature;
    }

    private static (
        Dictionary<string, IReadOnlyList<UiStyleRule>> ById,
        Dictionary<string, IReadOnlyList<UiStyleRule>> ByClass,
        Dictionary<string, IReadOnlyList<UiStyleRule>> ByTag,
        IReadOnlyList<UiStyleRule> Universal) BuildRuleIndex(IReadOnlyList<UiStyleRule> rules)
    {
        var byId = new Dictionary<string, List<UiStyleRule>>(StringComparer.Ordinal);
        var byClass = new Dictionary<string, List<UiStyleRule>>(StringComparer.Ordinal);
        var byTag = new Dictionary<string, List<UiStyleRule>>(StringComparer.OrdinalIgnoreCase);
        var universal = new List<UiStyleRule>();
        foreach (var rule in rules)
        {
            var key = rule.Selector.IndexKey;
            if (key.Id is { } id)
                Add(byId, id, rule);
            else if (key.Class is { } className)
                Add(byClass, className, rule);
            else if (key.Tag is { } tag)
                Add(byTag, tag, rule);
            else
                universal.Add(rule);
        }

        return (
            Freeze(byId, StringComparer.Ordinal),
            Freeze(byClass, StringComparer.Ordinal),
            Freeze(byTag, StringComparer.OrdinalIgnoreCase),
            universal.ToArray());
    }

    private static void Add(
        Dictionary<string, List<UiStyleRule>> index,
        string key,
        UiStyleRule rule)
    {
        if (!index.TryGetValue(key, out var values))
        {
            values = [];
            index.Add(key, values);
        }
        values.Add(rule);
    }

    private static Dictionary<string, IReadOnlyList<UiStyleRule>> Freeze(
        Dictionary<string, List<UiStyleRule>> source,
        StringComparer comparer)
    {
        var result = new Dictionary<string, IReadOnlyList<UiStyleRule>>(comparer);
        foreach (var (key, rules) in source)
            result.Add(key, rules.ToArray());
        return result;
    }

    public static UiStyleSheet Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        source = Regex.Replace(
            source,
            @"/\*.*?\*/",
            string.Empty,
            RegexOptions.Singleline);

        var rules = new List<UiStyleRule>();
        var fontFaces = new List<UiFontFace>();
        var keyframes = new Dictionary<string, UiKeyframes>(StringComparer.Ordinal);
        var order = 0;
        var cursor = 0;

        while (TryReadBlock(source, ref cursor, out var prelude, out var body))
        {
            prelude = prelude.Trim();
            if (prelude.Equals("@font-face", StringComparison.OrdinalIgnoreCase))
            {
                var font = ParseDeclarations(body);
                var family = font.GetValueOrDefault("font-family")?.Trim(' ', '\'', '"');
                var sourceMatch = Regex.Match(
                    font.GetValueOrDefault("src") ?? string.Empty,
                    "url\\(\\s*['\\\"]?([^'\\\"\\)]+)",
                    RegexOptions.IgnoreCase);
                if (!string.IsNullOrWhiteSpace(family) && sourceMatch.Success)
                    fontFaces.Add(new UiFontFace(family, sourceMatch.Groups[1].Value.Trim()));
                continue;
            }

            if (prelude.StartsWith("@keyframes ", StringComparison.OrdinalIgnoreCase))
            {
                var name = prelude[11..].Trim();
                var frames = ParseKeyframes(body);
                if (name.Length > 0 && frames.Count > 0)
                    keyframes[name] = new UiKeyframes(name, frames);
                continue;
            }

            if (prelude.Length == 0 || prelude.StartsWith('@'))
                continue;

            var declarations = ParseDeclarations(body);
            foreach (var selectorSource in SplitTopLevel(prelude, ','))
            {
                var selector = UiSelector.Parse(selectorSource.Trim());
                if (selector is not null)
                    rules.Add(new UiStyleRule(selector, declarations, order++));
            }
        }

        return new UiStyleSheet(rules, fontFaces, keyframes);
    }

    private static IReadOnlyList<UiKeyframe> ParseKeyframes(string source)
    {
        var result = new List<UiKeyframe>();
        var cursor = 0;
        while (TryReadBlock(source, ref cursor, out var prelude, out var body))
        {
            var declarations = ParseDeclarations(body);
            foreach (var selector in SplitTopLevel(prelude, ','))
            {
                var token = selector.Trim().ToLowerInvariant();
                float offset;
                if (token == "from") offset = 0.0f;
                else if (token == "to") offset = 1.0f;
                else if (token.EndsWith('%') &&
                         float.TryParse(token[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
                    offset = Math.Clamp(percent * 0.01f, 0.0f, 1.0f);
                else
                    continue;
                result.Add(new UiKeyframe(offset, UiKeyframeValues.Compile(declarations)));
            }
        }
        return result.OrderBy(frame => frame.Offset).ToArray();
    }

    internal static IReadOnlyDictionary<string, string> ParseDeclarations(
        string source)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var declaration in SplitTopLevel(source, ';'))
        {
            var colon = FindTopLevel(declaration, ':');
            if (colon <= 0)
                continue;

            var name = declaration[..colon].Trim();
            var value = declaration[(colon + 1)..].Trim();
            if (name.Length == 0 || value.Length == 0)
                continue;

            if (value.EndsWith("!important", StringComparison.OrdinalIgnoreCase))
                value = value[..^10].TrimEnd();

            result[name] = value;
        }

        return result;
    }

    private static bool TryReadBlock(
        string source,
        ref int cursor,
        out string prelude,
        out string body)
    {
        while (cursor < source.Length && char.IsWhiteSpace(source[cursor]))
            cursor++;

        var open = source.IndexOf('{', cursor);
        if (open < 0)
        {
            prelude = string.Empty;
            body = string.Empty;
            return false;
        }

        var depth = 1;
        var quote = '\0';
        var index = open + 1;
        for (; index < source.Length; index++)
        {
            var character = source[index];
            if (quote != '\0')
            {
                if (character == quote && source[index - 1] != '\\')
                    quote = '\0';
                continue;
            }

            if (character is '\'' or '"')
            {
                quote = character;
                continue;
            }

            if (character == '{')
                depth++;
            else if (character == '}' && --depth == 0)
                break;
        }

        if (depth != 0)
        {
            prelude = string.Empty;
            body = string.Empty;
            cursor = source.Length;
            return false;
        }

        prelude = source[cursor..open];
        body = source[(open + 1)..index];
        cursor = index + 1;
        return true;
    }

    internal static IEnumerable<string> SplitTopLevel(string source, char separator)
    {
        var start = 0;
        var depth = 0;
        var quote = '\0';
        for (var index = 0; index < source.Length; index++)
        {
            var character = source[index];
            if (quote != '\0')
            {
                if (character == quote && (index == 0 || source[index - 1] != '\\'))
                    quote = '\0';
                continue;
            }

            if (character is '\'' or '"')
                quote = character;
            else if (character == '(' || character == '[')
                depth++;
            else if (character == ')' || character == ']')
                depth = Math.Max(0, depth - 1);
            else if (character == separator && depth == 0)
            {
                yield return source[start..index];
                start = index + 1;
            }
        }

        yield return source[start..];
    }

    private static int FindTopLevel(string source, char target)
    {
        var depth = 0;
        var quote = '\0';
        for (var index = 0; index < source.Length; index++)
        {
            var character = source[index];
            if (quote != '\0')
            {
                if (character == quote && (index == 0 || source[index - 1] != '\\'))
                    quote = '\0';
                continue;
            }

            if (character is '\'' or '"')
                quote = character;
            else if (character is '(' or '[')
                depth++;
            else if (character is ')' or ']')
                depth--;
            else if (character == target && depth == 0)
                return index;
        }

        return -1;
    }
}

internal enum EUiCombinator : byte
{
    Descendant,
    Child
}

internal sealed class UiSelector
{
    private readonly IReadOnlyList<Part> _parts;
    public int Specificity { get; }
    internal UiSelectorIndexKey IndexKey => _parts[^1].Compound.IndexKey;

    private UiSelector(IReadOnlyList<Part> parts, int specificity)
    {
        _parts = parts;
        Specificity = specificity;
    }

    public static UiSelector? Parse(string source)
    {
        if (source.Length == 0 || source.Contains("::", StringComparison.Ordinal))
            return null;

        var tokens = new List<(string Source, EUiCombinator Before)>();
        var builder = new StringBuilder();
        var nextCombinator = EUiCombinator.Descendant;
        var bracketDepth = 0;

        void Flush()
        {
            if (builder.Length == 0)
                return;
            tokens.Add((builder.ToString(), nextCombinator));
            builder.Clear();
            nextCombinator = EUiCombinator.Descendant;
        }

        for (var index = 0; index < source.Length; index++)
        {
            var character = source[index];
            if (character is '[' or '(')
                bracketDepth++;
            else if (character is ']' or ')')
                bracketDepth--;

            if (bracketDepth == 0 && character == '>')
            {
                Flush();
                nextCombinator = EUiCombinator.Child;
                continue;
            }

            if (bracketDepth == 0 && char.IsWhiteSpace(character))
            {
                Flush();
                continue;
            }

            builder.Append(character);
        }

        Flush();
        if (tokens.Count == 0)
            return null;

        var parts = new List<Part>(tokens.Count);
        var specificity = 0;
        foreach (var token in tokens)
        {
            var compound = Compound.Parse(token.Source);
            if (compound is null)
                return null;
            specificity += compound.Specificity;
            parts.Add(new Part(compound, token.Before));
        }

        return new UiSelector(parts, specificity);
    }

    public bool Matches(UiElement element) => Matches(element, _parts.Count - 1);

    internal int AncestorMatchSignature(UiElement element)
    {
        var signature = 0;
        for (var index = 0; index < _parts.Count - 1; index++)
            if (_parts[index].Compound.Matches(element))
                signature = HashCode.Combine(signature, index + 1);
        return signature;
    }

    private bool Matches(UiElement? element, int partIndex)
    {
        if (element is null || !_parts[partIndex].Compound.Matches(element))
            return false;
        if (partIndex == 0)
            return true;

        if (_parts[partIndex].Before == EUiCombinator.Child)
            return Matches(element.Parent, partIndex - 1);

        for (var ancestor = element.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (Matches(ancestor, partIndex - 1))
                return true;
        }

        return false;
    }

    private sealed record Part(Compound Compound, EUiCombinator Before);

    private sealed class Compound
    {
        private readonly string? _tag;
        private readonly string? _id;
        private readonly string[] _classes;
        private readonly string[] _pseudos;
        private readonly (string Name, string? Value)[] _attributes;
        public int Specificity { get; }
        public UiSelectorIndexKey IndexKey => new(
            _id,
            _classes.FirstOrDefault(),
            _tag is null or "*" ? null : _tag);

        private Compound(
            string? tag,
            string? id,
            string[] classes,
            string[] pseudos,
            (string, string?)[] attributes)
        {
            _tag = tag;
            _id = id;
            _classes = classes;
            _pseudos = pseudos;
            _attributes = attributes;
            Specificity = (id is null ? 0 : 100) +
                          (classes.Length + pseudos.Length + attributes.Length) * 10 +
                          (tag is null || tag == "*" ? 0 : 1);
        }

        public static Compound? Parse(string source)
        {
            var tagEnd = source.IndexOfAny(['.', '#', ':', '[']);
            var tag = tagEnd < 0 ? source : source[..tagEnd];
            if (tagEnd < 0)
                tagEnd = source.Length;
            if (tag.Length == 0)
                tag = null;

            string? id = null;
            var classes = new List<string>();
            var pseudos = new List<string>();
            var attributes = new List<(string, string?)>();

            var cursor = tagEnd;
            while (cursor < source.Length)
            {
                var prefix = source[cursor++];
                if (prefix == '[')
                {
                    var end = source.IndexOf(']', cursor);
                    if (end < 0)
                        return null;
                    var content = source[cursor..end];
                    var equals = content.IndexOf('=');
                    attributes.Add(equals < 0
                        ? (content.Trim(), null)
                        : (content[..equals].Trim(), content[(equals + 1)..].Trim(' ', '\'', '"')));
                    cursor = end + 1;
                    continue;
                }

                var endIndex = cursor;
                while (endIndex < source.Length &&
                       source[endIndex] is not ('.' or '#' or ':' or '['))
                    endIndex++;
                var value = source[cursor..endIndex];
                if (value.Contains('('))
                    return null;
                if (prefix == '.')
                    classes.Add(value);
                else if (prefix == '#')
                    id = value;
                else if (prefix == ':')
                    pseudos.Add(value);
                cursor = endIndex;
            }

            return new Compound(tag, id, classes.ToArray(), pseudos.ToArray(), attributes.ToArray());
        }

        public bool Matches(UiElement element)
        {
            if (_tag is not null && _tag != "*" &&
                !string.Equals(_tag, element.TagName, StringComparison.OrdinalIgnoreCase))
                return false;
            if (_id is not null && !string.Equals(_id, element.Id, StringComparison.Ordinal))
                return false;
            for (var index = 0; index < _classes.Length; index++)
                if (!element.Classes.Contains(_classes[index]))
                    return false;
            foreach (var (name, value) in _attributes)
            {
                if (!element.Attributes.TryGetValue(name, out var actual) ||
                    value is not null && !string.Equals(value, actual, StringComparison.Ordinal))
                    return false;
            }

            foreach (var pseudo in _pseudos)
            {
                var matches = pseudo switch
                {
                    "root" => element.Parent is null,
                    "hover" => element.IsHovered,
                    "active" => element.IsActive,
                    "focus" => element.IsFocused,
                    "focus-visible" => element.IsFocused && element.IsFocusVisible,
                    "disabled" => element.IsDisabled,
                    "checked" => element.IsChecked,
                    "selected" => element.IsSelected,
                    "dragging" => element.IsDragging,
                    "drop-target" => element.IsDropTarget,
                    "first-child" => element.Parent is { Children.Count: > 0 } firstParent &&
                                     ReferenceEquals(firstParent.Children[0], element),
                    "last-child" => element.Parent is { Children.Count: > 0 } lastParent &&
                                    ReferenceEquals(lastParent.Children[^1], element),
                    "empty" => element.Children.Count == 0 && string.IsNullOrWhiteSpace(element.Text),
                    _ => false
                };
                if (!matches)
                    return false;
            }

            return true;
        }
    }
}

internal readonly record struct UiSelectorIndexKey(string? Id, string? Class, string? Tag);

internal static class UiStyleResolver
{
    private static readonly ConditionalWeakTable<UiElement, ResolutionState> ResolutionStates = new();
    private static readonly Regex VariablePattern = new(
        @"var\(\s*(--[A-Za-z0-9_-]+)\s*(?:,\s*([^\)]+))?\)",
        RegexOptions.Compiled);

    public static int Resolve(
        UiElement root,
        IReadOnlyList<UiStyleSheet> sheets,
        bool forceFullResolution = false)
    {
        var resolvedElements = 0;
        ResolveElement(root, null, sheets, forceFullResolution, ref resolvedElements);
        return resolvedElements;
    }

    private static void ResolveElement(
        UiElement element,
        UiComputedStyle? parentStyle,
        IReadOnlyList<UiStyleSheet> sheets,
        bool forceResolution,
        ref int resolvedElements)
    {
        var state = ResolutionStates.GetOrCreateValue(element);
        var parentComputedStyleVersion = element.Parent?.InheritedStyleVersion ?? -1;
        var localStateChanged =
            state.LocalStyleVersion != element.LocalStyleVersion ||
            state.LocalPseudoVersion != element.LocalPseudoVersion ||
            state.ParentComputedStyleVersion != parentComputedStyleVersion ||
            !ReferenceEquals(state.Parent, element.Parent);
        var subtreeChanged =
            state.SubtreeStyleVersion != element.StyleVersion ||
            state.SubtreePseudoVersion != element.PseudoVersion;

        if (!forceResolution && !localStateChanged && !subtreeChanged)
            return;

        var resolveThisElement = forceResolution || localStateChanged;
        var descendantDependencySignature = state.DescendantDependencySignature;
        if (localStateChanged || forceResolution)
        {
            descendantDependencySignature = 0;
            for (var index = 0; index < sheets.Count; index++)
                descendantDependencySignature = HashCode.Combine(
                    descendantDependencySignature,
                    sheets[index].DescendantDependencySignature(element));
        }
        var descendantSelectorsChanged =
            state.DescendantDependencySignature != descendantDependencySignature;
        if (resolveThisElement)
        {
            resolvedElements++;
            var style = UiComputedStyle.Inherit(parentStyle);
            var declarations = new Dictionary<string, Winner>(StringComparer.OrdinalIgnoreCase);

            foreach (var sheet in sheets)
            {
                ApplyMatchingRules(element, sheet.UniversalRules, declarations);
                ApplyMatchingRules(element, sheet.RulesForId(element.Id), declarations);
                foreach (var className in element.Classes)
                    ApplyMatchingRules(element, sheet.RulesForClass(className), declarations);
                ApplyMatchingRules(element, sheet.RulesForTag(element.TagName), declarations);
            }

            if (element.Attributes.TryGetValue("style", out var inlineStyle))
            {
                foreach (var (name, value) in UiStyleSheet.ParseDeclarations(inlineStyle))
                    declarations[name] = new Winner(1000, int.MaxValue, value);
            }

            foreach (var (name, winner) in declarations)
            {
                if (name.StartsWith("--", StringComparison.Ordinal))
                    style.Variables[name] = winner.Value;
            }

            foreach (var (name, winner) in declarations)
            {
                if (!name.StartsWith("--", StringComparison.Ordinal))
                    Apply(style, name, ResolveVariables(winner.Value, style.Variables));
            }

            element.ComputedStyle = style;
        }

        for (var index = 0; index < element.Children.Count; index++)
            ResolveElement(
                element.Children[index],
                element.ComputedStyle,
                sheets,
                forceResolution || descendantSelectorsChanged,
                ref resolvedElements);

        state.LocalStyleVersion = element.LocalStyleVersion;
        state.LocalPseudoVersion = element.LocalPseudoVersion;
        state.SubtreeStyleVersion = element.StyleVersion;
        state.SubtreePseudoVersion = element.PseudoVersion;
        state.ParentComputedStyleVersion = parentComputedStyleVersion;
        state.Parent = element.Parent;
        state.DescendantDependencySignature = descendantDependencySignature;
    }

    private static void ApplyMatchingRules(
        UiElement element,
        IReadOnlyList<UiStyleRule> rules,
        Dictionary<string, Winner> declarations)
    {
        foreach (var rule in rules)
        {
            if (!rule.Selector.Matches(element))
                continue;
            foreach (var (name, value) in rule.Declarations)
            {
                var candidate = new Winner(rule.Selector.Specificity, rule.Order, value);
                if (!declarations.TryGetValue(name, out var winner) ||
                    candidate.Specificity > winner.Specificity ||
                    candidate.Specificity == winner.Specificity && candidate.Order >= winner.Order)
                    declarations[name] = candidate;
            }
        }
    }

    private sealed class ResolutionState
    {
        public int LocalStyleVersion = int.MinValue;
        public int LocalPseudoVersion = int.MinValue;
        public int SubtreeStyleVersion = int.MinValue;
        public int SubtreePseudoVersion = int.MinValue;
        public int ParentComputedStyleVersion = int.MinValue;
        public int DescendantDependencySignature = int.MinValue;
        public UiElement? Parent;
    }

    private static string ResolveVariables(
        string source,
        IReadOnlyDictionary<string, string> variables)
    {
        for (var iteration = 0; iteration < 8; iteration++)
        {
            var changed = false;
            source = VariablePattern.Replace(source, match =>
            {
                changed = true;
                return variables.TryGetValue(match.Groups[1].Value, out var value)
                    ? value
                    : match.Groups[2].Success
                        ? match.Groups[2].Value.Trim()
                        : string.Empty;
            });
            if (!changed)
                break;
        }

        return source;
    }

    private static void Apply(UiComputedStyle style, string name, string value)
    {
        switch (name.ToLowerInvariant())
        {
            case "display": style.Display = value; break;
            case "position": style.Position = value; break;
            case "flex-direction": style.FlexDirection = value; break;
            case "flex-wrap": style.FlexWrap = value; break;
            case "justify-content": style.JustifyContent = value; break;
            case "align-items": style.AlignItems = value; break;
            case "align-self": style.AlignSelf = value; break;
            case "text-align": style.TextAlign = value.ToLowerInvariant(); break;
            case "vertical-align": style.VerticalAlign = value.ToLowerInvariant(); break;
            case "white-space": style.WhiteSpace = value.ToLowerInvariant(); break;
            case "text-fit": style.TextFit = value.ToLowerInvariant(); break;
            case "place-content": ApplyPlaceContent(style, value); break;
            case "overflow": style.OverflowX = style.OverflowY = value.ToLowerInvariant(); break;
            case "overflow-x": style.OverflowX = value.ToLowerInvariant(); break;
            case "overflow-y": style.OverflowY = value.ToLowerInvariant(); break;
            case "pointer-events": style.PointerEvents = value; break;
            case "visibility": style.Visibility = value; break;
            case "width": SetLength(value, result => style.Width = result); break;
            case "height": SetLength(value, result => style.Height = result); break;
            case "min-width": SetLength(value, result => style.MinWidth = result); break;
            case "min-height": SetLength(value, result => style.MinHeight = result); break;
            case "max-width": SetLength(value, result => style.MaxWidth = result); break;
            case "max-height": SetLength(value, result => style.MaxHeight = result); break;
            case "gap": SetLength(value, result => style.Gap = result); break;
            case "row-gap": SetLength(value, result => style.RowGap = result); break;
            case "column-gap": SetLength(value, result => style.ColumnGap = result); break;
            case "flex-grow": SetFloat(value, result => style.FlexGrow = result); break;
            case "flex-shrink": SetFloat(value, result => style.FlexShrink = result); break;
            case "flex-basis": SetLength(value, result => style.FlexBasis = result); break;
            case "aspect-ratio":
                var ratio = value.Split('/', StringSplitOptions.TrimEntries);
                if (ratio.Length == 1 && TryFloat(ratio[0], out var aspect))
                    style.AspectRatio = aspect;
                else if (ratio.Length == 2 && TryFloat(ratio[0], out var a) &&
                         TryFloat(ratio[1], out var b) && b != 0.0f)
                    style.AspectRatio = a / b;
                break;
            case "margin": style.Margin = ParseEdges(value, true); break;
            case "padding": style.Padding = ParseEdges(value, false); break;
            case "inset": style.Inset = ParseEdges(value, true); break;
            case "margin-top": SetEdge(value, edge => style.Margin = style.Margin with { Top = edge }); break;
            case "margin-right": SetEdge(value, edge => style.Margin = style.Margin with { Right = edge }); break;
            case "margin-bottom": SetEdge(value, edge => style.Margin = style.Margin with { Bottom = edge }); break;
            case "margin-left": SetEdge(value, edge => style.Margin = style.Margin with { Left = edge }); break;
            case "padding-top": SetEdge(value, edge => style.Padding = style.Padding with { Top = edge }); break;
            case "padding-right": SetEdge(value, edge => style.Padding = style.Padding with { Right = edge }); break;
            case "padding-bottom": SetEdge(value, edge => style.Padding = style.Padding with { Bottom = edge }); break;
            case "padding-left": SetEdge(value, edge => style.Padding = style.Padding with { Left = edge }); break;
            case "top": SetEdge(value, edge => style.Inset = style.Inset with { Top = edge }); break;
            case "right": SetEdge(value, edge => style.Inset = style.Inset with { Right = edge }); break;
            case "bottom": SetEdge(value, edge => style.Inset = style.Inset with { Bottom = edge }); break;
            case "left": SetEdge(value, edge => style.Inset = style.Inset with { Left = edge }); break;
            case "color": SetColor(value, result => style.Color = result); break;
            case "background-color": SetColor(value, result => style.BackgroundColor = result); break;
            case "background":
                SetColor(value, result => style.BackgroundColor = result);
                break;
            case "border-color": SetColor(value, result => style.BorderColor = result); break;
            case "placeholder-color": SetColor(value, result => style.PlaceholderColor = result); break;
            case "selection-background-color": SetColor(value, result => style.SelectionBackgroundColor = result); break;
            case "caret-color": SetColor(value, result => style.CaretColor = result); break;
            case "caret-width": SetLength(value, result => style.CaretWidthLength = result); break;
            case "border-width": SetLength(value, result => style.BorderWidthLength = result); break;
            case "border-radius": SetLength(value.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0], result => style.BorderRadiusLength = result); break;
            case "border": ApplyBorder(style, value); break;
            case "box-shadow": style.BoxShadowDefinitions = ParseBoxShadows(value); break;
            case "font-size": SetLength(value, result => style.FontSizeLength = result); break;
            case "min-font-size": SetLength(value, result => style.MinFontSizeLength = result); break;
            case "font-family":
                style.FontFamily = value.Split(',', 2)[0].Trim(' ', '\'', '"');
                break;
            case "opacity": SetFloat(value, result => style.Opacity = Math.Clamp(result, 0.0f, 1.0f)); break;
            case "z-index":
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var zIndex))
                    style.ZIndex = zIndex;
                break;
            case "background-image": style.BackgroundImage = value.Trim(); break;
            case "background-pattern": style.BackgroundPattern = value.Trim().ToLowerInvariant(); break;
            case "background-size": style.BackgroundSize = value.ToLowerInvariant(); break;
            case "background-position": style.BackgroundPosition = value.ToLowerInvariant(); break;
            case "background-slice": SetLength(value, result => style.BackgroundSlice = result); break;
            case "image-tint": SetColor(value, result => style.ImageTint = result); break;
            case "transform": style.TransformDefinition = UiTransformParser.Parse(value, style.TransformOrigin); break;
            case "transform-origin":
                style.TransformOrigin = UiTransformParser.ParseOrigin(value);
                style.TransformDefinition = style.TransformDefinition with { Origin = style.TransformOrigin };
                break;
            case "transition": style.Transitions = UiAnimationParser.ParseTransitions(value); break;
            case "animation": style.Animation = UiAnimationParser.ParseAnimation(value); break;
            case "object-fit": style.ObjectFit = value.ToLowerInvariant(); break;
            case "image-rendering": style.ImageRendering = value.ToLowerInvariant(); break;
            case "scrollbar-width": SetLength(value, result => style.ScrollbarWidth = result); break;
            case "scrollbar-color":
            {
                var colors = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (colors.Length > 0 && TryColor(colors[0], out var thumb))
                    style.ScrollbarColor = thumb;
                if (colors.Length > 1 && TryColor(colors[1], out var track))
                    style.ScrollbarTrackColor = track;
                break;
            }
            case "grid-template-columns": style.GridTemplateColumns = value; break;
            case "grid-template-rows": style.GridTemplateRows = value; break;
            case "grid-auto-columns": style.GridAutoColumns = value; break;
            case "grid-auto-rows": style.GridAutoRows = value; break;
            case "grid-column-start": style.GridColumnStart = value; break;
            case "grid-column-end": style.GridColumnEnd = value; break;
            case "grid-row-start": style.GridRowStart = value; break;
            case "grid-row-end": style.GridRowEnd = value; break;
            case "grid-column":
                (style.GridColumnStart, style.GridColumnEnd) = UiGridLayout.ParsePlacement(value);
                break;
            case "grid-row":
                (style.GridRowStart, style.GridRowEnd) = UiGridLayout.ParsePlacement(value);
                break;
            case "flex": ApplyFlex(style, value); break;
        }
    }

    private static void ApplyPlaceContent(UiComputedStyle style, string value)
    {
        var values = value.ToLowerInvariant().Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (values.Length == 0)
            return;

        var vertical = values[0];
        var horizontal = values.Length > 1 ? values[1] : vertical;
        style.AlignItems = vertical;
        style.JustifyContent = horizontal;
        style.VerticalAlign = vertical switch
        {
            "center" => "middle",
            "end" or "flex-end" => "bottom",
            _ => "top"
        };
        style.TextAlign = horizontal switch
        {
            "center" => "center",
            "end" or "flex-end" => "right",
            _ => "left"
        };
    }

    private static IReadOnlyList<UiBoxShadow> ParseBoxShadows(string value)
    {
        if (value.Trim().Equals("none", StringComparison.OrdinalIgnoreCase))
            return [];

        var result = new List<UiBoxShadow>();
        foreach (var item in UiStyleSheet.SplitTopLevel(value, ','))
        {
            var inset = false;
            Vector4? color = null;
            var lengths = new List<UiLength>(4);
            foreach (var token in SplitWhitespaceTopLevel(item))
            {
                if (token.Equals("inset", StringComparison.OrdinalIgnoreCase))
                {
                    inset = true;
                    continue;
                }
                if (TryColor(token, out var parsedColor))
                {
                    color = parsedColor;
                    continue;
                }
                if (UiLength.TryParse(token, out var length) &&
                    length.Unit is not (EUiLengthUnit.Auto or EUiLengthUnit.Percent))
                    lengths.Add(length);
            }

            if (lengths.Count < 2)
                continue;
            result.Add(new UiBoxShadow(
                lengths[0],
                lengths[1],
                lengths.Count > 2 ? lengths[2] : UiLength.Pixels(0),
                lengths.Count > 3 ? lengths[3] : UiLength.Pixels(0),
                color ?? new Vector4(0, 0, 0, 0.5f),
                inset));
        }
        return result;
    }

    private static IEnumerable<string> SplitWhitespaceTopLevel(string source)
    {
        var start = -1;
        var depth = 0;
        for (var index = 0; index < source.Length; index++)
        {
            var character = source[index];
            if (character == '(')
                depth++;
            else if (character == ')')
                depth = Math.Max(0, depth - 1);
            else if (char.IsWhiteSpace(character) && depth == 0)
            {
                if (start >= 0)
                {
                    yield return source[start..index];
                    start = -1;
                }
                continue;
            }
            if (start < 0)
                start = index;
        }
        if (start >= 0)
            yield return source[start..];
    }

    private static void ApplyFlex(UiComputedStyle style, string value)
    {
        var values = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (values.Length == 1 && values[0].Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            style.FlexGrow = 0.0f;
            style.FlexShrink = 0.0f;
            style.FlexBasis = UiLength.Auto;
            return;
        }
        if (values.Length == 1 && values[0].Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            style.FlexGrow = 1.0f;
            style.FlexShrink = 1.0f;
            style.FlexBasis = UiLength.Auto;
            return;
        }
        if (values.Length > 0 && TryFloat(values[0], out var grow))
        {
            style.FlexGrow = grow;
            // CSS defines `flex: <number>` as `<number> 1 0`.
            if (values.Length == 1)
            {
                style.FlexShrink = 1.0f;
                style.FlexBasis = UiLength.Pixels(0.0f);
            }
        }
        if (values.Length > 1 && TryFloat(values[1], out var shrink))
            style.FlexShrink = shrink;
        if (values.Length == 2)
            style.FlexBasis = UiLength.Pixels(0.0f);
        else if (values.Length > 2 && UiLength.TryParse(values[2], out var basis))
            style.FlexBasis = basis;
    }

    private static void ApplyBorder(UiComputedStyle style, string value)
    {
        foreach (var part in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (UiLength.TryParse(part, out var width) && width.Unit is not (EUiLengthUnit.Auto or EUiLengthUnit.Percent))
                style.BorderWidthLength = width;
            else if (TryColor(part, out var color))
                style.BorderColor = color;
        }
    }

    private static UiEdges ParseEdges(string value, bool allowAuto)
    {
        var values = value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(source => UiLength.TryParse(source, out var result) &&
                              (allowAuto || result.Unit != EUiLengthUnit.Auto)
                ? result
                : UiLength.Pixels(0))
            .ToArray();
        return values.Length switch
        {
            1 => new UiEdges(values[0], values[0], values[0], values[0]),
            2 => new UiEdges(values[0], values[1], values[0], values[1]),
            3 => new UiEdges(values[0], values[1], values[2], values[1]),
            >= 4 => new UiEdges(values[0], values[1], values[2], values[3]),
            _ => UiEdges.Zero
        };
    }

    private static void SetLength(string value, Action<UiLength> setter)
    {
        if (UiLength.TryParse(value, out var result))
            setter(result);
    }

    private static void SetEdge(string value, Action<UiLength> setter) => SetLength(value, setter);

    private static void SetPixels(string value, Action<float> setter)
    {
        if (UiLength.TryParse(value, out var result) && result.Unit == EUiLengthUnit.Pixel)
            setter(result.Value);
    }

    private static void SetFloat(string value, Action<float> setter)
    {
        if (TryFloat(value, out var result))
            setter(result);
    }

    private static bool TryFloat(string value, out float result) =>
        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);

    private static void SetColor(string value, Action<Vector4> setter)
    {
        if (TryColor(value, out var result))
            setter(result);
    }

    internal static bool TryColor(string source, out Vector4 result)
    {
        source = source.Trim();
        if (source.Equals("transparent", StringComparison.OrdinalIgnoreCase))
        {
            result = Vector4.Zero;
            return true;
        }

        if (source.Equals("white", StringComparison.OrdinalIgnoreCase))
        {
            result = Vector4.One;
            return true;
        }

        if (source.Equals("black", StringComparison.OrdinalIgnoreCase))
        {
            result = new Vector4(0, 0, 0, 1);
            return true;
        }

        if (source.StartsWith('#'))
        {
            var hex = source[1..];
            if (hex.Length is 3 or 4)
                hex = string.Concat(hex.Select(character => new string(character, 2)));
            if (hex.Length is 6 or 8 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var packed))
            {
                if (hex.Length == 6)
                    packed = packed << 8 | 0xff;
                result = new Vector4(
                    (packed >> 24 & 0xff) / 255.0f,
                    (packed >> 16 & 0xff) / 255.0f,
                    (packed >> 8 & 0xff) / 255.0f,
                    (packed & 0xff) / 255.0f);
                return true;
            }
        }

        var rgba = Regex.Match(source, @"rgba?\(([^\)]+)\)", RegexOptions.IgnoreCase);
        if (rgba.Success)
        {
            var body = rgba.Groups[1].Value;
            string[] channels;
            string? alphaSource;
            if (body.Contains(','))
            {
                var commaSeparated = body.Split(',', StringSplitOptions.TrimEntries);
                channels = commaSeparated.Take(3).ToArray();
                alphaSource = commaSeparated.Length == 4 ? commaSeparated[3] : null;
            }
            else
            {
                var slashSeparated = body.Split('/', 2, StringSplitOptions.TrimEntries);
                channels = slashSeparated[0].Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                alphaSource = slashSeparated.Length == 2 ? slashSeparated[1] : null;
            }

            if (channels.Length == 3 &&
                TryRgbChannel(channels[0], out var red) &&
                TryRgbChannel(channels[1], out var green) &&
                TryRgbChannel(channels[2], out var blue) &&
                (alphaSource is null || TryAlphaChannel(alphaSource, out _)))
            {
                var alpha = alphaSource is not null && TryAlphaChannel(alphaSource, out var parsedAlpha)
                    ? parsedAlpha
                    : 1.0f;
                result = new Vector4(red, green, blue, alpha);
                return true;
            }
        }

        result = Vector4.Zero;
        return false;
    }

    private static bool TryRgbChannel(string source, out float value)
    {
        var percentage = source.EndsWith('%');
        if (percentage)
            source = source[..^1];
        if (TryFloat(source, out var parsed))
        {
            value = Math.Clamp(parsed / (percentage ? 100.0f : 255.0f), 0.0f, 1.0f);
            return true;
        }
        value = 0.0f;
        return false;
    }

    private static bool TryAlphaChannel(string source, out float value)
    {
        var percentage = source.EndsWith('%');
        if (percentage)
            source = source[..^1];
        if (TryFloat(source, out var parsed))
        {
            value = Math.Clamp(parsed / (percentage ? 100.0f : 1.0f), 0.0f, 1.0f);
            return true;
        }
        value = 0.0f;
        return false;
    }

    private static string? ParseUrl(string value)
    {
        var match = Regex.Match(
            value,
            "url\\(\\s*['\\\"]?([^'\\\"\\)]+)",
            RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private readonly record struct Winner(int Specificity, int Order, string Value);
}

internal readonly record struct UiFontFace(string Family, string Source);
