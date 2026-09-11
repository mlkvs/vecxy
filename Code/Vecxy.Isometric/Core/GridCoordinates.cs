namespace Vecxy.Isometric;

public readonly record struct TileCoord(int X, int Y, int Level = 0);

public readonly record struct ChunkCoord(int X, int Y);

public readonly record struct RegionCoord(int X, int Y);

public readonly record struct LocalTileCoord(int X, int Y);

public readonly record struct LocalChunkCoord(int X, int Y);

public readonly record struct TileAddress(
    RegionCoord Region,
    ChunkCoord Chunk,
    LocalChunkCoord LocalChunk,
    LocalTileCoord LocalTile,
    int Level);
