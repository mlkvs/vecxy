namespace Vecxy.Isometric;

public enum EGridRotation : byte
{
    None,
    Clockwise90,
    Clockwise180,
    Clockwise270
}

public readonly record struct TileOffset(int X, int Y, int Level = 0)
{
    public TileOffset Rotate(EGridRotation rotation) => rotation switch
    {
        EGridRotation.None => this,
        EGridRotation.Clockwise90 => new TileOffset(-Y, X, Level),
        EGridRotation.Clockwise180 => new TileOffset(-X, -Y, Level),
        EGridRotation.Clockwise270 => new TileOffset(Y, -X, Level),
        _ => throw new ArgumentOutOfRangeException(nameof(rotation))
    };
}

public sealed class ObjectFootprint
{
    private readonly TileOffset[] _cells;

    public ObjectFootprint(IEnumerable<TileOffset> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        _cells = cells.Distinct().ToArray();
        if (_cells.Length == 0)
            throw new ArgumentException("A footprint must contain at least one cell.", nameof(cells));
    }

    public IReadOnlyList<TileOffset> Cells => _cells;

    public IEnumerable<TileCoord> GetOccupiedTiles(
        TileCoord anchor,
        EGridRotation rotation = EGridRotation.None)
    {
        foreach (var source in _cells)
        {
            var cell = source.Rotate(rotation);
            yield return new TileCoord(
                checked(anchor.X + cell.X),
                checked(anchor.Y + cell.Y),
                checked(anchor.Level + cell.Level));
        }
    }

    public static ObjectFootprint Rectangle(int width, int height, int levelOffset = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        var cells = new TileOffset[checked(width * height)];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            cells[y * width + x] = new TileOffset(x, y, levelOffset);
        return new ObjectFootprint(cells);
    }
}

public sealed class ObjectPlacementException(string message) : InvalidOperationException(message);
