using System.Numerics;

namespace Vecxy.Isometric;

public sealed class Chunk
{
    private readonly TileColumn[,] _columns;
    private readonly WorldGridConfig _config;
    private readonly HashSet<Guid> _objectIds = [];
    private readonly HashSet<Guid> _wallIds = [];

    internal Chunk(ChunkCoord coord, WorldGridConfig config)
    {
        Coord = coord;
        Region = config.GetRegion(coord);
        _config = config;
        _columns = new TileColumn[config.ChunkWidth, config.ChunkHeight];

        for (var y = 0; y < config.ChunkHeight; y++)
        for (var x = 0; x < config.ChunkWidth; x++)
        {
            var tile = config.GetTile(coord, new LocalTileCoord(x, y));
            _columns[x, y] = new TileColumn(tile.X, tile.Y, MarkChanged);
        }

        IsDirty = true;
    }

    public ChunkCoord Coord { get; }
    public RegionCoord Region { get; }
    public int Width => _columns.GetLength(0);
    public int Height => _columns.GetLength(1);
    public bool IsDirty { get; private set; }
    public long ChangeVersion { get; private set; }
    public IReadOnlySet<Guid> ObjectIds => _objectIds;
    public IReadOnlySet<Guid> WallIds => _wallIds;

    public TileColumn GetColumn(LocalTileCoord coord)
    {
        if (!_config.Contains(coord))
            throw new ArgumentOutOfRangeException(nameof(coord));

        return _columns[coord.X, coord.Y];
    }

    public bool TryGetTile(TileCoord coord, out Tile? tile)
    {
        if (_config.GetChunk(coord) != Coord)
        {
            tile = null;
            return false;
        }

        return GetColumn(_config.GetLocalTile(coord)).TryGetLevel(coord.Level, out tile);
    }

    internal Tile GetOrCreateTile(TileCoord coord)
    {
        if (_config.GetChunk(coord) != Coord)
            throw new ArgumentOutOfRangeException(nameof(coord), "Tile belongs to another chunk.");

        return GetColumn(_config.GetLocalTile(coord)).GetOrCreateLevel(coord.Level);
    }

    internal void RegisterObject(Guid objectId)
    {
        _objectIds.Add(objectId);
    }

    internal void UnregisterObject(Guid objectId)
    {
        _objectIds.Remove(objectId);
    }

    internal void RegisterWall(Guid wallId) => _wallIds.Add(wallId);

    internal void UnregisterWall(Guid wallId) => _wallIds.Remove(wallId);

    internal ChunkData CaptureData()
    {
        var tiles = new List<TileData>();
        for (var y = 0; y < Height; y++)
        for (var x = 0; x < Width; x++)
        {
            foreach (var tile in _columns[x, y].Levels.Where(tile => tile.Surface is not null))
                tiles.Add(new TileData(tile.Coord, tile.Surface?.Type));
        }

        return new ChunkData(Coord, [.. tiles]);
    }

    internal void ApplyData(ChunkData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Coord != Coord)
            throw new ArgumentException("Chunk data belongs to another chunk.", nameof(data));

        foreach (var source in data.Tiles)
        {
            if (_config.GetChunk(source.Coord) != Coord)
                throw new ArgumentException("Chunk data contains a tile from another chunk.", nameof(data));

            var tile = GetOrCreateTile(source.Coord);
            tile.SetSurface(source.Surface is { } type ? new TileSurface(type) : null);
        }

        IsDirty = false;
    }

    internal void MarkPersisted(long version)
    {
        if (ChangeVersion == version)
            IsDirty = false;
    }

    internal void MarkLoaded() => IsDirty = false;

    private void MarkChanged()
    {
        ChangeVersion++;
        IsDirty = true;
    }
}
