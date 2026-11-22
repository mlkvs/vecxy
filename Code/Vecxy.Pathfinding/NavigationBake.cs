using System.Numerics;
using System.Text.Json;

namespace Vecxy.Pathfinding;

public sealed record NavigationBakeSettings
{
    public Vector3 Min { get; init; }
    public Vector3 Max { get; init; }
    public float CellSize { get; init; } = .2f;
    public float Radius { get; init; } = .3f;
    public float Height { get; init; } = 1.8f;
    public float MaxSlope { get; init; } = 45;
    public float MaxStep { get; init; } = .3f;
}

public readonly record struct NavigationTriangle(Vector3 A, Vector3 B, Vector3 C, bool? SupportsWalking = null);

public sealed record NavigationBakeInput(NavigationBakeSettings Settings, NavigationTriangle[] Triangles,
    NavigationBox[]? Blockers = null, NavigationBox[]? Obstacles = null, NavigationBox[]? SolidVolumes = null);

/// <summary>An oriented box represented by its center and three world-space half-axes.</summary>
public readonly record struct NavigationBox(Vector3 Center, Vector3 Right, Vector3 Up, Vector3 Forward)
{
    public static NavigationBox FromTransform(Matrix4x4 transform, Vector3 size) => new(
        transform.Translation,
        Vector3.TransformNormal(Vector3.UnitX * (size.X * .5f), transform),
        Vector3.TransformNormal(Vector3.UnitY * (size.Y * .5f), transform),
        Vector3.TransformNormal(Vector3.UnitZ * (size.Z * .5f), transform));

    public NavigationTriangle[] ToTriangles()
    {
        Vector3[] points =
        [
            Center - Right - Up - Forward, Center + Right - Up - Forward,
            Center + Right - Up + Forward, Center - Right - Up + Forward,
            Center - Right + Up - Forward, Center + Right + Up - Forward,
            Center + Right + Up + Forward, Center - Right + Up + Forward
        ];
        ReadOnlySpan<int> indices = [0,1,2, 0,2,3, 4,6,5, 4,7,6, 0,4,5, 0,5,1,
            1,5,6, 1,6,2, 2,6,7, 2,7,3, 3,7,4, 3,4,0];
        var mirrored = Vector3.Dot(Vector3.Cross(Right, Up), Forward) < 0;
        var triangles = new NavigationTriangle[12];
        for (var i = 0; i < triangles.Length; i++)
            triangles[i] = new(points[indices[i * 3]], points[indices[i * 3 + (mirrored ? 2 : 1)]],
                points[indices[i * 3 + (mirrored ? 1 : 2)]]);
        return triangles;
    }

    internal Matrix4x4 UnitToWorld => new(
        Right.X, Right.Y, Right.Z, 0, Up.X, Up.Y, Up.Z, 0,
        Forward.X, Forward.Y, Forward.Z, 0, Center.X, Center.Y, Center.Z, 1);
}

