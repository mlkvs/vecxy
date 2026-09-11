namespace Vecxy.Isometric;

public enum EChunkStreamState : byte
{
    Unloaded,
    Loading,
    Loaded,
    Saving,
    Failed
}

public readonly record struct ChunkStreamInfo(
    ChunkCoord Coord,
    EChunkStreamState State,
    int LeaseCount,
    int ResidentObjectCount,
    int ResidentWallCount,
    bool IsDirty,
    Exception? LastError);

public sealed class WorldStreamer : IAsyncDisposable
{
    private readonly Dictionary<ChunkCoord, Entry> _entries = [];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;
    private bool _catalogLoaded;
    private bool _wallCatalogLoaded;

    public WorldStreamer(World world, IWorldStorage storage)
    {
        World = world ?? throw new ArgumentNullException(nameof(world));
        Storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _catalogLoaded = world.Objects.Count != 0;
        _wallCatalogLoaded = world.Walls.Count != 0;

        foreach (var region in world.Regions)
        foreach (var chunk in region.Chunks)
            _entries.Add(chunk.Coord, new Entry(EChunkStreamState.Loaded));
    }

    public World World { get; }
    public IWorldStorage Storage { get; }

    public ChunkStreamInfo GetInfo(ChunkCoord coord)
    {
        ThrowIfDisposed();
        _gate.Wait();
        try
        {
            if (!_entries.TryGetValue(coord, out var entry))
            {
                if (!World.TryGetChunk(coord, out _))
                    return new ChunkStreamInfo(coord, EChunkStreamState.Unloaded, 0, 0, 0, false, null);

                entry = new Entry(EChunkStreamState.Loaded);
                _entries.Add(coord, entry);
            }

            var dirty = World.TryGetChunk(coord, out var chunk) && chunk is not null && chunk.IsDirty;
            var residentObjects = chunk?.ObjectIds.Count ?? 0;
            return new ChunkStreamInfo(
                coord,
                entry.State,
                entry.LeaseCount,
                residentObjects,
                chunk?.WallIds.Count ?? 0,
                dirty,
                entry.LastError);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<Chunk> LoadChunkAsync(
        ChunkCoord coord,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LoadCoreAsync(coord, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<ChunkLease> AcquireChunkAsync(
        ChunkCoord coord,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var chunk = await LoadCoreAsync(coord, cancellationToken).ConfigureAwait(false);
            var entry = GetOrCreateEntry(coord);
            entry.LeaseCount++;
            return new ChunkLease(this, chunk);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<bool> SaveChunkAsync(
        ChunkCoord coord,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await SaveCoreAsync(coord, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask SaveObjectCatalogAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureCatalogLoadedCoreAsync(cancellationToken).ConfigureAwait(false);
            await SaveObjectCatalogCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask SaveWallCatalogAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureWallCatalogLoadedCoreAsync(cancellationToken).ConfigureAwait(false);
            await SaveWallCatalogCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureCatalogLoadedCoreAsync(cancellationToken).ConfigureAwait(false);
            await EnsureWallCatalogLoadedCoreAsync(cancellationToken).ConfigureAwait(false);
            TrackWorldChunks();
            foreach (var coord in _entries
                         .Where(pair => pair.Value.State == EChunkStreamState.Loaded)
                         .Select(pair => pair.Key)
                         .ToArray())
            {
                if (World.TryGetChunk(coord, out var chunk) && chunk is not null && chunk.IsDirty)
                    await SaveChunkDataCoreAsync(coord, chunk, cancellationToken).ConfigureAwait(false);
            }

            if (World.IsObjectCatalogDirty)
                await SaveObjectCatalogCoreAsync(cancellationToken).ConfigureAwait(false);
            if (World.IsWallCatalogDirty)
                await SaveWallCatalogCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<bool> UnloadChunkAsync(
        ChunkCoord coord,
        bool saveChanges = true,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!World.TryGetChunk(coord, out var chunk) || chunk is null)
                return true;

            var entry = GetOrCreateEntry(coord);
            if (entry.LeaseCount != 0)
                return false;

            if (saveChanges && (chunk.IsDirty || World.IsObjectCatalogDirty || World.IsWallCatalogDirty) &&
                !await SaveCoreAsync(coord, cancellationToken).ConfigureAwait(false))
                return false;

            World.UnloadChunk(coord);
            entry.State = EChunkStreamState.Unloaded;
            entry.LastError = null;
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask SynchronizeAsync(
        IEnumerable<ChunkCoord> desiredChunks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(desiredChunks);
        ThrowIfDisposed();

        var desired = desiredChunks.ToHashSet();
        foreach (var coord in desired)
            await LoadChunkAsync(coord, cancellationToken).ConfigureAwait(false);

        ChunkCoord[] loaded;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            TrackWorldChunks();
            loaded = _entries
                .Where(pair => pair.Value.State == EChunkStreamState.Loaded)
                .Select(pair => pair.Key)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }

        foreach (var coord in loaded)
        {
            if (!desired.Contains(coord))
                await UnloadChunkAsync(coord, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return ValueTask.CompletedTask;

        _disposed = true;
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }

    private async ValueTask<Chunk> LoadCoreAsync(
        ChunkCoord coord,
        CancellationToken cancellationToken)
    {
        await EnsureCatalogLoadedCoreAsync(cancellationToken).ConfigureAwait(false);
        await EnsureWallCatalogLoadedCoreAsync(cancellationToken).ConfigureAwait(false);
        if (World.TryGetChunk(coord, out var existing) && existing is not null)
        {
            var loadedEntry = GetOrCreateEntry(coord);
            loadedEntry.State = EChunkStreamState.Loaded;
            return existing;
        }

        var entry = GetOrCreateEntry(coord);
        entry.State = EChunkStreamState.Loading;
        entry.LastError = null;
        try
        {
            var data = await Storage.LoadChunkAsync(coord, cancellationToken).ConfigureAwait(false);
            if (data is not null && data.Coord != coord)
                throw new InvalidDataException($"Storage returned chunk {data.Coord} for request {coord}.");

            Chunk chunk;
            if (data is null)
            {
                chunk = World.GetOrCreateChunk(coord);
                World.IndexObjectsInChunk(chunk);
                World.IndexWallsInChunk(chunk);
                chunk.MarkLoaded();
            }
            else
            {
                chunk = World.LoadChunk(data);
            }
            entry.State = EChunkStreamState.Loaded;
            return chunk;
        }
        catch (OperationCanceledException)
        {
            entry.State = EChunkStreamState.Unloaded;
            throw;
        }
        catch (Exception exception)
        {
            entry.State = EChunkStreamState.Failed;
            entry.LastError = exception;
            throw;
        }
    }

    private async ValueTask<bool> SaveCoreAsync(
        ChunkCoord coord,
        CancellationToken cancellationToken)
    {
        if (!World.TryGetChunk(coord, out var chunk) || chunk is null)
            return false;

        var entry = GetOrCreateEntry(coord);
        entry.State = EChunkStreamState.Saving;
        entry.LastError = null;
        try
        {
            await EnsureCatalogLoadedCoreAsync(cancellationToken).ConfigureAwait(false);
            await EnsureWallCatalogLoadedCoreAsync(cancellationToken).ConfigureAwait(false);
            await SaveChunkDataCoreAsync(coord, chunk, cancellationToken).ConfigureAwait(false);
            if (World.IsObjectCatalogDirty)
                await SaveObjectCatalogCoreAsync(cancellationToken).ConfigureAwait(false);
            if (World.IsWallCatalogDirty)
                await SaveWallCatalogCoreAsync(cancellationToken).ConfigureAwait(false);
            entry.State = EChunkStreamState.Loaded;
            return true;
        }
        catch (OperationCanceledException)
        {
            entry.State = EChunkStreamState.Loaded;
            throw;
        }
        catch (Exception exception)
        {
            entry.State = EChunkStreamState.Failed;
            entry.LastError = exception;
            throw;
        }
    }

    private async ValueTask EnsureCatalogLoadedCoreAsync(CancellationToken cancellationToken)
    {
        if (_catalogLoaded)
            return;
        if (World.Objects.Count != 0)
        {
            _catalogLoaded = true;
            return;
        }

        var objects = await Storage.LoadObjectCatalogAsync(cancellationToken).ConfigureAwait(false);
        World.LoadObjectCatalog(objects);
        _catalogLoaded = true;
    }

    private async ValueTask SaveChunkDataCoreAsync(
        ChunkCoord coord,
        Chunk chunk,
        CancellationToken cancellationToken)
    {
        var version = chunk.ChangeVersion;
        await Storage.SaveChunkAsync(chunk.CaptureData(), cancellationToken).ConfigureAwait(false);
        chunk.MarkPersisted(version);
    }

    private async ValueTask SaveObjectCatalogCoreAsync(CancellationToken cancellationToken)
    {
        var version = World.ObjectCatalogVersion;
        await Storage.SaveObjectCatalogAsync(World.CaptureObjectCatalog(), cancellationToken)
            .ConfigureAwait(false);
        World.MarkObjectCatalogPersisted(version);
    }

    private async ValueTask EnsureWallCatalogLoadedCoreAsync(CancellationToken cancellationToken)
    {
        if (_wallCatalogLoaded)
            return;
        if (World.Walls.Count != 0)
        {
            _wallCatalogLoaded = true;
            return;
        }

        var walls = await Storage.LoadWallCatalogAsync(cancellationToken).ConfigureAwait(false);
        World.LoadWallCatalog(walls);
        _wallCatalogLoaded = true;
    }

    private async ValueTask SaveWallCatalogCoreAsync(CancellationToken cancellationToken)
    {
        var version = World.WallCatalogVersion;
        await Storage.SaveWallCatalogAsync(World.CaptureWallCatalog(), cancellationToken)
            .ConfigureAwait(false);
        World.MarkWallCatalogPersisted(version);
    }

    private Entry GetOrCreateEntry(ChunkCoord coord)
    {
        if (_entries.TryGetValue(coord, out var entry))
            return entry;

        entry = new Entry(EChunkStreamState.Unloaded);
        _entries.Add(coord, entry);
        return entry;
    }

    private void TrackWorldChunks()
    {
        foreach (var region in World.Regions)
        foreach (var chunk in region.Chunks)
        {
            if (!_entries.ContainsKey(chunk.Coord))
                _entries.Add(chunk.Coord, new Entry(EChunkStreamState.Loaded));
        }
    }

    private void Release(ChunkCoord coord)
    {
        if (_disposed)
            return;

        _gate.Wait();
        try
        {
            if (_entries.TryGetValue(coord, out var entry) && entry.LeaseCount > 0)
                entry.LeaseCount--;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed class Entry(EChunkStreamState state)
    {
        public EChunkStreamState State { get; set; } = state;
        public int LeaseCount { get; set; }
        public Exception? LastError { get; set; }
    }

    public sealed class ChunkLease : IDisposable
    {
        private WorldStreamer? _owner;

        internal ChunkLease(WorldStreamer owner, Chunk chunk)
        {
            _owner = owner;
            Chunk = chunk;
        }

        public Chunk Chunk { get; }

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.Release(Chunk.Coord);
        }
    }
}
