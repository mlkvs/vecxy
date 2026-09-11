namespace Vecxy.Isometric;

public sealed class TileColumn
{
    private readonly SortedDictionary<int, Tile> _levels = [];
    private readonly Action _onChanged;

    internal TileColumn(int x, int y, Action onChanged)
    {
        X = x;
        Y = y;
        _onChanged = onChanged;
    }

    public int X { get; }
    public int Y { get; }
    public IReadOnlyCollection<Tile> Levels => _levels.Values;

    public bool TryGetLevel(int level, out Tile? tile) =>
        _levels.TryGetValue(level, out tile);

    internal Tile GetOrCreateLevel(int level)
    {
        if (_levels.TryGetValue(level, out var tile))
            return tile;

        tile = new Tile(new TileCoord(X, Y, level), _onChanged);
        _levels.Add(level, tile);
        _onChanged();
        return tile;
    }
}
