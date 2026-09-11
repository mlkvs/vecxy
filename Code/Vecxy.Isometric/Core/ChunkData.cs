namespace Vecxy.Isometric;

public sealed record TileData(TileCoord Coord, ETileSurface? Surface);

public sealed record ChunkData(ChunkCoord Coord, TileData[] Tiles);

public sealed record WorldObjectData(
    Guid Id,
    string DefinitionId,
    TileOffset[] Footprint,
    TileCoord Anchor,
    EGridRotation Rotation);

public sealed record WallData(
    Guid Id,
    string DefinitionId,
    WallSegmentOffset[] Footprint,
    WallAnchorCoord Anchor,
    EGridRotation Rotation);