public sealed record NavigationData(
    NavigationBakeSettings Settings, int Columns, int Rows, float?[] Heights,
    int[]? PreviewBlocked = null, NavigationBox[]? BakedBlockers = null,
    Dictionary<int, Vector2>? Gradients = null)
{
    public int Version { get; init; } = 1;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { IncludeFields = true };

    public static NavigationBakeInput ReadInput(Stream stream) =>
        JsonSerializer.Deserialize<NavigationBakeInput>(stream, JsonOptions) ??
        throw new InvalidDataException("Navigation bake input is empty.");

    public static NavigationData Read(Stream stream) =>
        (JsonSerializer.Deserialize<NavigationData>(stream, JsonOptions) ??
         throw new InvalidDataException("Navigation map is empty.")).Validate();

    public void Write(Stream stream) => JsonSerializer.Serialize(stream, Validate(), JsonOptions);

    public NavigationData Validate()
    {
        var (columns, rows) = NavigationBaker.Measure(Settings);
        if (Version != 1 || (Columns, Rows) != (columns, rows) || Heights is null ||
            Heights.Length != columns * rows || Heights.Any(height => height is { } value &&
                (!float.IsFinite(value) || value < Settings.Min.Y || value > Settings.Max.Y)) ||
            Gradients is not null && Gradients.Any(pair => (uint)pair.Key >= (uint)Heights.Length ||
                !Heights[pair.Key].HasValue || !float.IsFinite(pair.Value.X) || !float.IsFinite(pair.Value.Y)))
            throw new InvalidDataException("Navigation map has invalid dimensions or heights.");
        return this;
    }

    public GridPoint Point(int index) => new(index % Columns, index / Columns);
    public int Index(GridPoint point) => point.Y * Columns + point.X;
    public bool Contains(GridPoint point) =>
        (uint)point.X < (uint)Columns && (uint)point.Y < (uint)Rows;

    public GridPoint CellAt(Vector3 position) => new(
        (int)MathF.Floor((position.X - Settings.Min.X) / Settings.CellSize),
        (int)MathF.Floor((position.Z - Settings.Min.Z) / Settings.CellSize));

    public Vector3 Center(int index)
    {
        var point = Point(index);
        return new Vector3(Settings.Min.X + (point.X + .5f) * Settings.CellSize,
            Heights[index] ?? Settings.Min.Y, Settings.Min.Z + (point.Y + .5f) * Settings.CellSize);
    }

    public Vector2 Gradient(int index) => Gradients?.GetValueOrDefault(index) ?? Vector2.Zero;

    public IEnumerable<int> BlockedCells(NavigationBox box)
    {
        var extent = Vector3.Abs(box.Right) + Vector3.Abs(box.Up) + Vector3.Abs(box.Forward);
        var padding = Settings.Radius + Settings.CellSize * .5f;
        var margin = new Vector3(padding, 0, padding);
        var min = CellAt(box.Center - extent - margin);
        var max = CellAt(box.Center + extent + margin);
        for (var z = Math.Max(0, min.Y); z <= Math.Min(Rows - 1, max.Y); z++)
        for (var x = Math.Max(0, min.X); x <= Math.Min(Columns - 1, max.X); x++)
        {
            var index = Index(new(x, z));
            if (Heights[index] is not { } floor || box.Center.Y + extent.Y <= floor + .001f ||
                box.Center.Y - extent.Y >= floor + Settings.Height)
                continue;
            var distance = Center(index) - box.Center;
            if (MathF.Abs(distance.X) <= extent.X + padding &&
                MathF.Abs(distance.Z) <= extent.Z + padding &&
                InsideAxis(new(-box.Right.Z, 0, box.Right.X)) &&
                InsideAxis(new(-box.Up.Z, 0, box.Up.X)) &&
                InsideAxis(new(-box.Forward.Z, 0, box.Forward.X)))
                yield return index;

            bool InsideAxis(Vector3 axis)
            {
                var support = MathF.Abs(Vector3.Dot(box.Right, axis)) + MathF.Abs(Vector3.Dot(box.Up, axis)) +
                    MathF.Abs(Vector3.Dot(box.Forward, axis)) + padding * (MathF.Abs(axis.X) + MathF.Abs(axis.Z));
                return MathF.Abs(Vector3.Dot(distance, axis)) <= support;
            }
        }
    }
}

public static class NavigationBaker
{
    private const float Epsilon = .001f;
    public const int MaxCells = 1_000_000;

    private readonly record struct HeightSpan(float Min, float Max, float? Floor, Vector2 Gradient);

    public static NavigationData Bake(NavigationBakeInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Triangles);
        var settings = input.Settings;
        var (columns, rows) = Measure(settings);
        var surface = new List<HeightSpan>?[columns * rows];
        var solidColumns = new List<Matrix4x4>?[surface.Length];
        var cos = MathF.Cos(settings.MaxSlope * MathF.PI / 180);

        foreach (var triangle in input.Triangles)
            Raster(triangle);
        foreach (var volume in input.SolidVolumes ?? [])
        {
            if (!Matrix4x4.Invert(volume.UnitToWorld, out var inverse))
                continue;
            var extent = Vector3.Abs(volume.Right) + Vector3.Abs(volume.Up) + Vector3.Abs(volume.Forward);
            var (firstX, lastX, firstZ, lastZ) = CellBounds(volume.Center - extent, volume.Center + extent);
            for (var z = firstZ; z <= lastZ; z++)
            for (var x = firstX; x <= lastX; x++)
                (solidColumns[z * columns + x] ??= []).Add(inverse);
        }
        var raw = new float?[surface.Length];
        var gradients = new Vector2[surface.Length];
        for (var i = 0; i < surface.Length; i++)
        {
            if (surface[i] is not { } spans)
                continue;
            // The first version stores one floor per column, selecting the lowest usable one.
            foreach (var span in spans)
                if (span.Floor is { } floor && (raw[i] is null || floor < raw[i]) && HasHeadroom(i, floor) &&
                    !spans.Any(other => (other.Max > floor + Epsilon && other.Min < floor + settings.Height &&
                        other.Floor is null && !(other.Min <= floor + Epsilon && other.Max <= floor + settings.MaxStep)) ||
                        other.Floor is { } upper && upper > floor + Epsilon &&
                        upper < floor + settings.Height))
                {
                    raw[i] = floor;
                    gradients[i] = span.Gradient;
                }
        }

