using Vecxy.Isometric;

ConfigurationIsValidated();
PositiveCoordinatesAreDecomposed();
NegativeCoordinatesUseFloorDivision();
CoordinateConversionRoundTrips();
WorldStorageIsSparseAndStable();
LevelsAreSparseAndIndependent();
await ChunksAreSavedUnloadedAndRestored();
await LeasesPreventPrematureUnloading();
await DesiredChunkSetDrivesStreaming();
ObjectsSpanChunksAndRegions();
await ObjectsPersistIndependentlyFromChunks();
WallsUseCanonicalEdgesAndCrossBoundaries();
await WallsPersistIndependentlyFromChunks();

Console.WriteLine("All Vecxy.Isometric checks passed.");

static void ConfigurationIsValidated()
{
    Throws<ArgumentOutOfRangeException>(() => new WorldGridConfig(chunkWidth: 0));
    Throws<ArgumentOutOfRangeException>(() => new WorldGridConfig(chunkHeight: -1));
    Throws<ArgumentOutOfRangeException>(() => new WorldGridConfig(regionWidth: 0));
    Throws<ArgumentOutOfRangeException>(() => new WorldGridConfig(regionHeight: -1));
    Throws<ArgumentException>(() => new WallFootprint(
        [new WallSegmentOffset(0, 0, 0, (EWallAxis)byte.MaxValue)]));
}

static void PositiveCoordinatesAreDecomposed()
{
    var config = new WorldGridConfig(4, 3, 2, 2);
    var address = config.GetAddress(new TileCoord(12, 8, 4));

    Equal(new ChunkCoord(3, 2), address.Chunk, "positive chunk");
    Equal(new RegionCoord(1, 1), address.Region, "positive region");
    Equal(new LocalChunkCoord(1, 0), address.LocalChunk, "positive local chunk");
    Equal(new LocalTileCoord(0, 2), address.LocalTile, "positive local tile");
    Equal(4, address.Level, "level");
}

static void NegativeCoordinatesUseFloorDivision()
{
    var config = new WorldGridConfig(4, 3, 2, 2);
    var address = config.GetAddress(new TileCoord(-1, -1, -2));

    Equal(new ChunkCoord(-1, -1), address.Chunk, "negative chunk");
    Equal(new RegionCoord(-1, -1), address.Region, "negative region");
    Equal(new LocalChunkCoord(1, 1), address.LocalChunk, "negative local chunk");
    Equal(new LocalTileCoord(3, 2), address.LocalTile, "negative local tile");
    Equal(-2, address.Level, "negative level");
}

static void CoordinateConversionRoundTrips()
{
    var config = new WorldGridConfig(7, 5, 3, 4);
    TileCoord[] samples =
    [
        new(0, 0, 0),
        new(6, 4, 1),
        new(7, 5, 2),
        new(-1, -1, -1),
        new(-7, -5, 8),
        new(-8, 19, 3)
    ];

    foreach (var source in samples)
    {
        var address = config.GetAddress(source);
        var chunk = config.GetChunk(address.Region, address.LocalChunk);
        var tile = config.GetTile(chunk, address.LocalTile, address.Level);
        Equal(source, tile, $"round trip for {source}");
    }
}

static void WorldStorageIsSparseAndStable()
{
    var world = new World(new WorldGridConfig(4, 4, 2, 2));
    var coord = new TileCoord(-1, -1, 3);

    Check(world.Regions.Count == 0, "A new world eagerly created regions.");
    Check(!world.TryGetTile(coord, out _), "A lookup created a missing tile.");

    var first = world.GetOrCreateTile(coord);
    var second = world.GetOrCreateTile(coord);

    Check(ReferenceEquals(first, second), "Repeated tile creation changed identity.");
    Equal(coord, first.Coord, "stored tile coordinate");
    Check(world.Regions.Count == 1, "One tile created an unexpected number of regions.");
    Check(world.Regions.Single().Chunks.Count == 1, "One tile created an unexpected number of chunks.");
}

