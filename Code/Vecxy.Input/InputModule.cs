using System.Collections.Concurrent;
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

        public override void RegisterGlobal(ContainerBuilder builder)
        {
            builder
                .RegisterType<InputCaptureState>()
                .As<IInputCaptureState>()
                .SingleInstance();
        }

        protected override void RegisterModule(ContainerBuilder builder)
        {
            builder
                .RegisterType<InputModule>()
                .AsSelf()
                .SingleInstance();
        }
    }

    private readonly IWindow _window;
    private readonly IInputCaptureState _captureState;
    private readonly List<InputMap> _maps = [];
    private readonly InputSnapshot _snapshot = new();
    private readonly InputSnapshot _actionSnapshot = new();
    private readonly ConcurrentQueue<IWindow.TouchEvent> _pendingTouches = new();
    private readonly Dictionary<int, TouchPoint> _activeTouches = [];
    private Vector2 _pendingMouseDelta;
    private Vector2 _pendingMouseWheelDelta;
    private Vector2 _lastMousePosition;
    private bool _hasMousePosition;
    private bool _initialized;
    private bool _disposed;
    private int? _primaryTouchId;
    private EPointerKind _lastPointerKind;

    internal InputSnapshot ActionSnapshot => _actionSnapshot;

    public Vector2 MousePosition => _snapshot.MousePosition;

    public Vector2 MouseDelta => _snapshot.MouseDelta;

    public Vector2 MouseWheelDelta => _snapshot.MouseWheelDelta;
    public IReadOnlyList<TouchPoint> Touches => _snapshot.Touches;
    public Vector2 PointerPosition => _snapshot.PointerPosition;
    public Vector2 PointerDelta => _snapshot.PointerDelta;
    public EPointerKind PointerKind => _snapshot.PointerKind;
    public bool IsPrimaryPointerPressed => _snapshot.IsPrimaryPointerPressed;

    public bool IsKeyPressed(EKeyboardKey key) =>
        _snapshot.IsKeyPressed(key);

    public bool IsMouseButtonPressed(EMouseButton button) =>
        _snapshot.IsMouseButtonPressed(button);

    public InputModule(
        IWindow window,
        IInputCaptureState captureState)
    {
        _window = window;
        _captureState = captureState;
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
        _window.TouchChanged += OnTouchChanged;

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

        UpdateTouches();
        UpdatePrimaryPointer();

        _actionSnapshot.CopyFrom(
            _snapshot,
            _captureState.SuppressKeyboard,
            _captureState.SuppressMouse);

        foreach (var map in _maps.ToArray())
            map.Update(_actionSnapshot);
    }

    public void OnShutdown()
    {
        if (!_initialized)
            return;

        _window.KeyChanged -= OnKeyChanged;
        _window.MouseButtonChanged -= OnMouseButtonChanged;
        _window.MouseMoved -= OnMouseMoved;
        _window.MouseWheelChanged -= OnMouseWheelChanged;
        _window.TouchChanged -= OnTouchChanged;

        _initialized = false;
        _activeTouches.Clear();
        while (_pendingTouches.TryDequeue(out _)) { }
        _primaryTouchId = null;
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
        _lastPointerKind = EPointerKind.Mouse;
    }

    private void OnMouseWheelChanged(IWindow.MouseWheelEvent eventData)
    {
        _pendingMouseWheelDelta +=
            new Vector2(
                eventData.X,
                eventData.Y);
    }

    private void OnTouchChanged(IWindow.TouchEvent eventData) =>
        _pendingTouches.Enqueue(eventData);

    private void UpdateTouches()
    {
        var frameTouches = _activeTouches.ToDictionary(
            pair => pair.Key,
            pair => pair.Value with
            {
                Delta = Vector2.Zero,
                Phase = ETouchPhase.Stationary
            });

        while (_pendingTouches.TryDequeue(out var eventData))
        {
            var position = new Vector2(eventData.X, eventData.Y);
            var delta = _activeTouches.TryGetValue(eventData.Id, out var previous)
                ? position - previous.Position
                : Vector2.Zero;
            var accumulated = frameTouches.GetValueOrDefault(eventData.Id).Delta + delta;
            var point = new TouchPoint(
                eventData.Id,
                position,
                accumulated,
                eventData.Phase,
                Math.Clamp(eventData.Pressure, 0.0f, 1.0f),
                eventData.IsPrimary);
            frameTouches[eventData.Id] = point;

            if (point.IsActive)
                _activeTouches[eventData.Id] = point;
            else
                _activeTouches.Remove(eventData.Id);

            if (eventData.Phase == ETouchPhase.Began &&
                (_primaryTouchId is null || eventData.IsPrimary))
                _primaryTouchId = eventData.Id;
        }

        if (_primaryTouchId is { } primary &&
            !_activeTouches.ContainsKey(primary) &&
            !frameTouches.TryGetValue(primary, out var endedPrimary))
            _primaryTouchId = null;
        else if (_primaryTouchId is { } endedId &&
                 frameTouches.TryGetValue(endedId, out endedPrimary) &&
                 !endedPrimary.IsActive)
        {
            // Keep the ended primary for this frame so release is delivered at
            // the final touch position. A replacement is selected next frame.
        }

        if (_primaryTouchId is null && _activeTouches.Count > 0)
            _primaryTouchId = _activeTouches.Keys.Min();

        _snapshot.SetTouches(frameTouches.Values
            .OrderByDescending(point => point.Id == _primaryTouchId)
            .ThenBy(point => point.Id)
            .Select(point => point with { IsPrimary = point.Id == _primaryTouchId }));
    }

    private void UpdatePrimaryPointer()
    {
        var primary = _snapshot.Touches
            .Where(point => point.IsPrimary)
            .Select(point => (TouchPoint?)point)
            .FirstOrDefault();
        if (primary is { } primaryTouch)
        {
            _snapshot.PointerPosition = primaryTouch.Position;
            _snapshot.PointerDelta = primaryTouch.Delta;
            _snapshot.PointerKind = EPointerKind.Touch;
            _snapshot.IsPrimaryPointerPressed = primaryTouch.IsActive;
            _lastPointerKind = EPointerKind.Touch;
            if (!primaryTouch.IsActive)
                _primaryTouchId = null;
            return;
        }

        _snapshot.PointerPosition = _lastPointerKind == EPointerKind.Touch
            ? _snapshot.PointerPosition
            : _snapshot.MousePosition;
        _snapshot.PointerDelta = _lastPointerKind == EPointerKind.Touch
            ? Vector2.Zero
            : _snapshot.MouseDelta;
        _snapshot.PointerKind = _lastPointerKind;
        _snapshot.IsPrimaryPointerPressed =
            _lastPointerKind == EPointerKind.Mouse &&
            _snapshot.IsMouseButtonPressed(EMouseButton.Left);
    }
}
