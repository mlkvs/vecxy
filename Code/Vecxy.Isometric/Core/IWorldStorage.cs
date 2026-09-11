namespace Vecxy.Isometric;

public interface IWorldStorage
{
    ValueTask<ChunkData?> LoadChunkAsync(
        ChunkCoord coord,
        CancellationToken cancellationToken = default);

    ValueTask SaveChunkAsync(
        ChunkData chunk,
        CancellationToken cancellationToken = default);

    ValueTask<WorldObjectData[]> LoadObjectCatalogAsync(
        CancellationToken cancellationToken = default);

    ValueTask SaveObjectCatalogAsync(
        WorldObjectData[] objects,
        CancellationToken cancellationToken = default);

    ValueTask<WallData[]> LoadWallCatalogAsync(
        CancellationToken cancellationToken = default);

    ValueTask SaveWallCatalogAsync(
        WallData[] walls,
        CancellationToken cancellationToken = default);
}

public sealed class InMemoryWorldStorage : IWorldStorage
{
    private readonly Dictionary<ChunkCoord, ChunkData> _chunks = [];
    private readonly Lock _lock = new();
    private WorldObjectData[] _objects = [];
    private WallData[] _walls = [];

    public int Count
    {
        get
        {
            lock (_lock)
                return _chunks.Count;
        }
    }

    public ValueTask<ChunkData?> LoadChunkAsync(
        ChunkCoord coord,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_lock)
        {
            return ValueTask.FromResult(
                _chunks.TryGetValue(coord, out var chunk)
                    ? Clone(chunk)
                    : null);
        }
    }

    public ValueTask SaveChunkAsync(
        ChunkData chunk,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_lock)
            _chunks[chunk.Coord] = Clone(chunk);
        return ValueTask.CompletedTask;
    }

    public ValueTask<WorldObjectData[]> LoadObjectCatalogAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_lock)
            return ValueTask.FromResult(_objects.Select(Clone).ToArray());
    }

    public ValueTask SaveObjectCatalogAsync(
        WorldObjectData[] objects,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(objects);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_lock)
            _objects = objects.Select(Clone).ToArray();
        return ValueTask.CompletedTask;
    }

    public ValueTask<WallData[]> LoadWallCatalogAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_lock)
            return ValueTask.FromResult(_walls.Select(Clone).ToArray());
    }

    public ValueTask SaveWallCatalogAsync(
        WallData[] walls,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(walls);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_lock)
            _walls = walls.Select(Clone).ToArray();
        return ValueTask.CompletedTask;
    }

    private static ChunkData Clone(ChunkData chunk) =>
        new(chunk.Coord, [.. chunk.Tiles]);

    private static WorldObjectData Clone(WorldObjectData instance) =>
        instance with { Footprint = [.. instance.Footprint] };

    private static WallData Clone(WallData wall) =>
        wall with { Footprint = [.. wall.Footprint] };
}
