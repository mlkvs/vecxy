using System.Numerics;

namespace Vecxy.Isometric;

public sealed class World
{
    private readonly Dictionary<RegionCoord, Region> _regions = [];
    private readonly Dictionary<Guid, IsometricObject> _objects = [];
    private readonly Dictionary<ChunkCoord, HashSet<Guid>> _objectIdsByChunk = [];
    private readonly Dictionary<TileCoord, Guid> _objectIdByTile = [];
    private readonly Dictionary<Guid, WallInstance> _walls = [];
    private readonly Dictionary<WallSegmentCoord, Guid> _wallIdBySegment = [];
    private readonly Dictionary<ChunkCoord, HashSet<Guid>> _wallIdsByChunk = [];

    public World(WorldGridConfig? config = null)
    {
        Config = config ?? new WorldGridConfig();
    }

    public WorldGridConfig Config { get; }
    public IReadOnlyCollection<Region> Regions => _regions.Values;
    public IReadOnlyCollection<IsometricObject> Objects => _objects.Values;
    public long ObjectCatalogVersion { get; private set; }
    public bool IsObjectCatalogDirty { get; private set; }
    public IReadOnlyCollection<WallInstance> Walls => _walls.Values;
    public long WallCatalogVersion { get; private set; }
    public bool IsWallCatalogDirty { get; private set; }

    public bool TryGetRegion(RegionCoord coord, out Region? region) =>
        _regions.TryGetValue(coord, out region);

    public Region GetOrCreateRegion(RegionCoord coord)
    {
        if (_regions.TryGetValue(coord, out var region))
            return region;

        region = new Region(coord, Config);
        _regions.Add(coord, region);
        return region;
    }

    public bool TryGetChunk(ChunkCoord coord, out Chunk? chunk)
    {
        var regionCoord = Config.GetRegion(coord);
        if (_regions.TryGetValue(regionCoord, out var region))
            return region.TryGetChunk(coord, out chunk);

        chunk = null;
        return false;
    }

    public Chunk GetOrCreateChunk(ChunkCoord coord) =>
        GetOrCreateRegion(Config.GetRegion(coord)).GetOrCreateChunk(coord);

    public bool TryGetTile(TileCoord coord, out Tile? tile)
    {
        if (TryGetChunk(Config.GetChunk(coord), out var chunk) && chunk is not null)
            return chunk.TryGetTile(coord, out tile);

        tile = null;
        return false;
    }

    public Tile GetOrCreateTile(TileCoord coord) =>
        GetOrCreateChunk(Config.GetChunk(coord)).GetOrCreateTile(coord);

    public bool TryGetObject(Guid id, out IsometricObject? instance) =>
        _objects.TryGetValue(id, out instance);

    public bool CanPlaceObject(
        ObjectFootprint footprint,
        TileCoord anchor,
        EGridRotation rotation = EGridRotation.None,
        Guid? ignoredObjectId = null)
    {
        ArgumentNullException.ThrowIfNull(footprint);
        foreach (var coord in footprint.GetOccupiedTiles(anchor, rotation))
        {
            if (_objectIdByTile.TryGetValue(coord, out var objectId) && objectId != ignoredObjectId)
                return false;
        }

        return true;
    }

    public IsometricObject PlaceObject(
        string definitionId,
        ObjectFootprint footprint,
        TileCoord anchor,
        EGridRotation rotation = EGridRotation.None,
        Guid? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        ArgumentNullException.ThrowIfNull(footprint);
        var objectId = id ?? Guid.NewGuid();
        if (objectId == Guid.Empty)
            throw new ArgumentException("Object ID cannot be empty.", nameof(id));
        if (_objects.ContainsKey(objectId) || _walls.ContainsKey(objectId))
            throw new ArgumentException($"Spatial entity {objectId} already exists.", nameof(id));

        var occupiedTiles = footprint.GetOccupiedTiles(anchor, rotation).ToArray();
        EnsurePlacementIsFree(occupiedTiles, null);

        var instance = new IsometricObject(objectId, definitionId, footprint, anchor, rotation);
        _objects.Add(objectId, instance);
        IndexObjectMetadata(objectId, occupiedTiles);
        IndexObject(instance.Id, occupiedTiles);
        MarkObjectCatalogDirty();
        return instance;
    }

