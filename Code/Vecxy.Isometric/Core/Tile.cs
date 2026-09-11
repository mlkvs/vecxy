using System.Numerics;

namespace Vecxy.Isometric;

public enum ETileSurface : byte
{
    Ground,
    Floor
}

public sealed class TileSurface
{
    public TileSurface(ETileSurface type)
    {
        Type = type;
    }

    public ETileSurface Type { get; }
}

public sealed class Tile
{
    private readonly Action _onChanged;
    private readonly HashSet<Guid> _occupants = [];

    internal Tile(TileCoord coord, Action onChanged)
    {
        Coord = coord;
        _onChanged = onChanged;
    }

    public TileCoord Coord { get; }
    public TileSurface? Surface { get; private set; }
    public IReadOnlySet<Guid> Occupants => _occupants;

    public void SetSurface(TileSurface? surface)
    {
        if (Surface?.Type == surface?.Type)
            return;

        Surface = surface;
        _onChanged();
    }

    internal void AddOccupant(Guid objectId)
    {
        _occupants.Add(objectId);
    }

    internal void RemoveOccupant(Guid objectId)
    {
        _occupants.Remove(objectId);
    }
}
