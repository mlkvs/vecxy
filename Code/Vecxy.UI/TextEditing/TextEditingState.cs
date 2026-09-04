using System.Globalization;

namespace Vecxy.UI;

/// <summary>Reusable single-line text editing state. Selection indices are UTF-16 boundaries.</summary>
public sealed class TextEditingState
{
    private string _text = string.Empty;

    public string Text
    {
        get => _text;
        set
        {
            _text = value ?? string.Empty;
            SelectionAnchor = TextNavigation.ClampBoundary(_text, SelectionAnchor);
            SelectionCaret = TextNavigation.ClampBoundary(_text, SelectionCaret);
            SelectionAnchor = Math.Min(SelectionAnchor, _text.Length);
            SelectionCaret = Math.Min(SelectionCaret, _text.Length);
        }
    }

    public int SelectionAnchor { get; private set; }
    public int SelectionCaret { get; private set; }
    public int CaretIndex => SelectionCaret;
    public int SelectionStart => Math.Min(SelectionAnchor, SelectionCaret);
    public int SelectionLength => Math.Abs(SelectionCaret - SelectionAnchor);
    public bool HasSelection => SelectionAnchor != SelectionCaret;
    public int MaxLength { get; set; }

    public void SetText(string value, bool moveCaretToEnd = false)
    {
        Text = MaxLength <= 0 ? value ?? string.Empty : Limit(value ?? string.Empty, MaxLength);
        if (moveCaretToEnd) Collapse(Text.Length);
    }

    public bool Insert(string? value)
    {
        value = Sanitize(value);
        if (value.Length == 0 && !HasSelection) return false;
        var start = SelectionStart;
        var capacity = MaxLength <= 0 ? int.MaxValue : Math.Max(0, MaxLength - TextElementCountOutsideSelection());
        value = capacity == 0 ? string.Empty : Limit(value, capacity);
        var replacement = Text.Remove(start, SelectionLength).Insert(start, value);
        if (replacement == Text) { Collapse(start + value.Length); return false; }
        Text = replacement;
        Collapse(start + value.Length);
        return true;
    }

    public bool Backspace(bool word = false)
    {
        if (HasSelection) return DeleteSelection();
        var start = word ? TextNavigation.WordLeft(Text, CaretIndex) : TextNavigation.Previous(Text, CaretIndex);
        return DeleteRange(start, CaretIndex - start);
    }

    public bool Delete(bool word = false)
    {
        if (HasSelection) return DeleteSelection();
        var end = word ? TextNavigation.WordRight(Text, CaretIndex) : TextNavigation.Next(Text, CaretIndex);
        return DeleteRange(CaretIndex, end - CaretIndex);
    }

    public bool DeleteSelection() => HasSelection && DeleteRange(SelectionStart, SelectionLength);

    public void MoveLeft(bool extend = false, bool word = false) =>
        Move(HasSelection && !extend && !word ? SelectionStart : word ? TextNavigation.WordLeft(Text, CaretIndex) : TextNavigation.Previous(Text, CaretIndex), extend);

    public void MoveRight(bool extend = false, bool word = false) =>
        Move(HasSelection && !extend && !word ? SelectionStart + SelectionLength : word ? TextNavigation.WordRight(Text, CaretIndex) : TextNavigation.Next(Text, CaretIndex), extend);

    public void Move(int index, bool extend = false)
    {
        index = TextNavigation.ClampBoundary(Text, index);
        if (!extend) SelectionAnchor = index;
        SelectionCaret = index;
    }

    public void Select(int start, int length)
    {
        start = TextNavigation.ClampBoundary(Text, start);
        var end = TextNavigation.ClampBoundary(Text, Math.Clamp(start + length, 0, Text.Length));
        SelectionAnchor = start;
        SelectionCaret = end;
    }

    public void SelectAll() => Select(0, Text.Length);
    public void Collapse(int index) => Move(index);

    private bool DeleteRange(int start, int length)
    {
        if (length <= 0) return false;
        Text = Text.Remove(start, length);
        Collapse(start);
        return true;
    }

    private int TextElementCountOutsideSelection() =>
        new StringInfo(Text.Remove(SelectionStart, SelectionLength)).LengthInTextElements;

    private static string Sanitize(string? value) =>
        (value ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);

    private static string Limit(string value, int maximum)
    {
        if (maximum <= 0) return string.Empty;
        var boundaries = StringInfo.ParseCombiningCharacters(value);
        return boundaries.Length <= maximum ? value : value[..boundaries[maximum]];
    }
}