    public void MoveObject(
        Guid id,
        TileCoord anchor,
        EGridRotation rotation = EGridRotation.None)
    {
        if (!_objects.TryGetValue(id, out var instance))
            throw new KeyNotFoundException($"Object {id} does not exist.");
        if (instance.Anchor == anchor && instance.Rotation == rotation)
            return;

        var oldTiles = instance.GetOccupiedTiles().ToArray();
        var newTiles = instance.Footprint.GetOccupiedTiles(anchor, rotation).ToArray();
        EnsurePlacementIsFree(newTiles, id);

        UnindexObject(id, oldTiles);
        UnindexObjectMetadata(id, oldTiles);
        instance.Anchor = anchor;
        instance.Rotation = rotation;
        IndexObjectMetadata(id, newTiles);
        IndexObject(id, newTiles);
        MarkObjectCatalogDirty();
    }

    public bool RemoveObject(Guid id)
    {
        if (!_objects.Remove(id, out var instance))
            return false;

        var occupiedTiles = instance.GetOccupiedTiles().ToArray();
        UnindexObject(id, occupiedTiles);
        UnindexObjectMetadata(id, occupiedTiles);
        MarkObjectCatalogDirty();
        return true;
    }

    public bool TryGetWall(Guid id, out WallInstance? wall) =>
        _walls.TryGetValue(id, out wall);

    public bool TryGetWallAt(WallSegmentCoord segment, out WallInstance? wall)
    {
        if (_wallIdBySegment.TryGetValue(segment, out var id))
            return _walls.TryGetValue(id, out wall);
        wall = null;
        return false;
    }

    public bool CanPlaceWall(
        WallFootprint footprint,
        WallAnchorCoord anchor,
        EGridRotation rotation = EGridRotation.None,
        Guid? ignoredWallId = null)
    {
        ArgumentNullException.ThrowIfNull(footprint);
        return footprint.GetOccupiedSegments(anchor, rotation).All(segment =>
            !_wallIdBySegment.TryGetValue(segment, out var id) || id == ignoredWallId);
    }

    public WallInstance PlaceWall(
        string definitionId,
        WallFootprint footprint,
        WallAnchorCoord anchor,
        EGridRotation rotation = EGridRotation.None,
        Guid? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        ArgumentNullException.ThrowIfNull(footprint);
        var wallId = id ?? Guid.NewGuid();
        if (wallId == Guid.Empty)
            throw new ArgumentException("Wall ID cannot be empty.", nameof(id));
        if (_walls.ContainsKey(wallId) || _objects.ContainsKey(wallId))
            throw new ArgumentException($"Spatial entity {wallId} already exists.", nameof(id));

        var segments = footprint.GetOccupiedSegments(anchor, rotation).ToArray();
        EnsureWallPlacementIsFree(segments, null);
        var wall = new WallInstance(wallId, definitionId, footprint, anchor, rotation);
        _walls.Add(wallId, wall);
        IndexWallMetadata(wallId, segments);
        IndexWall(wallId, segments);
        MarkWallCatalogDirty();
        return wall;
    }

    public void MoveWall(
        Guid id,
        WallAnchorCoord anchor,
        EGridRotation rotation = EGridRotation.None)
    {
        if (!_walls.TryGetValue(id, out var wall))
            throw new KeyNotFoundException($"Wall {id} does not exist.");
        if (wall.Anchor == anchor && wall.Rotation == rotation)
            return;

        var oldSegments = wall.GetOccupiedSegments().ToArray();
        var newSegments = wall.Footprint.GetOccupiedSegments(anchor, rotation).ToArray();
        EnsureWallPlacementIsFree(newSegments, id);
        UnindexWall(id, oldSegments);
        UnindexWallMetadata(id, oldSegments);
        wall.Anchor = anchor;
        wall.Rotation = rotation;
        IndexWallMetadata(id, newSegments);
        IndexWall(id, newSegments);
        MarkWallCatalogDirty();
    }