        var heights = (float?[])raw.Clone();
        var clearance = (int)MathF.Ceiling(settings.Radius / settings.CellSize + .5f);
        for (var z = 0; z < rows; z++)
        for (var x = 0; x < columns; x++)
        {
            var index = z * columns + x;
            if (raw[index] is not { } floor)
                continue;
            for (var dz = -clearance; dz <= clearance && heights[index].HasValue; dz++)
            for (var dx = -clearance; dx <= clearance; dx++)
            {
                var distance = new Vector2(Math.Max(0, Math.Abs(dx) - .5f), Math.Max(0, Math.Abs(dz) - .5f));
                if (distance.LengthSquared() * settings.CellSize * settings.CellSize >= settings.Radius * settings.Radius)
                    continue;
                var neighborX = x + dx;
                var neighborZ = z + dz;
                if ((uint)neighborX >= (uint)columns || (uint)neighborZ >= (uint)rows ||
                    raw[neighborZ * columns + neighborX] is not { } neighbor ||
                    !Connect(floor, gradients[index], neighbor, gradients[neighborZ * columns + neighborX],
                        dx, dz, settings))
                {
                    heights[index] = null;
                    break;
                }
            }
        }
        var result = new NavigationData(settings, columns, rows, heights, BakedBlockers: input.Blockers);
        foreach (var blocker in input.Blockers ?? [])
            foreach (var index in result.BlockedCells(blocker).ToArray())
                heights[index] = null;
        var slopes = new Dictionary<int, Vector2>();
        for (var i = 0; i < heights.Length; i++)
            if (heights[i].HasValue && gradients[i].LengthSquared() > 1e-8f)
                slopes.Add(i, gradients[i]);
        if (slopes.Count > 0)
            result = result with { Gradients = slopes };
        return input.Obstacles is { Length: > 0 } obstacles
            ? result with { PreviewBlocked = obstacles.SelectMany(result.BlockedCells).Distinct().Order().ToArray() }
            : result;

        bool HasHeadroom(int index, float floor)
        {
            if (solidColumns[index] is not { } solids)
                return true;
            var start = new Vector3(settings.Min.X + (index % columns + .5f) * settings.CellSize,
                floor + Epsilon, settings.Min.Z + (index / columns + .5f) * settings.CellSize);
            foreach (var inverse in solids)
            {
                var origin = Vector3.Transform(start, inverse);
                var up = Vector3.TransformNormal(Vector3.UnitY, inverse);
                var enter = 0f;
                var exit = settings.Height - Epsilon;
                for (var axis = 0; axis < 3 && enter < exit; axis++)
                {
                    if (MathF.Abs(up[axis]) < 1e-9f)
                    {
                        if (MathF.Abs(origin[axis]) >= 1)
                            exit = enter;
                        continue;
                    }
                    var a = (-1 - origin[axis]) / up[axis];
                    var b = (1 - origin[axis]) / up[axis];
                    enter = Math.Max(enter, Math.Min(a, b));
                    exit = Math.Min(exit, Math.Max(a, b));
                }
                if (enter < exit)
                    return false;
            }
            return true;
        }

        (int FirstX, int LastX, int FirstZ, int LastZ) CellBounds(Vector3 low, Vector3 high)
        {
            var cell = settings.CellSize;
            return (Math.Max(0, (int)MathF.Floor((low.X - settings.Min.X) / cell - Epsilon)),
                Math.Min(columns - 1, (int)MathF.Floor((high.X - settings.Min.X) / cell + Epsilon)),
                Math.Max(0, (int)MathF.Floor((low.Z - settings.Min.Z) / cell - Epsilon)),
                Math.Min(rows - 1, (int)MathF.Floor((high.Z - settings.Min.Z) / cell + Epsilon)));
        }

