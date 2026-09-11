using System.Numerics;

namespace Vecxy.Isometric;

public sealed class IsometricObject
{
    internal IsometricObject(
        Guid id,
        string definitionId,
        ObjectFootprint footprint,
        TileCoord anchor,
        EGridRotation rotation)
    {
        Id = id;
        DefinitionId = definitionId;
        Footprint = footprint;
        Anchor = anchor;
        Rotation = rotation;
    }

    public Guid Id { get; }
    public string DefinitionId { get; }
    public ObjectFootprint Footprint { get; }
    public TileCoord Anchor { get; internal set; }
    public EGridRotation Rotation { get; internal set; }

    public IEnumerable<TileCoord> GetOccupiedTiles() =>
        Footprint.GetOccupiedTiles(Anchor, Rotation);
}