    public bool RemoveWall(Guid id)
    {
        if (!_walls.Remove(id, out var wall))
            return false;
        var segments = wall.GetOccupiedSegments().ToArray();
        UnindexWall(id, segments);
        UnindexWallMetadata(id, segments);
        MarkWallCatalogDirty();
        return true;
    }

    internal Chunk LoadChunk(ChunkData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (TryGetChunk(data.Coord, out _))
            throw new InvalidOperationException($"Chunk {data.Coord} is already loaded.");

        var chunk = GetOrCreateChunk(data.Coord);
        chunk.ApplyData(data);
        IndexObjectsInChunk(chunk);
        IndexWallsInChunk(chunk);
        chunk.MarkLoaded();
        return chunk;
    }

    internal void IndexObjectsInChunk(Chunk chunk)
    {
        if (!_objectIdsByChunk.TryGetValue(chunk.Coord, out var objectIds))
            return;

        foreach (var objectId in objectIds)
        {
            var instance = _objects[objectId];
            foreach (var tileCoord in instance.GetOccupiedTiles()
                         .Where(coord => Config.GetChunk(coord) == chunk.Coord))
            {
                chunk.GetOrCreateTile(tileCoord).AddOccupant(objectId);
            }

            chunk.RegisterObject(objectId);
        }
    }

    internal void IndexWallsInChunk(Chunk chunk)
    {
        if (!_wallIdsByChunk.TryGetValue(chunk.Coord, out var wallIds))
            return;
        foreach (var wallId in wallIds)
            chunk.RegisterWall(wallId);
    }

    internal WorldObjectData[] CaptureObjectCatalog() => _objects.Values
        .Select(instance => new WorldObjectData(
            instance.Id,
            instance.DefinitionId,
            [.. instance.Footprint.Cells],
            instance.Anchor,
            instance.Rotation))
        .ToArray();

    internal void LoadObjectCatalog(WorldObjectData[] objects)
    {
        ArgumentNullException.ThrowIfNull(objects);
        if (_objects.Count != 0)
            throw new InvalidOperationException("Cannot load an object catalog into a non-empty registry.");

        var occupied = new Dictionary<TileCoord, Guid>();
        foreach (var data in objects)
        {
            ArgumentNullException.ThrowIfNull(data);
            if (data.Id == Guid.Empty)
                throw new InvalidDataException("The object catalog contains an empty ID.");
            if (_objects.ContainsKey(data.Id) || _walls.ContainsKey(data.Id))
                throw new InvalidDataException($"The object catalog contains duplicate ID {data.Id}.");
            if (string.IsNullOrWhiteSpace(data.DefinitionId))
                throw new InvalidDataException($"Object {data.Id} has no definition ID.");

            ObjectFootprint footprint;
            try
            {
                footprint = new ObjectFootprint(data.Footprint);
            }
            catch (Exception exception) when (exception is ArgumentException or OverflowException)
            {
                throw new InvalidDataException($"Object {data.Id} has an invalid footprint.", exception);
            }

            var tiles = footprint.GetOccupiedTiles(data.Anchor, data.Rotation).ToArray();
            foreach (var tile in tiles)
            {
                if (occupied.TryGetValue(tile, out var blocker))
                    throw new InvalidDataException(
                        $"Objects {blocker} and {data.Id} overlap at tile {tile}.");
                occupied.Add(tile, data.Id);
            }

            var instance = new IsometricObject(
                data.Id,
                data.DefinitionId,
                footprint,
                data.Anchor,
                data.Rotation);
            _objects.Add(instance.Id, instance);
            IndexObjectMetadata(instance.Id, tiles);
        }

        foreach (var region in _regions.Values)
        foreach (var chunk in region.Chunks)
            IndexObjectsInChunk(chunk);

        IsObjectCatalogDirty = false;
    }

