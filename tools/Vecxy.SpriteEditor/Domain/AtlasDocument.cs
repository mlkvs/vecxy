namespace Vecxy.SpriteEditor;

public sealed class AtlasDocument
{
    private readonly Stack<State> _undo = new();
    private readonly Stack<State> _redo = new();
    private State? _transaction;

    public SpriteAtlas Atlas { get; private set; } = new();
    public string TexturePath { get; private set; } = string.Empty;
    public bool Dirty { get; private set; }
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Open(SpriteAtlas atlas, string texturePath)
    {
        Atlas = atlas;
        TexturePath = string.IsNullOrWhiteSpace(texturePath) ? string.Empty : Path.GetFullPath(texturePath);
        Dirty = false;
        _undo.Clear();
        _redo.Clear();
        _transaction = null;
    }

    public void BeginEdit() => _transaction ??= Capture();

    public void CommitEdit()
    {
        if (_transaction is not { } before) return;
        _transaction = null;
        if (before.SameAs(Capture())) return;
        _undo.Push(before);
        _redo.Clear();
        Dirty = true;
    }

    public void Edit(Action<SpriteAtlas> action)
    {
        BeginEdit();
        action(Atlas);
        CommitEdit();
    }

    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        _redo.Push(Capture());
        Restore(_undo.Pop());
        Dirty = true;
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0) return false;
        _undo.Push(Capture());
        Restore(_redo.Pop());
        Dirty = true;
        return true;
    }

    public void MarkSaved() => Dirty = false;

    private State Capture() => new(
        Atlas.Texture,
        Atlas.FilePath,
        Atlas.Sprites.Select(pair => new SliceState(pair.Key, pair.Value.X, pair.Value.Y,
            pair.Value.Width, pair.Value.Height, pair.Value.PivotX, pair.Value.PivotY)).ToArray());

    private void Restore(State state)
    {
        Atlas.Texture = state.Texture;
        Atlas.FilePath = state.FilePath;
        Atlas.Sprites = state.Slices.ToDictionary(slice => slice.Name, slice => new SpriteSlice
        {
            X = slice.X, Y = slice.Y, Width = slice.Width, Height = slice.Height,
            PivotX = slice.PivotX, PivotY = slice.PivotY
        }, StringComparer.Ordinal);
    }

    private sealed record State(string Texture, string? FilePath, SliceState[] Slices)
    {
        public bool SameAs(State other) => Texture == other.Texture && FilePath == other.FilePath && Slices.SequenceEqual(other.Slices);
    }
    private sealed record SliceState(string Name, int X, int Y, int Width, int Height, float PivotX, float PivotY);
}
