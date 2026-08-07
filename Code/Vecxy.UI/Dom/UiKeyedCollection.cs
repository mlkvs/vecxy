namespace Vecxy.UI;

/// <summary>
/// Retains UI component instances by stable application key. Updating a list
/// changes, moves, creates or removes only the affected entries; unchanged
/// elements keep their event subscriptions, focus and local state.
/// </summary>
public sealed class UiKeyedCollection<TKey, TItem, TView>
    where TKey : notnull
{
    private readonly UiElement _parent;
    private readonly Func<TItem, TView> _create;
    private readonly Func<TView, UiElement> _root;
    private readonly Action<TView, TItem, int> _update;
    private readonly Dictionary<TKey, Entry> _entries = [];
    private readonly HashSet<TKey> _liveKeys = [];

    public UiKeyedCollection(
        UiElement parent,
        Func<TItem, TView> create,
        Func<TView, UiElement> root,
        Action<TView, TItem, int> update)
    {
        _parent = parent ?? throw new ArgumentNullException(nameof(parent));
        _create = create ?? throw new ArgumentNullException(nameof(create));
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _update = update ?? throw new ArgumentNullException(nameof(update));
    }

    public int Count => _entries.Count;

    public void Update(IEnumerable<TItem> items, Func<TItem, TKey> keySelector)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(keySelector);
        _liveKeys.Clear();
        var index = 0;
        foreach (var item in items)
        {
            var key = keySelector(item);
            if (!_liveKeys.Add(key))
                throw new InvalidOperationException($"Duplicate UI collection key: {key}");
            if (!_entries.TryGetValue(key, out var entry))
            {
                var view = _create(item);
                var root = _root(view);
                if (root.Parent is null)
                    _parent.Insert(Math.Min(index, _parent.Children.Count), root);
                else if (!ReferenceEquals(root.Parent, _parent))
                    throw new InvalidOperationException("A keyed view was attached to a different parent.");
                else if (_parent.Children.IndexOfReference(root) != index)
                    _parent.MoveChild(root, index);
                entry = new Entry(view, root);
                _entries.Add(key, entry);
            }
            else
            {
                var current = _parent.Children.IndexOfReference(entry.Root);
                if (current != index)
                    _parent.MoveChild(entry.Root, index);
            }
            _update(entry.View, item, index);
            index++;
        }

        foreach (var key in _entries.Keys.Where(key => !_liveKeys.Contains(key)).ToArray())
        {
            _entries[key].Root.RemoveFromParent();
            _entries.Remove(key);
        }
    }

    public void Clear()
    {
        foreach (var entry in _entries.Values)
            entry.Root.RemoveFromParent();
        _entries.Clear();
        _liveKeys.Clear();
    }

    private sealed record Entry(TView View, UiElement Root);
}

internal static class UiElementListExtensions
{
    public static int IndexOfReference(this IReadOnlyList<UiElement> values, UiElement target)
    {
        for (var index = 0; index < values.Count; index++)
            if (ReferenceEquals(values[index], target))
                return index;
        return -1;
    }
}
