using System.Globalization;
using Facebook.Yoga;
using static Facebook.Yoga.YGNodeStyleAPI;

namespace Vecxy.UI;

internal static class UiGridLayout
{
    public static void ResetNativeGrid(Node node)
    {
        YGNodeStyleSetGridTemplateColumnsCount(node, 0);
        YGNodeStyleSetGridTemplateRowsCount(node, 0);
        YGNodeStyleSetGridAutoColumnsCount(node, 0);
        YGNodeStyleSetGridAutoRowsCount(node, 0);
    }

    public static bool PlaceGrids(UiElement root, float viewportWidth, float viewportHeight)
    {
        var grids = root.DescendantsAndSelf()
            .Where(element => element.IsDisplayed && element.ComputedStyle.Display == "grid")
            .ToArray();
        foreach (var grid in grids)
            PlaceGrid(grid, viewportWidth, viewportHeight);
        return grids.Length > 0;
    }

    internal static (string Start, string End) ParsePlacement(string source)
    {
        var slash = source.IndexOf('/');
        return slash < 0
            ? (source.Trim(), "auto")
            : (source[..slash].Trim(), source[(slash + 1)..].Trim());
    }

    private static void PlaceGrid(UiElement container, float viewportWidth, float viewportHeight)
    {
        var style = container.ComputedStyle;
        var columns = ParseTracks(style.GridTemplateColumns, viewportWidth, viewportHeight);
        if (columns.Count == 0)
            columns.Add(new Track(ETrack.Fr, 1.0f, 0.0f));
        var rows = ParseTracks(style.GridTemplateRows, viewportWidth, viewportHeight);
        var autoRows = ParseTracks(style.GridAutoRows, viewportWidth, viewportHeight);
        var items = container.Children
            .Where(child =>
                child.IsDisplayed &&
                child.ComputedStyle.Position is not ("absolute" or "fixed"))
            .ToArray();
        var placements = PlaceItems(items, columns.Count);
        var rowCount = Math.Max(
            rows.Count,
            placements.Count == 0 ? 0 : placements.Max(item => item.Row + item.RowSpan));
        while (rows.Count < rowCount)
        {
            var implicitIndex = rows.Count;
            rows.Add(autoRows.Count > 0
                ? autoRows[implicitIndex % autoRows.Count]
                : new Track(ETrack.Auto, 0.0f, 0.0f));
        }

        var columnGap = ResolveGap(style.ColumnGap ?? style.Gap, viewportWidth, viewportHeight);
        var rowGap = ResolveGap(style.RowGap ?? style.Gap, viewportWidth, viewportHeight);
        var leftPadding = ResolveEdge(style.Padding.Left, viewportWidth, viewportHeight) + style.BorderWidth;
        var rightPadding = ResolveEdge(style.Padding.Right, viewportWidth, viewportHeight) + style.BorderWidth;
        var topPadding = ResolveEdge(style.Padding.Top, viewportWidth, viewportHeight) + style.BorderWidth;
        var bottomPadding = ResolveEdge(style.Padding.Bottom, viewportWidth, viewportHeight) + style.BorderWidth;
        var contentWidth = Math.Max(0.0f, container.Bounds.Width - leftPadding - rightPadding);
        var contentHeight = Math.Max(0.0f, container.Bounds.Height - topPadding - bottomPadding);
        var columnSizes = ResolveTrackSizes(columns, contentWidth, columnGap, placements, true);
        var rowSizes = ResolveTrackSizes(rows, contentHeight, rowGap, placements, false);

        if (style.Height.Unit == EUiLengthUnit.Auto && rowSizes.Length > 0)
        {
            var desiredHeight = rowSizes.Sum() + rowGap * Math.Max(0, rowSizes.Length - 1) +
                                topPadding + bottomPadding;
            YGNodeStyleSetHeight(container.YogaNode, desiredHeight);
        }

        foreach (var placement in placements)
        {
            var x = leftPadding + columnSizes.Take(placement.Column).Sum() + columnGap * placement.Column;
            var y = topPadding + rowSizes.Take(placement.Row).Sum() + rowGap * placement.Row;
            var width = columnSizes.Skip(placement.Column).Take(placement.ColumnSpan).Sum() +
                        columnGap * Math.Max(0, placement.ColumnSpan - 1);
            var height = rowSizes.Skip(placement.Row).Take(placement.RowSpan).Sum() +
                         rowGap * Math.Max(0, placement.RowSpan - 1);
            var node = placement.Element.YogaNode;
            YGNodeStyleSetPositionType(node, YGPositionType.Absolute);
            YGNodeStyleSetPosition(node, YGEdge.Left, x);
            YGNodeStyleSetPosition(node, YGEdge.Top, y);
            YGNodeStyleSetWidth(node, Math.Max(0.0f, width));
            YGNodeStyleSetHeight(node, Math.Max(0.0f, height));
        }
    }