static void LevelsAreSparseAndIndependent()
{
    var world = new World(new WorldGridConfig(4, 4, 2, 2));
    var ground = world.GetOrCreateTile(new TileCoord(1, 2, 0));
    var upperFloor = world.GetOrCreateTile(new TileCoord(1, 2, 4));
    ground.SetSurface(new TileSurface(ETileSurface.Ground));
    upperFloor.SetSurface(new TileSurface(ETileSurface.Floor));

    Check(!ReferenceEquals(ground, upperFloor), "Different levels share one tile instance.");
    Equal(ETileSurface.Ground, ground.Surface!.Type, "ground surface");
    Equal(ETileSurface.Floor, upperFloor.Surface!.Type, "upper floor surface");
    Check(!world.TryGetTile(new TileCoord(1, 2, 1), out _), "An unused intermediate level was created.");

    var chunk = world.GetOrCreateChunk(new ChunkCoord(0, 0));
    var column = chunk.GetColumn(new LocalTileCoord(1, 2));
    Check(column.Levels.Select(tile => tile.Coord.Level).SequenceEqual([0, 4]),
        "Column levels are not sparse and ordered.");
}

static async Task ChunksAreSavedUnloadedAndRestored()
{
    var config = new WorldGridConfig(4, 4, 2, 2);
    var storage = new InMemoryWorldStorage();
    var sourceWorld = new World(config);
    await using var sourceStreamer = new WorldStreamer(sourceWorld, storage);
    var coord = new TileCoord(-1, 5, 2);
    var chunkCoord = config.GetChunk(coord);
    var tile = sourceWorld.GetOrCreateTile(coord);
    tile.SetSurface(new TileSurface(ETileSurface.Floor));

    Check(sourceStreamer.GetInfo(chunkCoord).IsDirty, "A changed chunk was not marked dirty.");
    Check(await sourceStreamer.SaveChunkAsync(chunkCoord), "A loaded chunk was not saved.");
    Check(storage.Count == 1, "Storage did not receive the chunk snapshot.");
    Check(!sourceStreamer.GetInfo(chunkCoord).IsDirty, "A saved chunk remained dirty.");

    tile.SetSurface(new TileSurface(ETileSurface.Floor));
    Check(!sourceStreamer.GetInfo(chunkCoord).IsDirty, "An equivalent surface dirtied the chunk.");
    tile.SetSurface(new TileSurface(ETileSurface.Ground));
    Check(sourceStreamer.GetInfo(chunkCoord).IsDirty, "A surface change did not dirty the chunk.");
    Check(await sourceStreamer.UnloadChunkAsync(chunkCoord), "A dirty chunk was not saved and unloaded.");
    Check(!sourceWorld.TryGetChunk(chunkCoord, out _), "An unloaded chunk remained in the world.");

    var restoredWorld = new World(config);
    await using var restoredStreamer = new WorldStreamer(restoredWorld, storage);
    var restoredChunk = await restoredStreamer.LoadChunkAsync(chunkCoord);
    Check(restoredChunk.TryGetTile(coord, out var restoredTile), "The saved tile was not restored.");
    Equal(ETileSurface.Ground, restoredTile!.Surface!.Type, "restored surface");
    Check(!restoredChunk.IsDirty, "A restored chunk was marked dirty.");
}

static async Task LeasesPreventPrematureUnloading()
{
    var world = new World(new WorldGridConfig(4, 4, 2, 2));
    var storage = new InMemoryWorldStorage();
    await using var streamer = new WorldStreamer(world, storage);
    var coord = new ChunkCoord(3, -2);
    using var lease = await streamer.AcquireChunkAsync(coord);

    Equal(1, streamer.GetInfo(coord).LeaseCount, "lease count");
    Check(!await streamer.UnloadChunkAsync(coord), "A leased chunk was unloaded.");
    lease.Dispose();
    Equal(0, streamer.GetInfo(coord).LeaseCount, "released lease count");
    Check(await streamer.UnloadChunkAsync(coord), "A released chunk was not unloaded.");
}

static async Task DesiredChunkSetDrivesStreaming()
{
    var world = new World(new WorldGridConfig(4, 4, 2, 2));
    var storage = new InMemoryWorldStorage();
    await using var streamer = new WorldStreamer(world, storage);
    var first = new ChunkCoord(0, 0);
    var second = new ChunkCoord(1, 0);

    await streamer.SynchronizeAsync([first, second]);
    Check(world.TryGetChunk(first, out _) && world.TryGetChunk(second, out _),
        "The desired chunk set was not loaded.");

    await streamer.SynchronizeAsync([second]);
    Check(!world.TryGetChunk(first, out _) && world.TryGetChunk(second, out _),
        "Chunks outside the desired set were not unloaded.");
    Equal(EChunkStreamState.Unloaded, streamer.GetInfo(first).State, "evicted chunk state");
    Equal(EChunkStreamState.Loaded, streamer.GetInfo(second).State, "retained chunk state");
}

