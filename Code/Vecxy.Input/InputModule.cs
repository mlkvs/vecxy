using System.Numerics;
using Autofac;
using Vecxy.Assets;
using Vecxy.Kernel;

namespace Vecxy.Input;

public sealed class InputModule :
    IModule,
    IModule.IUpdatable,
    IInputManager
{
    public sealed class Definition : AModuleDefinition<InputModule>
    {
        protected override IReadOnlyList<Type> Exports => [typeof(IInputManager)];

        protected override void RegisterModule(ContainerBuilder builder)
        {
            builder
                .RegisterType<InputModule>()
                .AsSelf()
                .SingleInstance();
        }
    }

    private readonly IWindow _window;
    private readonly List<InputMap> _maps = [];
    private readonly InputSnapshot _snapshot = new();
    private Vector2 _pendingMouseDelta;
    private Vector2 _pendingMouseWheelDelta;
    private Vector2 _lastMousePosition;
    private bool _hasMousePosition;
    private bool _initialized;
    private bool _disposed;

    internal InputSnapshot Snapshot => _snapshot;

    public Vector2 MousePosition => _snapshot.MousePosition;

    public Vector2 MouseDelta => _snapshot.MouseDelta;

    public InputModule(IWindow window)
    {
        _window = window;
    }

    public InputMap Create(AssetRef<InputAsset> asset, string mapName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentException.ThrowIfNullOrWhiteSpace(mapName);

        return new InputMap(this, asset, mapName);
    }

    public void OnInitialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_initialized)
            return;

        _window.KeyChanged += OnKeyChanged;
        _window.MouseButtonChanged += OnMouseButtonChanged;
        _window.MouseMoved += OnMouseMoved;
        _window.MouseWheelChanged += OnMouseWheelChanged;

        _initialized = true;
    }

    public void OnUpdate(float deltaTime)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_initialized)
            return;

        _snapshot.MouseDelta = _pendingMouseDelta;
        _snapshot.MouseWheelDelta = _pendingMouseWheelDelta;
        _pendingMouseDelta = Vector2.Zero;
        _pendingMouseWheelDelta = Vector2.Zero;

        foreach (var map in _maps.ToArray())
            map.Update(_snapshot);
    }

    public void OnShutdown()
    {
        if (!_initialized)
            return;

        _window.KeyChanged -= OnKeyChanged;
        _window.MouseButtonChanged -= OnMouseButtonChanged;
        _window.MouseMoved -= OnMouseMoved;
        _window.MouseWheelChanged -= OnMouseWheelChanged;

        _initialized = false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        OnShutdown();
        _maps.Clear();
        _disposed = true;
    }

    internal void Register(InputMap map)
    {
        _maps.Add(map);
    }

    internal void Unregister(InputMap map)
    {
        _maps.Remove(map);
    }

    private void OnKeyChanged(IWindow.KeyEvent eventData)
    {
        _snapshot.SetKey(
            InputTypeMaps.MapKey(eventData.Key),
            eventData.IsPressed);
    }

    private void OnMouseButtonChanged(IWindow.MouseButtonEvent eventData)
    {
        _snapshot.SetMouseButton(
            InputTypeMaps.MapMouseButton(eventData.Button),
            eventData.IsPressed);
    }

    private void OnMouseMoved(IWindow.MouseMoveEvent eventData)
    {
        var position =
            new Vector2(
                eventData.X,
                eventData.Y);

        if (_hasMousePosition)
            _pendingMouseDelta += position - _lastMousePosition;
        else
            _hasMousePosition = true;

        _lastMousePosition = position;
        _snapshot.MousePosition = position;
    }

    private void OnMouseWheelChanged(IWindow.MouseWheelEvent eventData)
    {
        _pendingMouseWheelDelta +=
            new Vector2(
                eventData.X,
                eventData.Y);
    }
}