    private static List<Placement> PlaceItems(IReadOnlyList<UiElement> items, int columnCount)
    {
        var result = new List<Placement>(items.Count);
        var occupied = new HashSet<(int Row, int Column)>();
        var cursorRow = 0;
        var cursorColumn = 0;
        foreach (var item in items)
        {
            var columnStart = ParseLine(item.ComputedStyle.GridColumnStart);
            var columnEnd = ParseLine(item.ComputedStyle.GridColumnEnd);
            var rowStart = ParseLine(item.ComputedStyle.GridRowStart);
            var rowEnd = ParseLine(item.ComputedStyle.GridRowEnd);
            var columnSpan = ResolveSpan(columnStart, columnEnd, columnCount);
            var rowSpan = ResolveSpan(rowStart, rowEnd, int.MaxValue);
            var column = columnStart.Kind == ELine.Integer ? columnStart.Value - 1 : -1;
            var row = rowStart.Kind == ELine.Integer ? rowStart.Value - 1 : -1;

            if (column < 0 || row < 0)
            {
                if (column < 0 && row < 0)
                {
                    column = cursorColumn;
                    row = cursorRow;
                }
                else
                {
                    column = Math.Max(0, column);
                    row = Math.Max(0, row);
                }
                while (!Fits(row, column, rowSpan, columnSpan, columnCount, occupied))
                {
                    column++;
                    if (column >= columnCount)
                    {
                        column = 0;
                        row++;
                    }
                }
            }

            column = Math.Clamp(column, 0, Math.Max(0, columnCount - columnSpan));
            row = Math.Max(0, row);
            for (var y = row; y < row + rowSpan; y++)
            for (var x = column; x < column + columnSpan; x++)
                occupied.Add((y, x));
            result.Add(new Placement(item, row, column, rowSpan, columnSpan));
            cursorRow = row;
            cursorColumn = column + columnSpan;
            if (cursorColumn >= columnCount)
            {
                cursorColumn = 0;
                cursorRow++;
            }
        }
        return result;
    }

    private static int ResolveSpan(Line start, Line end, int maximum)
    {
        var span = end.Kind == ELine.Span ? end.Value : start.Kind == ELine.Span ? start.Value :
            start.Kind == ELine.Integer && end.Kind == ELine.Integer
                ? Math.Max(1, end.Value - start.Value)
                : 1;
        return Math.Clamp(span, 1, maximum);
    }

    private static bool Fits(
        int row,
        int column,
        int rowSpan,
        int columnSpan,
        int columnCount,
        HashSet<(int Row, int Column)> occupied)
    {
        if (column + columnSpan > columnCount)
            return false;
        for (var y = row; y < row + rowSpan; y++)
        for (var x = column; x < column + columnSpan; x++)
            if (occupied.Contains((y, x)))
                return false;
        return true;
    }

