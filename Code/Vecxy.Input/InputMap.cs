using System.Numerics;
using Vecxy.Assets;
using Vecxy.Diagnostics;

namespace Vecxy.Input;

public sealed class InputMap : IDisposable
{
    private readonly List<InputAction> _actions = [];
    private readonly Dictionary<string, InputAction> _actionsByName =
        new(StringComparer.Ordinal);
    private readonly AssetRef<InputAsset> _asset;
    private readonly InputModule _input;
    private readonly string _mapName;
    private int _assetVersion;
    private bool _disposed;

    internal InputMap(
        InputModule input,
        AssetRef<InputAsset> asset,
        string mapName)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentException.ThrowIfNullOrWhiteSpace(mapName);

        _input = input;
        _asset = asset.Acquire();
        _mapName = mapName;
        _assetVersion = _asset.Version;

        var map = GetMapAsset();
        Name = map.Name;
        Rebuild(map);

        _input.Register(this);
    }

    public string Name { get; private set; }

    public bool IsEnabled { get; private set; }

    public IReadOnlyList<InputAction> Actions => _actions;

    public InputAction this[string actionName] => GetAction(actionName);

    public void Enable()
    {
        ThrowIfDisposed();
        EnsureFresh();

        if (IsEnabled)
            return;

        IsEnabled = true;

        var snapshot = _input.Snapshot;
        foreach (var action in _actions)
            action.Sync(snapshot, this);
    }

    public void Disable()
    {
        if (!IsEnabled)
            return;

        IsEnabled = false;

        foreach (var action in _actions)
            action.Reset();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Disable();
        _input.Unregister(this);
        _asset.Dispose();
        _disposed = true;
    }

    public InputAction GetAction(string actionName)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(actionName);

        if (_actionsByName.TryGetValue(actionName, out var action))
            return action;

        throw new KeyNotFoundException(
            $"Input map '{Name}' does not contain action '{actionName}'.");
    }

    public InputAction<TValue> GetAction<TValue>(string actionName)
    {
        var action = GetAction(actionName);

        if (action is InputAction<TValue> typed)
            return typed;

        throw new InvalidCastException(
            $"Input action '{Name}.{actionName}' is not '{typeof(TValue).Name}'.");
    }

    internal void Update(InputSnapshot snapshot)
    {
        if (!IsEnabled || _disposed)
            return;

        EnsureFresh();

        foreach (var action in _actions)
            action.Update(snapshot, this);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void EnsureFresh()
    {
        if (_assetVersion == _asset.Version)
            return;

        _assetVersion = _asset.Version;

        if (_asset.HasError)
            return;

        try
        {
            var map = GetMapAsset();
            Rebuild(map);
            Name = map.Name;
        }
        catch (Exception exception)
        {
            Logger.Error(
                exception,
                $"Input map hot reload failed, keeping previous bindings: {_asset.Metadata.Path}::{_mapName}");
            return;
        }

        if (IsEnabled)
        {
            var snapshot = _input.Snapshot;
            foreach (var action in _actions)
                action.Sync(snapshot, this);
        }
    }

    private InputMapAsset GetMapAsset()
    {
        var map = _asset.Value.Maps.FirstOrDefault(
            current => string.Equals(
                current.Name,
                _mapName,
                StringComparison.Ordinal));

        if (map is null)
        {
            throw new KeyNotFoundException(
                $"Input asset does not contain map '{_mapName}'.");
        }

        return map;
    }

    private void Rebuild(InputMapAsset map)
    {
        var rebuilt = new List<InputAction>(map.Actions.Count);
        var rebuiltByName = new Dictionary<string, InputAction>(StringComparer.Ordinal);

        foreach (var actionAsset in map.Actions)
        {
            if (_actionsByName.TryGetValue(actionAsset.Name, out var existing) &&
                CanReuse(existing, actionAsset))
            {
                existing.Rebind(CreateBindings(actionAsset));
                rebuilt.Add(existing);
                rebuiltByName.Add(existing.Name, existing);
                continue;
            }

            var created = CreateAction(actionAsset);
            rebuilt.Add(created);
            rebuiltByName.Add(created.Name, created);
        }

        _actions.Clear();
        _actions.AddRange(rebuilt);
        _actionsByName.Clear();

        foreach (var (actionName, action) in rebuiltByName)
            _actionsByName.Add(actionName, action);
    }

    private static bool CanReuse(
        InputAction action,
        InputActionAsset asset)
    {
        return asset.Type switch
        {
            EInputActionType.Button => action.GetType() == typeof(InputAction),
            EInputActionType.Vector2 => action.GetType() == typeof(InputAction<Vector2>),
            _ => false,
        };
    }

    private static InputBinding[] CreateBindings(InputActionAsset action)
    {
        return action.Bindings
            .Select(CreateBinding)
            .ToArray();
    }

    private static InputAction CreateAction(InputActionAsset action)
    {
        var bindings = CreateBindings(action);

        return action.Type switch
        {
            EInputActionType.Button =>
                new InputAction(
                    action.Name,
                    bindings),

            EInputActionType.Vector2 =>
                new InputAction<Vector2>(
                    action.Name,
                    bindings),

            _ => throw new NotSupportedException(
                $"Input action type '{action.Type}' is not supported."),
        };
    }

    private static InputBinding CreateBinding(InputBindingAsset binding)
    {
        return binding.Type switch
        {
            EInputBindingType.Keyboard =>
                InputBinding.Keyboard(binding.Key),

            EInputBindingType.Mouse =>
                InputBinding.Mouse(binding.Mouse),

            EInputBindingType.Composite =>
                CreateComposite(binding.Composite),

            _ => throw new NotSupportedException(
                $"Input binding type '{binding.Type}' is not supported."),
        };
    }

    private static InputBinding CreateComposite(string composite)
    {
        return composite switch
        {
            "WASD" => InputBinding.Composite(InputComposite.Wasd),
            _ => throw new NotSupportedException(
                $"Input composite '{composite}' is not supported."),
        };
    }
}