static void ObjectsSpanChunksAndRegions()
{
    var config = new WorldGridConfig(2, 2, 2, 2);
    var world = new World(config);
    var footprint = ObjectFootprint.Rectangle(3, 2);
    var instance = world.PlaceObject("furniture.sofa", footprint, new TileCoord(3, -1, 1));
    var originalTiles = instance.GetOccupiedTiles().ToArray();

    Equal(6, originalTiles.Length, "sofa footprint size");
    Equal(4, originalTiles.Select(config.GetChunk).Distinct().Count(), "covered chunk count");
    Equal(4, originalTiles.Select(config.GetAddress).Select(address => address.Region).Distinct().Count(),
        "covered region count");
    Check(originalTiles.All(coord =>
        world.TryGetTile(coord, out var tile) && tile!.Occupants.Contains(instance.Id)),
        "The object was not indexed in every occupied tile.");

    Throws<ObjectPlacementException>(() => world.PlaceObject(
        "furniture.table",
        ObjectFootprint.Rectangle(1, 1),
        new TileCoord(4, 0, 1)));

    world.MoveObject(instance.Id, new TileCoord(-5, 7, 2), EGridRotation.Clockwise90);
    var movedTiles = instance.GetOccupiedTiles().ToArray();
    Check(originalTiles.All(coord =>
        world.TryGetTile(coord, out var tile) && !tile!.Occupants.Contains(instance.Id)),
        "Moving left stale occupancy behind.");
    Check(movedTiles.All(coord =>
        world.TryGetTile(coord, out var tile) && tile!.Occupants.Contains(instance.Id)),
        "Moving did not rebuild occupancy.");
    Equal(new TileCoord(-5, 7, 2), instance.Anchor, "moved anchor");
    Equal(EGridRotation.Clockwise90, instance.Rotation, "moved rotation");

    Check(world.RemoveObject(instance.Id), "The object was not removed.");
    Check(!world.TryGetObject(instance.Id, out _), "The removed object remained in the registry.");
    Check(movedTiles.All(coord =>
        world.TryGetTile(coord, out var tile) && tile!.Occupants.Count == 0),
        "Removing left stale occupancy behind.");
}

static async Task ObjectsPersistIndependentlyFromChunks()
{
    var config = new WorldGridConfig(4, 4, 2, 2);
    var storage = new InMemoryWorldStorage();
    var sourceWorld = new World(config);
    await using var sourceStreamer = new WorldStreamer(sourceWorld, storage);
    var instance = sourceWorld.PlaceObject(
        "furniture.sofa",
        ObjectFootprint.Rectangle(3, 1),
        new TileCoord(3, 1, 0));
    var chunkCoord = config.GetChunk(instance.Anchor);

    Equal(1, sourceStreamer.GetInfo(chunkCoord).ResidentObjectCount, "resident object count");
    Check(await sourceStreamer.UnloadChunkAsync(chunkCoord),
        "A chunk with a catalogued object was not unloaded.");
    Check(!sourceWorld.TryGetChunk(chunkCoord, out _), "The object's chunk remained loaded.");
    Check(sourceWorld.TryGetObject(instance.Id, out _), "Unloading a chunk removed global object metadata.");
    Throws<ObjectPlacementException>(() => sourceWorld.PlaceObject(
        "furniture.chair",
        ObjectFootprint.Rectangle(1, 1),
        instance.Anchor));

    var oldAnchor = instance.Anchor;
    var newAnchor = new TileCoord(20, 20, 2);
    sourceWorld.MoveObject(instance.Id, newAnchor, EGridRotation.Clockwise180);
    Check(sourceWorld.CanPlaceObject(ObjectFootprint.Rectangle(1, 1), oldAnchor),
        "Moving did not release globally indexed occupancy in an unloaded chunk.");
    await sourceStreamer.SaveObjectCatalogAsync();

    var restoredWorld = new World(config);
    await using var restoredStreamer = new WorldStreamer(restoredWorld, storage);
    await restoredStreamer.LoadChunkAsync(chunkCoord);
    Check(restoredWorld.TryGetObject(instance.Id, out var restored), "The object catalog was not restored.");
    Equal(instance.DefinitionId, restored!.DefinitionId, "restored object definition");
    Equal(newAnchor, restored.Anchor, "restored object anchor");
    Check(!restoredWorld.TryGetTile(oldAnchor, out var oldTile) || !oldTile!.Occupants.Contains(instance.Id),
        "The old chunk rebuilt stale object occupancy.");

    await restoredStreamer.LoadChunkAsync(config.GetChunk(newAnchor));
    Check(restoredWorld.TryGetTile(newAnchor, out var tile) && tile!.Occupants.Contains(instance.Id),
        "The loaded chunk did not rebuild object occupancy.");
}

