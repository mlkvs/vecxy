namespace Vecxy.Isometric;

public sealed class WorldGridConfig
{
    public WorldGridConfig(
        int chunkWidth = 16,
        int chunkHeight = 16,
        int regionWidth = 16,
        int regionHeight = 16)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chunkWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chunkHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(regionWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(regionHeight);

        ChunkWidth = chunkWidth;
        ChunkHeight = chunkHeight;
        RegionWidth = regionWidth;
        RegionHeight = regionHeight;
    }

    public int ChunkWidth { get; }
    public int ChunkHeight { get; }
    public int RegionWidth { get; }
    public int RegionHeight { get; }

    public TileAddress GetAddress(TileCoord tile)
    {
        var chunk = GetChunk(tile);
        return new TileAddress(
            GetRegion(chunk),
            chunk,
            GetLocalChunk(chunk),
            GetLocalTile(tile),
            tile.Level);
    }

    public ChunkCoord GetChunk(TileCoord tile) => new(
        GridMath.FloorDivide(tile.X, ChunkWidth),
        GridMath.FloorDivide(tile.Y, ChunkHeight));

    public RegionCoord GetRegion(ChunkCoord chunk) => new(
        GridMath.FloorDivide(chunk.X, RegionWidth),
        GridMath.FloorDivide(chunk.Y, RegionHeight));

    public LocalTileCoord GetLocalTile(TileCoord tile) => new(
        GridMath.PositiveModulo(tile.X, ChunkWidth),
        GridMath.PositiveModulo(tile.Y, ChunkHeight));

    public LocalChunkCoord GetLocalChunk(ChunkCoord chunk) => new(
        GridMath.PositiveModulo(chunk.X, RegionWidth),
        GridMath.PositiveModulo(chunk.Y, RegionHeight));

    public TileCoord GetTile(ChunkCoord chunk, LocalTileCoord localTile, int level = 0)
    {
        EnsureLocalTile(localTile);
        return new TileCoord(
            checked(chunk.X * ChunkWidth + localTile.X),
            checked(chunk.Y * ChunkHeight + localTile.Y),
            level);
    }

    public ChunkCoord GetChunk(RegionCoord region, LocalChunkCoord localChunk)
    {
        EnsureLocalChunk(localChunk);
        return new ChunkCoord(
            checked(region.X * RegionWidth + localChunk.X),
            checked(region.Y * RegionHeight + localChunk.Y));
    }

    public bool Contains(LocalTileCoord coord) =>
        (uint)coord.X < (uint)ChunkWidth &&
        (uint)coord.Y < (uint)ChunkHeight;

    public bool Contains(LocalChunkCoord coord) =>
        (uint)coord.X < (uint)RegionWidth &&
        (uint)coord.Y < (uint)RegionHeight;

    private void EnsureLocalTile(LocalTileCoord coord)
    {
        if (!Contains(coord))
            throw new ArgumentOutOfRangeException(nameof(coord));
    }

    private void EnsureLocalChunk(LocalChunkCoord coord)
    {
        if (!Contains(coord))
            throw new ArgumentOutOfRangeException(nameof(coord));
    }
}

internal static class GridMath
{
    public static int FloorDivide(int value, int positiveDivisor)
    {
        var quotient = value / positiveDivisor;
        var remainder = value % positiveDivisor;
        return remainder < 0 ? quotient - 1 : quotient;
    }

    public static int PositiveModulo(int value, int positiveDivisor)
    {
        var remainder = value % positiveDivisor;
        return remainder < 0 ? remainder + positiveDivisor : remainder;
    }
}