        void Raster(NavigationTriangle triangle)
        {
            var (a, b, c, maySupport) = triangle;
            if (!IsFinite(a) || !IsFinite(b) || !IsFinite(c))
                throw new ArgumentException("Navigation geometry contains a non-finite vertex.");
            var normal = Vector3.Cross(b - a, c - a);
            var normalLength = normal.Length();
            if (normalLength <= 1e-8f)
                return;
            var low = Vector3.Min(Vector3.Min(a, b), c);
            var high = Vector3.Max(Vector3.Max(a, b), c);
            if (high.Y < settings.Min.Y || low.Y > settings.Max.Y || high.X < settings.Min.X ||
                low.X > settings.Max.X || high.Z < settings.Min.Z || low.Z > settings.Max.Z)
                return;
            var cell = settings.CellSize;
            var (firstX, lastX, firstZ, lastZ) = CellBounds(low, high);
            var supports = maySupport != false && normal.Y / normalLength >= cos;
            var gradient = supports ? new Vector2(-normal.X, -normal.Z) / normal.Y : Vector2.Zero;
            Span<Vector3> polygon = stackalloc Vector3[8];
            Span<Vector3> buffer = stackalloc Vector3[8];
            for (var z = firstZ; z <= lastZ; z++)
            for (var x = firstX; x <= lastX; x++)
            {
                var left = settings.Min.X + x * cell;
                var front = settings.Min.Z + z * cell;
                polygon[0] = a;
                polygon[1] = b;
                polygon[2] = c;
                var count = Clip(polygon[..3], buffer, 0, left, true);
                count = Clip(buffer[..count], polygon, 0, left + cell, false);
                count = Clip(polygon[..count], buffer, 2, front, true);
                count = Clip(buffer[..count], polygon, 2, front + cell, false);
                if (count == 0)
                    continue;
                var min = polygon[0].Y;
                var max = min;
                for (var i = 1; i < count; i++)
                {
                    min = Math.Min(min, polygon[i].Y);
                    max = Math.Max(max, polygon[i].Y);
                }
                var mid = new Vector3(left + cell * .5f, 0, front + cell * .5f);
                float? height = null;
                if (supports && ContainsXZ(a, b, c, mid))
                {
                    var candidate = a.Y + Vector2.Dot(gradient, new(mid.X - a.X, mid.Z - a.Z));
                    if (candidate >= settings.Min.Y && candidate <= settings.Max.Y)
                        height = candidate;
                }
                // Adjacent triangles of a slope can cross the cell without owning its center.
                // Keep their supporting classification so they do not become false ceilings.
                if (supports && height is null)
                    continue;
                (surface[z * columns + x] ??= []).Add(new(min, max, height, gradient));
            }
        }
    }

    private static bool ContainsXZ(Vector3 a, Vector3 b, Vector3 c, Vector3 p)
    {
        static float Side(Vector3 u, Vector3 v, Vector3 p) =>
            (v.X - u.X) * (p.Z - u.Z) - (v.Z - u.Z) * (p.X - u.X);
        return Side(a, b, p) <= 1e-6f && Side(b, c, p) <= 1e-6f && Side(c, a, p) <= 1e-6f;
    }

    private static int Clip(ReadOnlySpan<Vector3> source, Span<Vector3> output, int axis, float boundary, bool above)
    {
        if (source.IsEmpty)
            return 0;
        var count = 0;
        var previous = source[^1];
        var last = (previous[axis] - boundary) * (above ? 1 : -1);
        foreach (var vertex in source)
        {
            var current = (vertex[axis] - boundary) * (above ? 1 : -1);
            if ((current > 0) != (last > 0) && current != 0 && last != 0)
                output[count++] = Vector3.Lerp(previous, vertex, last / (last - current));
            if (current >= 0)
                output[count++] = vertex;
            previous = vertex;
            last = current;
        }
        return count;
    }

    internal static (int Columns, int Rows) Measure(NavigationBakeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var size = settings.Max - settings.Min;
        if (!IsFinite(size) || !IsFinite(settings.Min) || size.X <= 0 || size.Y <= 0 || size.Z <= 0 ||
            !float.IsFinite(settings.CellSize) || settings.CellSize < .025f ||
            !float.IsFinite(settings.Radius) || settings.Radius < 0 ||
            !float.IsFinite(settings.Height) || settings.Height <= 0 ||
            !float.IsFinite(settings.MaxSlope) || settings.MaxSlope is < 0 or > 70 ||
            !float.IsFinite(settings.MaxStep) || settings.MaxStep < 0)
            throw new ArgumentException("Invalid navigation bounds or agent settings.");
        var columns = Math.Floor(size.X / settings.CellSize + 1e-5);
        var rows = Math.Floor(size.Z / settings.CellSize + 1e-5);
        if (columns < 1 || rows < 1 || columns * rows > MaxCells ||
            settings.Radius / settings.CellSize > 64)
            throw new ArgumentException("Navigation bounds or clearance are too large for the cell size.");
        return ((int)columns, (int)rows);
    }

    internal static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    internal static bool Connect(float from, Vector2 fromGradient, float to, Vector2 toGradient,
        int dx, int dz, NavigationBakeSettings settings)
    {
        var half = new Vector2(dx, dz) * (settings.CellSize * .5f);
        var delta = to - from - Vector2.Dot(fromGradient + toGradient, half);
        return MathF.Abs(delta) <= settings.MaxStep * Math.Max(1, Math.Max(Math.Abs(dx), Math.Abs(dz))) + Epsilon;
    }
}
