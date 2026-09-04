using System.Globalization;
using System.Text;

namespace Vecxy.UI;

/// <summary>Unicode-safe caret and word boundary helpers using UTF-16 string indices.</summary>
public static class TextNavigation
{
    public static int ClampBoundary(string text, int index)
    {
        index = Math.Clamp(index, 0, text.Length);
        if (index == 0 || index == text.Length) return index;
        var boundaries = StringInfo.ParseCombiningCharacters(text);
        var position = Array.BinarySearch(boundaries, index);
        return position >= 0 ? boundaries[position] : boundaries[Math.Max(0, ~position - 1)];
    }

    public static int Previous(string text, int index)
    {
        index = ClampBoundary(text, index);
        if (index == 0) return 0;
        var boundaries = StringInfo.ParseCombiningCharacters(text);
        var position = Array.BinarySearch(boundaries, index);
        return position > 0 ? boundaries[position - 1] : position == 0 ? 0 : boundaries[Math.Max(0, ~position - 1)];
    }

    public static int Next(string text, int index)
    {
        index = ClampBoundary(text, index);
        if (index >= text.Length) return text.Length;
        var boundaries = StringInfo.ParseCombiningCharacters(text);
        var position = Array.BinarySearch(boundaries, index);
        var next = position >= 0 ? position + 1 : ~position;
        return next < boundaries.Length ? boundaries[next] : text.Length;
    }

    public static int WordLeft(string text, int index)
    {
        index = Previous(text, index);
        while (index > 0 && !IsWordAt(text, index)) index = Previous(text, index);
        while (index > 0 && IsWordAt(text, Previous(text, index))) index = Previous(text, index);
        return index;
    }

    public static int WordRight(string text, int index)
    {
        while (index < text.Length && IsWordAt(text, index)) index = Next(text, index);
        while (index < text.Length && !IsWordAt(text, index)) index = Next(text, index);
        return index;
    }

    public static (int Start, int Length) WordAt(string text, int index)
    {
        if (text.Length == 0) return (0, 0);
        index = index == text.Length ? Previous(text, index) : ClampBoundary(text, index);
        if (!IsWordAt(text, index)) return (index, Next(text, index) - index);
        var start = index;
        var end = Next(text, index);
        while (start > 0 && IsWordAt(text, Previous(text, start))) start = Previous(text, start);
        while (end < text.Length && IsWordAt(text, end)) end = Next(text, end);
        return (start, end - start);
    }

    private static bool IsWordAt(string text, int index)
    {
        if (index >= text.Length) return false;
        var category = Rune.GetUnicodeCategory(Rune.GetRuneAt(text, index));
        return category is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or
            UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter or
            UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber or
            UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.ConnectorPunctuation;
    }
}
