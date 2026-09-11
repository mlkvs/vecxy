using System.Text.Json.Serialization;
using Vecxy.Prototypes;

namespace Vecxy.Isometric.Editor;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EEditorAssetKind : byte
{
    Surface,
    Wall,
    Object,
    Opening,
    Connection
}

public sealed class IsometricAssetDefinition
{
    public int SchemaVersion { get; set; } = 1;
    public Guid AssetId { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New asset";
    public string Category { get; set; } = "Other";
    public EEditorAssetKind Kind { get; set; }
    public string Texture { get; set; } = string.Empty;
    public string Color { get; set; } = "#6b7280";
    public int Width { get; set; } = 1;
    public int Height { get; set; } = 1;
    public int Length { get; set; } = 1;
    public bool Blocking { get; set; } = true;
    public bool HiddenContents { get; set; }
    public List<EditorSlotDefinition> Slots { get; set; } = [];
    [JsonIgnore] public string FilePath { get; set; } = string.Empty;
}

public sealed class EditorSlotDefinition
{
    public string Id { get; set; } = "surface";
    public string Type { get; set; } = "SupportSurface";
    public int Capacity { get; set; } = 1;
}

public sealed class IsometricWorldAsset
{
    public int SchemaVersion { get; set; } = 1;
    public Guid WorldId { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New World";
    public int ChunkWidth { get; set; } = 16;
    public int ChunkHeight { get; set; } = 16;
    public int RegionWidth { get; set; } = 16;
    public int RegionHeight { get; set; } = 16;
    public List<EditorPlacement> Placements { get; set; } = [];
    [JsonIgnore] public string FilePath { get; set; } = string.Empty;
}

public sealed class EditorPlacement
{
    public sealed class Prototype : APrototype<EditorPlacement, Prototype.Options>
    {
        public sealed class Options
        {
            public Guid DefinitionId { get; set; }
            public EEditorAssetKind Kind { get; set; }
            public int X { get; set; }
            public int Y { get; set; }
            public int Level { get; set; }
            public int Rotation { get; set; }
            public Guid? ParentId { get; set; }
            public string? SlotId { get; set; }
        }

        protected override EditorPlacement Instantiate(IPrototypeContext context) => new();

        protected override void Configure(EditorPlacement target, Options options)
        {
            target.DefinitionId = options.DefinitionId;
            target.Kind = options.Kind;
            target.X = options.X;
            target.Y = options.Y;
            target.Level = options.Level;
            target.Rotation = options.Rotation;
            target.ParentId = options.ParentId;
            target.SlotId = options.SlotId;
        }
    }

    public Guid InstanceId { get; set; } = Guid.NewGuid();
    public Guid DefinitionId { get; set; }
    public EEditorAssetKind Kind { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Level { get; set; }
    public int Rotation { get; set; }
    public Guid? ParentId { get; set; }
    public string? SlotId { get; set; }
    public Dictionary<string, string> Properties { get; set; } = [];
}

public sealed class EditorPrototypeContext : APrototypeContext;

public sealed class EditorPlacementPrototypeSystem
    : APrototypeSystem<EditorPlacement, EditorPrototypeContext>
{
}
