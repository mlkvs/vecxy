namespace Vecxy.UI;

internal static class UiTextWrap
{
    // Layout and glyph advances travel through different float calculations.
    // A small tolerance prevents a word measured to fit from jumping to a new
    // line because the final paint width differs by a fraction of a pixel.
    private const float FitTolerance = 0.5f;

    public static string[] Lines(
        string text,
        float maximumWidth,
        Func<string, float> measure)
    {
        if (!float.IsFinite(maximumWidth) || maximumWidth <= 0.0f)
            return text.Replace("\r", string.Empty).Split('\n');

        var result = new List<string>();
        foreach (var paragraph in text.Replace("\r", string.Empty).Split('\n'))
        {
            if (paragraph.Length == 0)
            {
                result.Add(string.Empty);
                continue;
            }

            var line = string.Empty;
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = line.Length == 0 ? word : $"{line} {word}";
                if (measure(candidate) <= maximumWidth + FitTolerance)
                {
                    line = candidate;
                    continue;
                }

                if (line.Length > 0)
                {
                    result.Add(line);
                    line = string.Empty;
                }

                // Keep lexical words intact. A constrained text element may use
                // text-fit: shrink to fit an unusually long word; splitting the
                // word here produces visually broken labels such as "лоток" or
                // a single trailing Cyrillic character on the next line.
                line = word;
            }

            if (line.Length > 0)
                result.Add(line);
        }

        return result.Count == 0 ? [string.Empty] : result.ToArray();
    }
}
