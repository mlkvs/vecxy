namespace Vecxy.UI;

internal sealed class UiTextLayoutCache
{
    private readonly Entry[] _entries = [new(), new()];
    private int _replacement;

    public bool TryGet(
        object font,
        string text,
        float fontSize,
        float maximumWidth,
        out string[] lines)
    {
        var widthRatio = float.IsFinite(maximumWidth)
            ? maximumWidth / Math.Max(0.001f, fontSize)
            : float.PositiveInfinity;
        foreach (var entry in _entries)
        {
            if (ReferenceEquals(entry.Font, font) &&
                string.Equals(entry.Text, text, StringComparison.Ordinal) &&
                (entry.WidthRatio == widthRatio ||
                 Math.Abs(entry.WidthRatio - widthRatio) <= 0.001f))
            {
                lines = entry.Lines;
                return true;
            }
        }
        lines = [];
        return false;
    }

    public void Store(
        object font,
        string text,
        float fontSize,
        float maximumWidth,
        string[] lines)
    {
        var entry = _entries[_replacement++ % _entries.Length];
        entry.Font = font;
        entry.Text = text;
        entry.WidthRatio = float.IsFinite(maximumWidth)
            ? maximumWidth / Math.Max(0.001f, fontSize)
            : float.PositiveInfinity;
        entry.Lines = lines;
    }

    private sealed class Entry
    {
        public object? Font;
        public string? Text;
        public float WidthRatio = float.NaN;
        public string[] Lines = [];
    }
}