static void WallsUseCanonicalEdgesAndCrossBoundaries()
{
    var config = new WorldGridConfig(2, 2, 2, 2);
    var world = new World(config);
    var wall = world.PlaceWall(
        "walls.brick",
        WallFootprint.Line(5, EWallAxis.AlongX),
        new WallAnchorCoord(3, 0, 1));
    var originalSegments = wall.GetOccupiedSegments().ToArray();

    Equal(5, originalSegments.Length, "wall segment count");
    Check(originalSegments.All(segment =>
        world.TryGetWallAt(segment, out var indexed) && ReferenceEquals(indexed, wall)),
        "The wall was not indexed at every segment.");
    Throws<ArgumentException>(() => world.PlaceObject(
        "furniture.invalid-id",
        ObjectFootprint.Rectangle(1, 1),
        new TileCoord(20, 20, 0),
        id: wall.Id));
    Equal(6, originalSegments
        .SelectMany(segment => segment.GetAdjacentTiles())
        .Select(config.GetChunk)
        .Distinct()
        .Count(), "wall chunk coverage");

    Throws<WallPlacementException>(() => world.PlaceWall(
        "walls.wood",
        WallFootprint.Line(1, EWallAxis.AlongX),
        new WallAnchorCoord(4, 0, 1)));

    world.MoveWall(wall.Id, new WallAnchorCoord(-5, 7, 2), EGridRotation.Clockwise90);
    var movedSegments = wall.GetOccupiedSegments().ToArray();
    Check(originalSegments.All(segment => !world.TryGetWallAt(segment, out _)),
        "Moving left stale wall segments behind.");
    Check(movedSegments.All(segment => world.TryGetWallAt(segment, out var indexed) && indexed!.Id == wall.Id),
        "Moving did not rebuild the wall index.");
    Check(movedSegments.All(segment => segment.Axis == EWallAxis.AlongY),
        "Rotating an X wall did not produce canonical Y segments.");

    Check(world.RemoveWall(wall.Id), "The wall was not removed.");
    Check(movedSegments.All(segment => !world.TryGetWallAt(segment, out _)),
        "Removing left stale wall segments behind.");
}

static async Task WallsPersistIndependentlyFromChunks()
{
    var config = new WorldGridConfig(4, 4, 2, 2);
    var storage = new InMemoryWorldStorage();
    var sourceWorld = new World(config);
    await using var sourceStreamer = new WorldStreamer(sourceWorld, storage);
    var wall = sourceWorld.PlaceWall(
        "walls.plaster",
        WallFootprint.Line(5, EWallAxis.AlongY),
        new WallAnchorCoord(0, 3, 0));
    var segment = wall.GetOccupiedSegments().First();
    var chunkCoord = config.GetChunk(segment.GetAdjacentTiles().First());

    Equal(1, sourceStreamer.GetInfo(chunkCoord).ResidentWallCount, "resident wall count");
    Check(await sourceStreamer.UnloadChunkAsync(chunkCoord), "A wall chunk was not unloaded.");

    var restoredWorld = new World(config);
    await using var restoredStreamer = new WorldStreamer(restoredWorld, storage);
    await restoredStreamer.LoadChunkAsync(chunkCoord);
    Check(restoredWorld.TryGetWall(wall.Id, out var restored), "The wall catalog was not restored.");
    Equal(5, restored!.GetOccupiedSegments().Count(), "restored wall length");
    Check(restoredWorld.TryGetWallAt(segment, out var indexed) && indexed!.Id == wall.Id,
        "The loaded chunk did not rebuild its wall index.");
}

static void Equal<T>(T expected, T actual, string message)
    where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{message}: expected {expected}, got {actual}.");
}

static void Check(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void Throws<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}
