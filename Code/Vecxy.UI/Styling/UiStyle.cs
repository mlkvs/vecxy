using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace Vecxy.UI;

public enum EUiLengthUnit : byte
{
    Auto,
    Pixel,
    Percent
}

public readonly record struct UiLength(float Value, EUiLengthUnit Unit)
{
    public static UiLength Auto => new(0.0f, EUiLengthUnit.Auto);
    public static UiLength Pixels(float value) => new(value, EUiLengthUnit.Pixel);
    public static UiLength Percent(float value) => new(value, EUiLengthUnit.Percent);

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

public sealed class UiComputedStyle
{
    public string Display { get; set; } = "flex";
    public string Position { get; set; } = "relative";
    public string FlexDirection { get; set; } = "column";
    public string FlexWrap { get; set; } = "nowrap";
    public string JustifyContent { get; set; } = "flex-start";
    public string AlignItems { get; set; } = "stretch";
    public string AlignSelf { get; set; } = "auto";
    public string Overflow { get; set; } = "visible";
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
    public UiLength FlexBasis { get; set; } = UiLength.Auto;
    public float FlexGrow { get; set; }
    public float FlexShrink { get; set; }
    public float? AspectRatio { get; set; }
    public Vector4 Color { get; set; } = Vector4.One;
    public Vector4 BackgroundColor { get; set; } = Vector4.Zero;
    public Vector4 BorderColor { get; set; } = Vector4.Zero;
    public float BorderWidth { get; set; }
    public float BorderRadius { get; set; }
    public float FontSize { get; set; } = 16.0f;
    public string FontFamily { get; set; } = "Vecxy Fallback";
    public float Opacity { get; set; } = 1.0f;
    public int ZIndex { get; set; }
    public string? BackgroundImage { get; set; }
    public string ObjectFit { get; set; } = "fill";
    public Dictionary<string, string> Variables { get; } =
        new(StringComparer.Ordinal);

    internal static UiComputedStyle Inherit(UiComputedStyle? parent)
    {
        var style = new UiComputedStyle();
        if (parent is null)
            return style;

        style.Color = parent.Color;
        style.FontSize = parent.FontSize;
        style.FontFamily = parent.FontFamily;
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
    public IReadOnlyList<UiStyleRule> Rules { get; }
    public IReadOnlyList<UiFontFace> FontFaces { get; }

    private UiStyleSheet(
        IReadOnlyList<UiStyleRule> rules,
        IReadOnlyList<UiFontFace> fontFaces)
    {
        Rules = rules;
        FontFaces = fontFaces;
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

        return new UiStyleSheet(rules, fontFaces);
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
            if (_classes.Any(value => !element.Classes.Contains(value)))
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
                    "focus" or "focus-visible" => element.IsFocused,
                    "disabled" => element.IsDisabled,
                    "first-child" => element.Parent?.Children.FirstOrDefault() == element,
                    "last-child" => element.Parent?.Children.LastOrDefault() == element,
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

internal static class UiStyleResolver
{
    private static readonly Regex VariablePattern = new(
        @"var\(\s*(--[A-Za-z0-9_-]+)\s*(?:,\s*([^\)]+))?\)",
        RegexOptions.Compiled);

    public static void Resolve(UiElement root, IReadOnlyList<UiStyleSheet> sheets)
    {
        ResolveElement(root, null, sheets);
    }

    private static void ResolveElement(
        UiElement element,
        UiComputedStyle? parentStyle,
        IReadOnlyList<UiStyleSheet> sheets)
    {
        var style = UiComputedStyle.Inherit(parentStyle);
        var declarations = new Dictionary<string, Winner>(StringComparer.OrdinalIgnoreCase);

        foreach (var sheet in sheets)
        {
            foreach (var rule in sheet.Rules)
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
        foreach (var child in element.Children)
            ResolveElement(child, style, sheets);
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
            case "overflow":
            case "overflow-x":
            case "overflow-y": style.Overflow = value; break;
            case "pointer-events": style.PointerEvents = value; break;
            case "visibility": style.Visibility = value; break;
            case "width": SetLength(value, result => style.Width = result); break;
            case "height": SetLength(value, result => style.Height = result); break;
            case "min-width": SetLength(value, result => style.MinWidth = result); break;
            case "min-height": SetLength(value, result => style.MinHeight = result); break;
            case "max-width": SetLength(value, result => style.MaxWidth = result); break;
            case "max-height": SetLength(value, result => style.MaxHeight = result); break;
            case "gap": SetLength(value, result => style.Gap = result); break;
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
            case "border-width": SetPixels(value, result => style.BorderWidth = result); break;
            case "border-radius": SetPixels(value.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0], result => style.BorderRadius = result); break;
            case "border": ApplyBorder(style, value); break;
            case "font-size": SetPixels(value, result => style.FontSize = Math.Max(1.0f, result)); break;
            case "font-family":
                style.FontFamily = value.Split(',', 2)[0].Trim(' ', '\'', '"');
                break;
            case "opacity": SetFloat(value, result => style.Opacity = Math.Clamp(result, 0.0f, 1.0f)); break;
            case "z-index":
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var zIndex))
                    style.ZIndex = zIndex;
                break;
            case "background-image": style.BackgroundImage = ParseUrl(value); break;
            case "object-fit": style.ObjectFit = value.ToLowerInvariant(); break;
            case "flex": ApplyFlex(style, value); break;
        }
    }

    private static void ApplyFlex(UiComputedStyle style, string value)
    {
        var values = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (values.Length > 0 && TryFloat(values[0], out var grow))
            style.FlexGrow = grow;
        if (values.Length > 1 && TryFloat(values[1], out var shrink))
            style.FlexShrink = shrink;
        if (values.Length > 2 && UiLength.TryParse(values[2], out var basis))
            style.FlexBasis = basis;
    }

    private static void ApplyBorder(UiComputedStyle style, string value)
    {
        foreach (var part in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.EndsWith("px", StringComparison.OrdinalIgnoreCase))
                SetPixels(part, result => style.BorderWidth = result);
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
            var channels = rgba.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries);
            if (channels.Length is 3 or 4 &&
                TryFloat(channels[0], out var red) &&
                TryFloat(channels[1], out var green) &&
                TryFloat(channels[2], out var blue) &&
                (channels.Length == 3 || TryFloat(channels[3], out _)))
            {
                var alpha = channels.Length == 4
                    ? float.Parse(channels[3], CultureInfo.InvariantCulture)
                    : 1.0f;
                result = new Vector4(red / 255.0f, green / 255.0f, blue / 255.0f, alpha);
                return true;
            }
        }

        result = Vector4.Zero;
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