    private static float[] ResolveTrackSizes(
        IReadOnlyList<Track> tracks,
        float available,
        float gap,
        IReadOnlyList<Placement> placements,
        bool horizontal)
    {
        var result = new float[tracks.Count];
        var fractionTotal = 0.0f;
        for (var index = 0; index < tracks.Count; index++)
        {
            var track = tracks[index];
            result[index] = track.Kind switch
            {
                ETrack.Points => track.Value,
                ETrack.Percent => available * track.Value * 0.01f,
                ETrack.Auto => placements
                    .Where(item =>
                        (horizontal ? item.ColumnSpan : item.RowSpan) == 1 &&
                        (horizontal ? item.Column : item.Row) == index)
                    .Select(item => DesiredSize(item.Element, horizontal))
                    .DefaultIfEmpty(0.0f)
                    .Max(),
                _ => 0.0f
            };
            // A fractional minmax() track participates in the distribution with
            // its full fraction. Its minimum is a lower bound, not a fixed part
            // that must be subtracted before fractions are calculated.
            if (track.Kind != ETrack.Fr)
                result[index] = Math.Max(result[index], track.Minimum);
            if (track.Kind == ETrack.Fr)
                fractionTotal += Math.Max(0.0f, track.Value);
        }
        var remaining = Math.Max(
            0.0f,
            available - gap * Math.Max(0, tracks.Count - 1) - result.Sum());
        if (fractionTotal > 0.0f)
        {
            for (var index = 0; index < tracks.Count; index++)
                if (tracks[index].Kind == ETrack.Fr)
                    result[index] = Math.Max(
                        tracks[index].Minimum,
                        remaining * tracks[index].Value / fractionTotal);
        }
        return result;
    }

    private static float DesiredSize(UiElement element, bool horizontal)
    {
        var length = horizontal ? element.ComputedStyle.Width : element.ComputedStyle.Height;
        if (length.Unit is EUiLengthUnit.Pixel or EUiLengthUnit.Ui)
            return length.Value;
        var intrinsic = horizontal ? element.IntrinsicSize.X : element.IntrinsicSize.Y;
        return intrinsic > 0.0f
            ? intrinsic
            : horizontal ? element.Bounds.Width : element.Bounds.Height;
    }

    private static List<Track> ParseTracks(string source, float viewportWidth, float viewportHeight)
    {
        var result = new List<Track>();
        foreach (var token in ExpandTracks(source))
        {
            if (TryMinMax(token, viewportWidth, viewportHeight, out var minimum, out var maximum))
            {
                var minimumValue = minimum.Kind switch
                {
                    ETrack.Points => minimum.Value,
                    ETrack.Percent => viewportWidth * minimum.Value * 0.01f,
                    _ => 0.0f
                };
                result.Add(maximum with { Minimum = minimumValue });
            }
            else
            {
                result.Add(ParseTrack(token, viewportWidth, viewportHeight));
            }
        }
        return result;
    }

    private static IEnumerable<string> ExpandTracks(string source)
    {
        foreach (var token in SplitWhitespace(source))
        {
            if (!token.StartsWith("repeat(", StringComparison.OrdinalIgnoreCase) || !token.EndsWith(')'))
            {
                if (!string.IsNullOrWhiteSpace(token) && !token.Equals("none", StringComparison.OrdinalIgnoreCase))
                    yield return token;
                continue;
            }
            var arguments = token[7..^1];
            var comma = FindTopLevelComma(arguments);
            if (comma <= 0 ||
                !int.TryParse(arguments[..comma].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) ||
                count is < 1 or > 256)
                continue;
            var repeated = SplitWhitespace(arguments[(comma + 1)..]).ToArray();
            for (var repetition = 0; repetition < count; repetition++)
            foreach (var repeatedTrack in repeated)
                yield return repeatedTrack;
        }
    }

