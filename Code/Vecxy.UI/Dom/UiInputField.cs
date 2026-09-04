using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Facebook.Yoga;
using Vecxy.Assets;
using Vecxy.Kernel;

namespace Vecxy.UI;

public enum TextInputType : byte { Text, Password }

public sealed class UiInputField : UiElement
{
    private readonly TextEditingState _editing = new();
    private Action<UiElement?>? _focus;
    private bool _readOnly;
    private TextInputType _inputType;
    private string _placeholder = string.Empty;
    private double _blinkTime;
    private bool _caretVisible = true;
    private bool _pointerSelecting;
    private long _lastClick;
    private Vector2 _lastClickPosition;
    private int _clickCount;

    internal UiInputField(Config config, IReadOnlyDictionary<string, string> attributes, string? text = null)
        : base(config, "input-field", attributes, text)
    {
        _editing.MaxLength = ParseMaxLength(attributes.GetValueOrDefault("max-length"));
        _editing.SetText(attributes.GetValueOrDefault("value") ?? text ?? string.Empty, true);
        Placeholder = attributes.GetValueOrDefault("placeholder") ?? string.Empty;
        _readOnly = BooleanAttribute(attributes, "readonly") || BooleanAttribute(attributes, "read-only");
        _inputType = string.Equals(attributes.GetValueOrDefault("input-type"), "password", StringComparison.OrdinalIgnoreCase)
            ? TextInputType.Password : TextInputType.Text;
        Focused += _ => { ResetBlink(); FocusGained?.Invoke(this); };
        Blurred += _ => { _pointerSelecting = false; FocusLost?.Invoke(this); };
    }

    public new string Text
    {
        get => _editing.Text;
        set => Change(() => _editing.SetText(value ?? string.Empty));
    }

    public string Placeholder
    {
        get => _placeholder;
        set
        {
            value ??= string.Empty;
            if (_placeholder == value) return;
            _placeholder = value;
            MarkContentChanged();
        }
    }
    public bool ReadOnly { get => _readOnly; set => _readOnly = value; }
    public bool Disabled
    {
        get => IsDisabled;
        set { IsEnabled = !value; if (value && IsFocused) Blur(); }
    }
    public int MaxLength
    {
        get => _editing.MaxLength;
        set
        {
            _editing.MaxLength = Math.Max(0, value);
            if (value > 0) Change(() => _editing.SetText(_editing.Text));
        }
    }
    public TextInputType InputType { get => _inputType; set { if (_inputType != value) { _inputType = value; InvalidateVisual(); } } }
    public int CaretIndex => _editing.CaretIndex;
    public int SelectionAnchor => _editing.SelectionAnchor;
    public int SelectionCaret => _editing.SelectionCaret;
    public int SelectionStart => _editing.SelectionStart;
    public int SelectionLength => _editing.SelectionLength;
    public bool HasSelection => _editing.HasSelection;

    internal float HorizontalScroll { get; private set; }
    internal Rect ContentBounds { get; set; }
    internal bool CaretVisible => IsFocused && !ReadOnly && _caretVisible;
    internal string DisplayText => InputType == TextInputType.Password
        ? new string('•', new StringInfo(Text).LengthInTextElements) : Text;

    public event Action<string>? TextChanged;
    public event Action<string>? Submitted;
    public event Action<UiInputField>? FocusGained;
    public event Action<UiInputField>? FocusLost;
    public event Action<UiInputField>? SelectionChanged;

    public void Focus() => _focus?.Invoke(this);
    public void Blur() => _focus?.Invoke(null);
    public void SelectAll() { SetSelection(() => _editing.SelectAll()); EnsureCaretVisible(); }
    public void Select(int start, int length) { SetSelection(() => _editing.Select(start, length)); EnsureCaretVisible(); }
    public void ClearSelection() { SetSelection(() => _editing.Collapse(CaretIndex)); }
    public void MoveCaretToStart() { SetSelection(() => _editing.Collapse(0)); EnsureCaretVisible(); }
    public void MoveCaretToEnd() { SetSelection(() => _editing.Collapse(Text.Length)); EnsureCaretVisible(); }

    internal void AttachFocus(Action<UiElement?> focus) => _focus = focus;

    internal void UpdateInput(double deltaTime)
    {
        if (!IsFocused) return;
        _blinkTime += deltaTime;
        if (_blinkTime < 0.5) return;
        _blinkTime %= 0.5;
        _caretVisible = !_caretVisible;
        InvalidateVisual();
    }

    internal void HandleTextInput(string value)
    {
        if (ReadOnly || Disabled || string.IsNullOrEmpty(value)) return;
        Change(() => _editing.Insert(value));
        EnsureCaretVisible();
    }

    internal void HandleKey(EKeyboardKey key, bool shift, bool primary, IClipboard clipboard)
    {
        if (Disabled) return;
        if (primary)
        {
            if (key == EKeyboardKey.A) { SelectAll(); return; }
            if (key == EKeyboardKey.C) { Copy(clipboard); return; }
            if (key == EKeyboardKey.X) { Cut(clipboard); return; }
            if (key == EKeyboardKey.V) { Paste(clipboard); return; }
        }
        switch (key)
        {
            case EKeyboardKey.Left: SetSelection(() => _editing.MoveLeft(shift, primary)); break;
            case EKeyboardKey.Right: SetSelection(() => _editing.MoveRight(shift, primary)); break;
            case EKeyboardKey.Home: SetSelection(() => _editing.Move(0, shift)); break;
            case EKeyboardKey.End: SetSelection(() => _editing.Move(Text.Length, shift)); break;
            case EKeyboardKey.Backspace when !ReadOnly: Change(() => _editing.Backspace(primary)); break;
            case EKeyboardKey.Delete when !ReadOnly: Change(() => _editing.Delete(primary)); break;
            case EKeyboardKey.Enter or EKeyboardKey.KeypadEnter: Submitted?.Invoke(Text); break;
            case EKeyboardKey.Escape: Blur(); return;
            default: return;
        }
        EnsureCaretVisible();
    }

