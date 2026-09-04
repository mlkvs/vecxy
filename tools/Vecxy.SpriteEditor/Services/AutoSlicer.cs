namespace Vecxy.SpriteEditor;

public sealed record AutoSliceOptions(byte AlphaThreshold = 1, int MinWidth = 4, int MinHeight = 4, int Padding = 0, int MergeDistance = 2);
public sealed record GridSliceOptions(int CellWidth, int CellHeight, int OffsetX = 0, int OffsetY = 0, int SpacingX = 0, int SpacingY = 0);

public static class AutoSlicer
{
    public static IReadOnlyList<SpriteSlice> ByTransparency(byte[] rgba, int width, int height, AutoSliceOptions options)
    {
        var visited = new bool[width * height];
        var queue = new Queue<int>();
        var bounds = new List<Bounds>();
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var start = y * width + x;
            if (visited[start] || rgba[start * 4 + 3] < options.AlphaThreshold) continue;
            visited[start] = true;
            queue.Enqueue(start);
            var area = new Bounds(x, y, x, y);
            while (queue.TryDequeue(out var index))
            {
                var px = index % width;
                var py = index / width;
                area = area.Include(px, py);
                Visit(px - 1, py); Visit(px + 1, py); Visit(px, py - 1); Visit(px, py + 1);
            }
            if (area.Width >= options.MinWidth && area.Height >= options.MinHeight) bounds.Add(area);

            void Visit(int nx, int ny)
            {
                if (nx < 0 || ny < 0 || nx >= width || ny >= height) return;
                var next = ny * width + nx;
                if (visited[next]) return;
                visited[next] = true;
                if (rgba[next * 4 + 3] >= options.AlphaThreshold) queue.Enqueue(next);
            }
        }
        Merge(bounds, Math.Max(0, options.MergeDistance));
        return bounds.OrderBy(value => value.Top).ThenBy(value => value.Left).Select(value =>
        {
            var left = Math.Max(0, value.Left - options.Padding);
            var top = Math.Max(0, value.Top - options.Padding);
            var right = Math.Min(width - 1, value.Right + options.Padding);
            var bottom = Math.Min(height - 1, value.Bottom + options.Padding);
            return new SpriteSlice { X = left, Y = top, Width = right - left + 1, Height = bottom - top + 1 };
        }).ToArray();
    }

    public static IReadOnlyList<SpriteSlice> ByGrid(int width, int height, GridSliceOptions options)
    {
        if (options.CellWidth < 1 || options.CellHeight < 1) return [];
        var result = new List<SpriteSlice>();
        for (var y = options.OffsetY; y + options.CellHeight <= height; y += options.CellHeight + Math.Max(0, options.SpacingY))
        for (var x = options.OffsetX; x + options.CellWidth <= width; x += options.CellWidth + Math.Max(0, options.SpacingX))
            result.Add(new SpriteSlice { X = x, Y = y, Width = options.CellWidth, Height = options.CellHeight });
        return result;
    }

    private static void Merge(List<Bounds> values, int distance)
    {
        for (var changed = true; changed;)
        {
            changed = false;
            for (var i = 0; i < values.Count && !changed; i++)
            for (var j = i + 1; j < values.Count; j++)
                if (values[i].DistanceTo(values[j]) <= distance)
                {
                    values[i] = values[i].Union(values[j]);
                    values.RemoveAt(j);
                    changed = true;
                    break;
                }
        }
    }

    private readonly record struct Bounds(int Left, int Top, int Right, int Bottom)
    {
        public int Width => Right - Left + 1;
        public int Height => Bottom - Top + 1;
        public Bounds Include(int x, int y) => new(Math.Min(Left, x), Math.Min(Top, y), Math.Max(Right, x), Math.Max(Bottom, y));
        public Bounds Union(Bounds other) => new(Math.Min(Left, other.Left), Math.Min(Top, other.Top), Math.Max(Right, other.Right), Math.Max(Bottom, other.Bottom));
        public int DistanceTo(Bounds other)
        {
            var dx = Math.Max(0, Math.Max(other.Left - Right - 1, Left - other.Right - 1));
            var dy = Math.Max(0, Math.Max(other.Top - Bottom - 1, Top - other.Bottom - 1));
            return Math.Max(dx, dy);
        }
    }
}