    private static bool TryMinMax(
        string source,
        float viewportWidth,
        float viewportHeight,
        out Track minimum,
        out Track maximum)
    {
        minimum = default;
        maximum = default;
        if (!source.StartsWith("minmax(", StringComparison.OrdinalIgnoreCase) || !source.EndsWith(')'))
            return false;
        var arguments = source[7..^1];
        var comma = FindTopLevelComma(arguments);
        if (comma <= 0)
            return false;
        minimum = ParseTrack(arguments[..comma], viewportWidth, viewportHeight);
        maximum = ParseTrack(arguments[(comma + 1)..], viewportWidth, viewportHeight);
        return true;
    }

    private static Track ParseTrack(string source, float viewportWidth, float viewportHeight)
    {
        source = source.Trim();
        if (source is "auto" or "min-content" or "max-content")
            return new Track(ETrack.Auto, 0.0f, 0.0f);
        if (source.EndsWith("fr", StringComparison.OrdinalIgnoreCase) &&
            TryFloat(source[..^2], out var fraction))
            return new Track(ETrack.Fr, Math.Max(0.0f, fraction), 0.0f);
        if (!UiLength.TryParse(source, out var length))
            return new Track(ETrack.Auto, 0.0f, 0.0f);
        if (length.Unit == EUiLengthUnit.Percent)
            return new Track(ETrack.Percent, length.Value, 0.0f);
        if (length.Unit == EUiLengthUnit.Auto)
            return new Track(ETrack.Auto, 0.0f, 0.0f);
        return new Track(
            ETrack.Points,
            UiLayout.ResolvePoints(length, viewportWidth, viewportHeight),
            0.0f);
    }

    private static Line ParseLine(string source)
    {
        source = source.Trim().ToLowerInvariant();
        if (source.Length == 0 || source == "auto")
            return default;
        if (source.StartsWith("span ") && int.TryParse(source[5..].Trim(), out var span) && span > 0)
            return new Line(ELine.Span, span);
        return int.TryParse(source, out var value)
            ? new Line(ELine.Integer, value)
            : default;
    }

    private static float ResolveGap(UiLength length, float width, float height) =>
        length.Unit == EUiLengthUnit.Percent
            ? width * length.Value * 0.01f
            : length.Unit == EUiLengthUnit.Auto ? 0.0f : UiLayout.ResolvePoints(length, width, height);

    private static float ResolveEdge(UiLength length, float width, float height) =>
        length.Unit == EUiLengthUnit.Percent
            ? width * length.Value * 0.01f
            : length.Unit == EUiLengthUnit.Auto ? 0.0f : UiLayout.ResolvePoints(length, width, height);

    private static IEnumerable<string> SplitWhitespace(string source)
    {
        var start = -1;
        var depth = 0;
        for (var index = 0; index < source.Length; index++)
        {
            var character = source[index];
            if (character == '(') depth++;
            else if (character == ')') depth--;
            if (!char.IsWhiteSpace(character) || depth != 0)
            {
                if (start < 0) start = index;
                continue;
            }
            if (start >= 0)
            {
                yield return source[start..index];
                start = -1;
            }
        }
        if (start >= 0)
            yield return source[start..];
    }

    private static int FindTopLevelComma(string source)
    {
        var depth = 0;
        for (var index = 0; index < source.Length; index++)
        {
            if (source[index] == '(') depth++;
            else if (source[index] == ')') depth--;
            else if (source[index] == ',' && depth == 0) return index;
        }
        return -1;
    }

    private static bool TryFloat(string source, out float value) =>
        float.TryParse(source.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private enum ETrack : byte { Auto, Points, Percent, Fr }
    private enum ELine : byte { Auto, Integer, Span }
    private readonly record struct Track(ETrack Kind, float Value, float Minimum);
    private readonly record struct Line(ELine Kind, int Value);
    private readonly record struct Placement(
        UiElement Element,
        int Row,
        int Column,
        int RowSpan,
        int ColumnSpan);
}