    internal void BeginPointerSelection(Vector2 point)
    {
        if (Disabled) return;
        Focus();
        var now = Stopwatch.GetTimestamp();
        var elapsed = Stopwatch.GetElapsedTime(_lastClick, now).TotalMilliseconds;
        _clickCount = elapsed <= 450 && Vector2.DistanceSquared(point, _lastClickPosition) <= 25 ? _clickCount + 1 : 1;
        _lastClick = now;
        _lastClickPosition = point;
        var index = HitTestText(point.X);
        if (_clickCount == 2)
        {
            var word = TextNavigation.WordAt(Text, index);
            Select(word.Start, word.Length);
        }
        else if (_clickCount >= 3)
        {
            SelectAll();
            _clickCount = 0;
        }
        else Select(index, 0);
        _pointerSelecting = true;
    }

    internal void UpdatePointerSelection(Vector2 point)
    {
        if (!_pointerSelecting || Disabled) return;
        var anchor = SelectionAnchor;
        SetSelection(() => { _editing.Collapse(anchor); _editing.Move(HitTestText(point.X), true); });
        EnsureCaretVisible();
    }

    internal void EndPointerSelection() => _pointerSelecting = false;

    internal string DisplayPrefix(int utf16Index)
    {
        utf16Index = TextNavigation.ClampBoundary(Text, utf16Index);
        return InputType == TextInputType.Password
            ? new string('•', new StringInfo(Text[..utf16Index]).LengthInTextElements)
            : Text[..utf16Index];
    }

    internal float Measure(string value) => UiTextMeasurement.MeasureWidth(this, value);

    internal void EnsureCaretVisible()
    {
        if (ContentBounds.Width <= 0) return;
        var previous = HorizontalScroll;
        var caret = Measure(DisplayPrefix(CaretIndex));
        if (caret - HorizontalScroll > ContentBounds.Width - 2) HorizontalScroll = caret - ContentBounds.Width + 2;
        if (caret - HorizontalScroll < 0) HorizontalScroll = caret;
        HorizontalScroll = Math.Max(0, HorizontalScroll);
        if (Math.Abs(previous - HorizontalScroll) > 0.001f)
            InvalidateVisual();
    }

    private int HitTestText(float documentX)
    {
        var local = documentX - ContentBounds.X + HorizontalScroll;
        var display = DisplayText;
        var textWidth = Measure(display);
        if (HorizontalScroll <= 0 && textWidth < ContentBounds.Width)
            local -= ComputedStyle.TextAlign switch { "center" => (ContentBounds.Width - textWidth) * .5f, "right" or "end" => ContentBounds.Width - textWidth, _ => 0 };
        if (local <= 0) return 0;
        var boundaries = StringInfo.ParseCombiningCharacters(Text).Append(Text.Length);
        foreach (var boundary in boundaries)
        {
            var width = Measure(DisplayPrefix(boundary));
            var previous = TextNavigation.Previous(Text, boundary);
            var previousWidth = Measure(DisplayPrefix(previous));
            if (local <= (previousWidth + width) * .5f) return previous;
        }
        return Text.Length;
    }

    private void Copy(IClipboard clipboard)
    {
        if (!HasSelection || InputType == TextInputType.Password) return;
        clipboard.SetText(Text.Substring(SelectionStart, SelectionLength));
    }

    private void Cut(IClipboard clipboard)
    {
        if (ReadOnly || InputType == TextInputType.Password || !HasSelection) return;
        Copy(clipboard);
        Change(() => _editing.DeleteSelection());
    }

    private void Paste(IClipboard clipboard)
    {
        if (ReadOnly || clipboard.GetText() is not { } value) return;
        Change(() => _editing.Insert(value));
    }

    private void Change(Action operation)
    {
        var beforeText = _editing.Text;
        var beforeAnchor = SelectionAnchor;
        var beforeCaret = SelectionCaret;
        operation();
        ResetBlink();
        if (beforeText != _editing.Text)
        {
            MarkContentChanged();
            TextChanged?.Invoke(_editing.Text);
        }
        if (beforeAnchor != SelectionAnchor || beforeCaret != SelectionCaret) SelectionChanged?.Invoke(this);
        InvalidateVisual();
    }

    private void SetSelection(Action operation)
    {
        var anchor = SelectionAnchor;
        var caret = SelectionCaret;
        operation();
        ResetBlink();
        if (anchor != SelectionAnchor || caret != SelectionCaret) SelectionChanged?.Invoke(this);
        InvalidateVisual();
    }

    private void ResetBlink() { _blinkTime = 0; _caretVisible = true; }
    private void MarkContentChanged()
    {
        if (Facebook.Yoga.YGNodeAPI.YGNodeHasMeasureFunc(YogaNode))
            Facebook.Yoga.YGNodeAPI.YGNodeMarkDirty(YogaNode);
        InvalidateLayout();
        InvalidateVisual();
    }
    private static bool BooleanAttribute(IReadOnlyDictionary<string, string> values, string name) =>
        values.TryGetValue(name, out var value) && !value.Equals("false", StringComparison.OrdinalIgnoreCase);
    private static int ParseMaxLength(string? value) => int.TryParse(value, out var result) ? Math.Max(0, result) : 0;
}