    internal void MarkObjectCatalogPersisted(long version)
    {
        if (ObjectCatalogVersion == version)
            IsObjectCatalogDirty = false;
    }

    internal WallData[] CaptureWallCatalog() => _walls.Values
        .Select(wall => new WallData(
            wall.Id,
            wall.DefinitionId,
            [.. wall.Footprint.Segments],
            wall.Anchor,
            wall.Rotation))
        .ToArray();

    internal void LoadWallCatalog(WallData[] walls)
    {
        ArgumentNullException.ThrowIfNull(walls);
        if (_walls.Count != 0)
            throw new InvalidOperationException("Cannot load a wall catalog into a non-empty registry.");

        foreach (var data in walls)
        {
            if (data.Id == Guid.Empty || _walls.ContainsKey(data.Id) || _objects.ContainsKey(data.Id))
                throw new InvalidDataException($"Wall catalog contains invalid ID {data.Id}.");
            if (string.IsNullOrWhiteSpace(data.DefinitionId))
                throw new InvalidDataException($"Wall {data.Id} has no definition ID.");
            WallFootprint footprint;
            try
            {
                footprint = new WallFootprint(data.Footprint);
            }
            catch (Exception exception) when (exception is ArgumentException or OverflowException)
            {
                throw new InvalidDataException($"Wall {data.Id} has an invalid footprint.", exception);
            }

            var segments = footprint.GetOccupiedSegments(data.Anchor, data.Rotation).ToArray();
            EnsureWallPlacementIsFree(segments, null);
            var wall = new WallInstance(data.Id, data.DefinitionId, footprint, data.Anchor, data.Rotation);
            _walls.Add(wall.Id, wall);
            IndexWallMetadata(wall.Id, segments);
        }

        foreach (var region in _regions.Values)
        foreach (var chunk in region.Chunks)
            IndexWallsInChunk(chunk);
        IsWallCatalogDirty = false;
    }

    internal void MarkWallCatalogPersisted(long version)
    {
        if (WallCatalogVersion == version)
            IsWallCatalogDirty = false;
    }

    internal bool UnloadChunk(ChunkCoord coord)
    {
        var regionCoord = Config.GetRegion(coord);
        if (!_regions.TryGetValue(regionCoord, out var region) || !region.RemoveChunk(coord))
            return false;

        if (region.Chunks.Count == 0)
            _regions.Remove(regionCoord);
        return true;
    }

    private void EnsurePlacementIsFree(IEnumerable<TileCoord> tiles, Guid? ignoredObjectId)
    {
        foreach (var coord in tiles)
        {
            if (_objectIdByTile.TryGetValue(coord, out var blocker) && blocker != ignoredObjectId)
                throw new ObjectPlacementException($"Tile {coord} is occupied by object {blocker}.");
        }
    }

    private void IndexObject(Guid objectId, IReadOnlyCollection<TileCoord> occupiedTiles)
    {
        foreach (var coord in occupiedTiles)
            GetOrCreateTile(coord).AddOccupant(objectId);

        foreach (var chunkCoord in occupiedTiles.Select(Config.GetChunk).Distinct())
            GetOrCreateChunk(chunkCoord).RegisterObject(objectId);
    }

    private void UnindexObject(Guid objectId, IReadOnlyCollection<TileCoord> occupiedTiles)
    {
        foreach (var coord in occupiedTiles)
        {
            if (TryGetTile(coord, out var tile) && tile is not null)
                tile.RemoveOccupant(objectId);
        }

        foreach (var chunkCoord in occupiedTiles.Select(Config.GetChunk).Distinct())
        {
            if (TryGetChunk(chunkCoord, out var chunk) && chunk is not null)
                chunk.UnregisterObject(objectId);
        }
    }

