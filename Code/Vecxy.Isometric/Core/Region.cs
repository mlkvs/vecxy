using System.Numerics;

namespace Vecxy.Isometric;

public sealed class Region
{
    private readonly Dictionary<ChunkCoord, Chunk> _chunks = [];
    private readonly WorldGridConfig _config;

    internal Region(RegionCoord coord, WorldGridConfig config)
    {
        Coord = coord;
        _config = config;
    }

    public RegionCoord Coord { get; }
    public IReadOnlyCollection<Chunk> Chunks => _chunks.Values;

    public bool TryGetChunk(ChunkCoord coord, out Chunk? chunk)
    {
        if (_config.GetRegion(coord) != Coord)
        {
            chunk = null;
            return false;
        }

        return _chunks.TryGetValue(coord, out chunk);
    }

    internal Chunk GetOrCreateChunk(ChunkCoord coord)
    {
        if (_config.GetRegion(coord) != Coord)
            throw new ArgumentOutOfRangeException(nameof(coord), "Chunk belongs to another region.");

        if (_chunks.TryGetValue(coord, out var chunk))
            return chunk;

        chunk = new Chunk(coord, _config);
        _chunks.Add(coord, chunk);
        return chunk;
    }

    internal bool RemoveChunk(ChunkCoord coord) => _chunks.Remove(coord);
}
