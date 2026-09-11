namespace Vecxy.Isometric;

public enum EWallAxis : byte
{
    AlongX,
    AlongY
}

public readonly record struct WallAnchorCoord(int X, int Y, int Level = 0);

public readonly record struct WallSegmentCoord(int X, int Y, int Level, EWallAxis Axis)
{
    public IEnumerable<TileCoord> GetAdjacentTiles()
    {
        yield return new TileCoord(X, Y, Level);
        yield return Axis == EWallAxis.AlongX
            ? new TileCoord(X, checked(Y - 1), Level)
            : new TileCoord(checked(X - 1), Y, Level);
    }
}

public readonly record struct WallSegmentOffset(int X, int Y, int Level, EWallAxis Axis)
{
    public WallSegmentOffset Rotate(EGridRotation rotation)
    {
        var endX = Axis == EWallAxis.AlongX ? checked(X + 1) : X;
        var endY = Axis == EWallAxis.AlongY ? checked(Y + 1) : Y;
        var start = RotatePoint(X, Y, rotation);
        var end = RotatePoint(endX, endY, rotation);
        return new WallSegmentOffset(
            Math.Min(start.X, end.X),
            Math.Min(start.Y, end.Y),
            Level,
            start.X != end.X ? EWallAxis.AlongX : EWallAxis.AlongY);
    }

    private static (int X, int Y) RotatePoint(int x, int y, EGridRotation rotation) => rotation switch
    {
        EGridRotation.None => (x, y),
        EGridRotation.Clockwise90 => (checked(-y), x),
        EGridRotation.Clockwise180 => (checked(-x), checked(-y)),
        EGridRotation.Clockwise270 => (y, checked(-x)),
        _ => throw new ArgumentOutOfRangeException(nameof(rotation))
    };
}

public sealed class WallFootprint
{
    private readonly WallSegmentOffset[] _segments;

    public WallFootprint(IEnumerable<WallSegmentOffset> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        _segments = segments.Distinct().ToArray();
        if (_segments.Length == 0)
            throw new ArgumentException("A wall footprint must contain at least one segment.", nameof(segments));
        if (_segments.Any(segment => !Enum.IsDefined(segment.Axis)))
            throw new ArgumentException("A wall footprint contains an unknown axis.", nameof(segments));
    }

    public IReadOnlyList<WallSegmentOffset> Segments => _segments;

    public IEnumerable<WallSegmentCoord> GetOccupiedSegments(
        WallAnchorCoord anchor,
        EGridRotation rotation = EGridRotation.None)
    {
        foreach (var source in _segments)
        {
            var segment = source.Rotate(rotation);
            yield return new WallSegmentCoord(
                checked(anchor.X + segment.X),
                checked(anchor.Y + segment.Y),
                checked(anchor.Level + segment.Level),
                segment.Axis);
        }
    }

    public static WallFootprint Line(int length, EWallAxis axis)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        var segments = new WallSegmentOffset[length];
        for (var index = 0; index < length; index++)
        {
            segments[index] = axis == EWallAxis.AlongX
                ? new WallSegmentOffset(index, 0, 0, axis)
                : new WallSegmentOffset(0, index, 0, axis);
        }
        return new WallFootprint(segments);
    }
}

public sealed class WallInstance
{
    internal WallInstance(
        Guid id,
        string definitionId,
        WallFootprint footprint,
        WallAnchorCoord anchor,
        EGridRotation rotation)
    {
        Id = id;
        DefinitionId = definitionId;
        Footprint = footprint;
        Anchor = anchor;
        Rotation = rotation;
    }

    public Guid Id { get; }
    public string DefinitionId { get; }
    public WallFootprint Footprint { get; }
    public WallAnchorCoord Anchor { get; internal set; }
    public EGridRotation Rotation { get; internal set; }

    public IEnumerable<WallSegmentCoord> GetOccupiedSegments() =>
        Footprint.GetOccupiedSegments(Anchor, Rotation);
}

public sealed class WallPlacementException(string message) : InvalidOperationException(message);