    private void IndexObjectMetadata(Guid objectId, IEnumerable<TileCoord> occupiedTiles)
    {
        var tiles = occupiedTiles.ToArray();
        foreach (var tile in tiles)
            _objectIdByTile.Add(tile, objectId);

        foreach (var chunkCoord in tiles.Select(Config.GetChunk).Distinct())
        {
            if (!_objectIdsByChunk.TryGetValue(chunkCoord, out var objectIds))
            {
                objectIds = [];
                _objectIdsByChunk.Add(chunkCoord, objectIds);
            }
            objectIds.Add(objectId);
        }
    }

    private void UnindexObjectMetadata(Guid objectId, IEnumerable<TileCoord> occupiedTiles)
    {
        var tiles = occupiedTiles.ToArray();
        foreach (var tile in tiles)
        {
            if (_objectIdByTile.TryGetValue(tile, out var indexedId) && indexedId == objectId)
                _objectIdByTile.Remove(tile);
        }

        foreach (var chunkCoord in tiles.Select(Config.GetChunk).Distinct())
        {
            if (!_objectIdsByChunk.TryGetValue(chunkCoord, out var objectIds))
                continue;
            objectIds.Remove(objectId);
            if (objectIds.Count == 0)
                _objectIdsByChunk.Remove(chunkCoord);
        }
    }

    private void MarkObjectCatalogDirty()
    {
        ObjectCatalogVersion++;
        IsObjectCatalogDirty = true;
    }

    private void EnsureWallPlacementIsFree(
        IEnumerable<WallSegmentCoord> segments,
        Guid? ignoredWallId)
    {
        foreach (var segment in segments)
        {
            if (_wallIdBySegment.TryGetValue(segment, out var blocker) && blocker != ignoredWallId)
                throw new WallPlacementException($"Wall segment {segment} is occupied by wall {blocker}.");
        }
    }

    private void IndexWall(Guid wallId, IReadOnlyCollection<WallSegmentCoord> segments)
    {
        foreach (var chunkCoord in GetWallChunks(segments))
            GetOrCreateChunk(chunkCoord).RegisterWall(wallId);
    }

    private void UnindexWall(Guid wallId, IReadOnlyCollection<WallSegmentCoord> segments)
    {
        foreach (var chunkCoord in GetWallChunks(segments))
        {
            if (TryGetChunk(chunkCoord, out var chunk) && chunk is not null)
                chunk.UnregisterWall(wallId);
        }
    }

    private void IndexWallMetadata(Guid wallId, IReadOnlyCollection<WallSegmentCoord> segments)
    {
        foreach (var segment in segments)
            _wallIdBySegment.Add(segment, wallId);
        foreach (var chunkCoord in GetWallChunks(segments))
        {
            if (!_wallIdsByChunk.TryGetValue(chunkCoord, out var wallIds))
            {
                wallIds = [];
                _wallIdsByChunk.Add(chunkCoord, wallIds);
            }
            wallIds.Add(wallId);
        }
    }

    private void UnindexWallMetadata(Guid wallId, IReadOnlyCollection<WallSegmentCoord> segments)
    {
        foreach (var segment in segments)
        {
            if (_wallIdBySegment.TryGetValue(segment, out var indexedId) && indexedId == wallId)
                _wallIdBySegment.Remove(segment);
        }
        foreach (var chunkCoord in GetWallChunks(segments))
        {
            if (!_wallIdsByChunk.TryGetValue(chunkCoord, out var wallIds))
                continue;
            wallIds.Remove(wallId);
            if (wallIds.Count == 0)
                _wallIdsByChunk.Remove(chunkCoord);
        }
    }

    private IEnumerable<ChunkCoord> GetWallChunks(IEnumerable<WallSegmentCoord> segments) =>
        segments
            .SelectMany(segment => segment.GetAdjacentTiles())
            .Select(Config.GetChunk)
            .Distinct();

    private void MarkWallCatalogDirty()
    {
        WallCatalogVersion++;
        IsWallCatalogDirty = true;
    }
}
