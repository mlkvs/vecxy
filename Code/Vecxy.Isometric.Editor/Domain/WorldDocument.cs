using System.Text.Json;

namespace Vecxy.Isometric.Editor;

public sealed class WorldDocument
{
    private readonly Stack<string> _undo = [];
    private readonly Stack<string> _redo = [];
    private readonly JsonSerializerOptions _json;

    public WorldDocument(IsometricWorldAsset world, JsonSerializerOptions json)
    {
        World = world;
        _json = json;
    }

    public IsometricWorldAsset World { get; private set; }
    public bool IsDirty { get; private set; }
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Execute(Action<IsometricWorldAsset> mutation)
    {
        var before = JsonSerializer.Serialize(World, _json);
        mutation(World);
        _undo.Push(before);
        _redo.Clear();
        IsDirty = true;
    }

    public bool Undo()
    {
        if (!_undo.TryPop(out var state)) return false;
        _redo.Push(JsonSerializer.Serialize(World, _json));
        World = Deserialize(state);
        IsDirty = true;
        return true;
    }

    public bool Redo()
    {
        if (!_redo.TryPop(out var state)) return false;
        _undo.Push(JsonSerializer.Serialize(World, _json));
        World = Deserialize(state);
        IsDirty = true;
        return true;
    }

    public void MarkSaved() => IsDirty = false;

    private IsometricWorldAsset Deserialize(string value)
    {
        var filePath = World.FilePath;
        var restored = JsonSerializer.Deserialize<IsometricWorldAsset>(value, _json) ??
                       throw new InvalidDataException("Could not restore world document history.");
        restored.FilePath = filePath;
        return restored;
    }
}
