using System.Diagnostics;
using System.Numerics;
using Autofac;
using Facebook.Yoga;
using Vecxy.Assets;
using Vecxy.Diagnostics;
using Vecxy.Input;
using Vecxy.Kernel;
using Vecxy.Rendering;

namespace Vecxy.UI;

public interface IUiManager
{
    IReadOnlyList<UiDocument> Documents { get; }
    UiDocument Load(string path);
    bool Unload(UiDocument document);
    void Focus(UiElement? element, bool focusVisible = false);
}

public sealed class UiModule :
    IModule,
    IModule.IUpdatable,
    IUiManager,
    IUiDiagnostics
{
    public sealed class Definition : AModuleDefinition<UiModule>
    {
        protected override IReadOnlyList<Type> Exports =>
            [typeof(IUiManager), typeof(IUiDiagnostics)];

        protected override void RegisterModule(ContainerBuilder builder)
        {
            builder.RegisterType<UiModule>().AsSelf().SingleInstance();
        }
    }

    private readonly IAssetsManager _assets;
    private readonly IConfigProvider _configs;
    private readonly IInputManager _input;
    private readonly IInputCaptureState _inputCapture;
    private readonly IRenderer _renderer;
    private readonly IRenderOverlayStage _overlays;
    private readonly ITextureResolver _textures;
    private readonly UiRenderer _uiRenderer;
    private readonly UiPerformanceStatistics _statistics = new();
    private readonly Config _yogaConfig = new();
    private readonly List<UiDocument> _documents = [];
    private ConfigRef<UiConfig>? _settings;
    private UiElement? _pressedElement;
    private UiElement? _focusedElement;
    private UiElement? _draggingElement;
    private UiElement? _dropTarget;
    private readonly List<UiElement> _hoveredElements = [];
    private readonly List<UiElement> _nextHoveredElements = [];
    private UiElement? _scrollCandidate;
    private UiElement? _scrollingElement;
    private UiElement? _inertiaElement;
    private UiDocument? _pressedDocument;
    private readonly Dictionary<int, (UiElement Element, UiDocument Document)> _touchCaptures = [];
    private Vector2 _pressPosition;
    private Vector2 _lastPointerPosition;
    private Vector2 _scrollVelocity;
    private Vector2 _cachedHitPoint = new(float.NaN, float.NaN);
    private UiElement? _cachedHitElement;
    private UiDocument? _cachedHitDocument;
    private int _cachedHitSignature = int.MinValue;
    private bool _wasPointerPressed;
    private bool _wasTabPressed;
    private bool _initialized;
    private bool _disposed;

    public IReadOnlyList<UiDocument> Documents => _documents;
    public UiPerformanceStatistics Statistics => _statistics;

    public UiModule(
        IAssetsManager assets,
        IConfigProvider configs,
        IInputManager input,
        IInputCaptureState inputCapture,
        IRenderer renderer,
        IRenderOverlayStage overlays,
        ITextureResolver textures,
        IGraphicsDeviceProvider graphics)
    {
        _assets = assets;
        _configs = configs;
        _input = input;
        _inputCapture = inputCapture;
        _renderer = renderer;
        _overlays = overlays;
        _textures = textures;
        _uiRenderer = new UiRenderer(graphics.GraphicsDevice, _statistics);
        _yogaConfig.SetUseWebDefaults(false);
        _yogaConfig.SetPointScaleFactor(1.0f);
    }

    public UiDocument Load(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var source = _assets.Load<UiDocumentAsset>(path);
        try
        {
            var document = new UiDocument(
                _assets,
                _textures,
                _yogaConfig,
                source);
            _documents.Add(document);
            return document;
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }

    public bool Unload(UiDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!_documents.Remove(document))
            return false;
        document.Dispose();
        return true;
    }

    public void Focus(UiElement? element, bool focusVisible = false)
    {
        if (element is { IsFocusable: false })
            element = null;
        if (ReferenceEquals(_focusedElement, element))
        {
            if (element is not null)
                element.IsFocusVisible = focusVisible;
            return;
        }

        var previous = _focusedElement;
        _focusedElement = element;
        if (previous is not null)
        {
            previous.IsFocused = false;
            previous.IsFocusVisible = false;
            previous.RaiseBlurred();
        }
        if (element is not null)
        {
            element.IsFocused = true;
            element.IsFocusVisible = focusVisible;
            element.RaiseFocused();
        }
    }

    public void OnInitialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized)
            return;
        _assets.RegisterImporter<UiDocumentAsset>(new UiDocumentAssetImporter());
        _assets.RegisterImporter<UiStyleSheetAsset>(new UiStyleSheetAssetImporter());
        _assets.RegisterImporter<UiFontAsset>(new UiFontAssetImporter());
        _assets.RegisterImporter<UiSpriteAtlasAsset>(new UiSpriteAtlasAssetImporter());
        _settings = _configs.LoadConfig<UiConfig>("Configs/UI.yaml");
        _overlays.RegisterGameOverlay(RenderOverlay);
        _initialized = true;
    }

    public void OnUpdate(float deltaTime)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_initialized)
            return;

        _statistics.BeginFrame(deltaTime);
        var updateStarted = Stopwatch.GetTimestamp();
        var allocatedBeforeUpdate = GC.GetAllocatedBytesForCurrentThread();
        double layoutMilliseconds = 0;
        double animationMilliseconds = 0;
        foreach (var document in _documents)
        {
            try
            {
                var started = Stopwatch.GetTimestamp();
                document.Layout(_renderer.GameOutputWidth, _renderer.GameOutputHeight, Settings);
                layoutMilliseconds += Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                started = Stopwatch.GetTimestamp();
                document.UpdateAnimations(
                    deltaTime,
                    _renderer.GameOutputWidth,
                    _renderer.GameOutputHeight,
                    Settings);
                animationMilliseconds += Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            }
            catch (Exception exception)
            {
                Logger.Error(exception, $"Could not update UI document: {document.Path}");
            }
        }

        var inputStarted = Stopwatch.GetTimestamp();
        UpdateScrollInertia(deltaTime);

        var pointer = _renderer.ScreenToGameOutput(_input.PointerPosition);
        var hitTestStarted = Stopwatch.GetTimestamp();
        var hit = HitTest(pointer, out var hitDocument);
        DispatchTouchEvents();
        var hitTestMilliseconds = Stopwatch.GetElapsedTime(hitTestStarted).TotalMilliseconds;

        _nextHoveredElements.Clear();
        if (_input.PointerKind == EPointerKind.Mouse || _input.IsPrimaryPointerPressed)
            for (var hovered = hit; hovered is not null; hovered = hovered.Parent)
                _nextHoveredElements.Add(hovered);
        foreach (var hovered in _hoveredElements)
            if (!_nextHoveredElements.Contains(hovered))
                hovered.IsHovered = false;
        foreach (var hovered in _nextHoveredElements)
            if (!_hoveredElements.Contains(hovered))
                hovered.IsHovered = true;
        _hoveredElements.Clear();
        _hoveredElements.AddRange(_nextHoveredElements);

        HandleKeyboardFocus();
        HandleScrolling(hit);

        var pointerPressed = _input.IsPrimaryPointerPressed;
        var pointerCancelled = _input.Touches.Any(touch =>
            touch.IsPrimary && touch.Phase == ETouchPhase.Cancelled);
        if (pointerPressed && !_wasPointerPressed)
        {
            _pressedElement = hit;
            _pressedDocument = hitDocument;
            _scrollCandidate = FindScrollable(hit, preferHorizontal: false);
            _scrollingElement = null;
            _inertiaElement = null;
            _scrollVelocity = Vector2.Zero;
            _pressPosition = pointer;
            _lastPointerPosition = pointer;
            Focus(hit, false);
            if (_pressedElement is not null)
                _pressedElement.IsActive = true;
        }
        else if (pointerPressed && _wasPointerPressed && _pressedElement is { } pressed)
        {
            var pointerDelta = pointer - _lastPointerPosition;
            var threshold = Settings.DragScrollThreshold * (_pressedDocument?.LayoutScale ?? 1.0f);
            var exceededThreshold = Vector2.DistanceSquared(pointer, _pressPosition) >= threshold * threshold;

            if (_scrollingElement is null && _draggingElement is null && exceededThreshold &&
                _scrollCandidate is { } scrollCandidate &&
                MovementMatchesScrollAxis(scrollCandidate, pointer - _pressPosition))
            {
                _scrollingElement = scrollCandidate;
                pressed.IsActive = false;
            }
            else if (_scrollingElement is null && _draggingElement is null &&
                     pressed.IsDraggable && exceededThreshold)
            {
                _draggingElement = pressed;
                pressed.IsDragging = true;
                pressed.RaiseDragStarted();
            }

            if (_scrollingElement is not null)
                DragScroll(pointerDelta, deltaTime);
            else if (_draggingElement is not null)
                SetDropTarget(FindDropTarget(hit, _draggingElement));

            _lastPointerPosition = pointer;
        }
        else if (!pointerPressed && _wasPointerPressed)
        {
            var releasedElement = _pressedElement;
            _pressedElement = null;
            if (releasedElement is not null)
            {
                releasedElement.IsActive = false;
                if (_scrollingElement is not null)
                {
                    _inertiaElement = _scrollingElement;
                    _scrollingElement = null;
                }
                else if (_draggingElement is not null)
                {
                    _dropTarget?.RaiseDropped(_draggingElement);
                    _draggingElement.IsDragging = false;
                    _draggingElement.RaiseDragEnded();
                    _draggingElement = null;
                    SetDropTarget(null);
                }
                else if (!pointerCancelled && ReferenceEquals(releasedElement, hit))
                    releasedElement.RaiseClicked();
            }
            _scrollCandidate = null;
            _pressedDocument = null;
        }

        _wasPointerPressed = pointerPressed;
        _inputCapture.SuppressMouse = hit is not null || _pressedElement is not null || _scrollingElement is not null;
        _inputCapture.SuppressKeyboard =
            _focusedElement?.TagName is "input" or "textarea" or "select";
        var inputMilliseconds = Stopwatch.GetElapsedTime(inputStarted).TotalMilliseconds;
        _statistics.RecordUpdate(
            Stopwatch.GetElapsedTime(updateStarted).TotalMilliseconds,
            layoutMilliseconds,
            animationMilliseconds,
            hitTestMilliseconds,
            inputMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBeforeUpdate);
    }

    private UiElement? HitTest(Vector2 outputPoint, out UiDocument? hitDocument)
    {
        var signature = new HashCode();
        signature.Add(_renderer.GameOutputWidth);
        signature.Add(_renderer.GameOutputHeight);
        signature.Add(_documents.Count);
        foreach (var document in _documents)
        {
            signature.Add(document.IsVisible);
            signature.Add(document.RenderVersion);
        }
        var currentSignature = signature.ToHashCode();
        if (outputPoint == _cachedHitPoint && currentSignature == _cachedHitSignature)
        {
            hitDocument = _cachedHitDocument;
            return _cachedHitElement;
        }

        for (var index = _documents.Count - 1; index >= 0; index--)
        {
            var document = _documents[index];
            if (!document.IsVisible)
                continue;
            var hit = document.Root.HitTest(document.ToLayoutPoint(outputPoint));
            if (hit is null)
                continue;
            return CacheHit(outputPoint, currentSignature, hit, document, out hitDocument);
        }
        return CacheHit(outputPoint, currentSignature, null, null, out hitDocument);
    }

    private UiElement? CacheHit(
        Vector2 point,
        int signature,
        UiElement? element,
        UiDocument? document,
        out UiDocument? hitDocument)
    {
        _cachedHitPoint = point;
        _cachedHitSignature = signature;
        _cachedHitElement = element;
        _cachedHitDocument = document;
        hitDocument = document;
        return element;
    }

    private void DispatchTouchEvents()
    {
        foreach (var touch in _input.Touches)
        {
            var outputPosition = _renderer.ScreenToGameOutput(touch.Position);
            var current = HitTest(outputPosition, out var currentDocument);
            if (touch.Phase == ETouchPhase.Began && current is not null && currentDocument is not null)
            {
                _touchCaptures[touch.Id] = (current, currentDocument);
                current.RaiseTouchStarted(ToUiTouch(touch, currentDocument));
                continue;
            }

            if (!_touchCaptures.TryGetValue(touch.Id, out var capture))
                continue;
            var eventData = ToUiTouch(touch, capture.Document);
            switch (touch.Phase)
            {
                case ETouchPhase.Moved:
                    capture.Element.RaiseTouchMoved(eventData);
                    break;
                case ETouchPhase.Ended:
                    capture.Element.RaiseTouchEnded(eventData);
                    _touchCaptures.Remove(touch.Id);
                    break;
                case ETouchPhase.Cancelled:
                    capture.Element.RaiseTouchCancelled(eventData);
                    _touchCaptures.Remove(touch.Id);
                    break;
            }
        }
    }

    private UiTouchEvent ToUiTouch(TouchPoint touch, UiDocument document)
    {
        var current = _renderer.ScreenToGameOutput(touch.Position);
        var previous = _renderer.ScreenToGameOutput(touch.Position - touch.Delta);
        return new UiTouchEvent(
            touch.Id,
            document.ToLayoutPoint(current),
            (current - previous) / Math.Max(0.0001f, document.LayoutScale),
            touch.Pressure,
            touch.IsPrimary);
    }

    private static bool MovementMatchesScrollAxis(UiElement element, Vector2 movement)
    {
        var horizontal = element.CanScrollHorizontally;
        var vertical = element.CanScrollVertically;
        return horizontal && vertical ||
               horizontal && Math.Abs(movement.X) >= Math.Abs(movement.Y) ||
               vertical && Math.Abs(movement.Y) >= Math.Abs(movement.X);
    }

    private void DragScroll(Vector2 outputDelta, float deltaTime)
    {
        if (_scrollingElement is not { } element)
            return;
        var scale = Math.Max(0.0001f, _pressedDocument?.LayoutScale ?? 1.0f);
        var requested = -outputDelta / scale;
        if (!element.CanScrollHorizontally)
            requested.X = 0.0f;
        if (!element.CanScrollVertically)
            requested.Y = 0.0f;

        var before = element.ScrollOffset;
        element.ScrollBy(requested);
        var applied = element.ScrollOffset - before;
        if (deltaTime <= 0.0001f)
            return;
        var instantaneous = applied / deltaTime;
        _scrollVelocity = Vector2.Lerp(_scrollVelocity, instantaneous, 0.45f);
    }

    private void UpdateScrollInertia(float deltaTime)
    {
        if (_inertiaElement is not { } element || deltaTime <= 0.0f)
            return;

        var speed = _scrollVelocity.Length();
        if (speed < 8.0f || !float.IsFinite(speed))
        {
            _inertiaElement = null;
            _scrollVelocity = Vector2.Zero;
            return;
        }

        var before = element.ScrollOffset;
        element.ScrollBy(_scrollVelocity * deltaTime);
        var applied = element.ScrollOffset - before;
        if (Math.Abs(applied.X) < 0.001f)
            _scrollVelocity.X = 0.0f;
        if (Math.Abs(applied.Y) < 0.001f)
            _scrollVelocity.Y = 0.0f;

        speed = _scrollVelocity.Length();
        if (speed <= 0.0f)
        {
            _inertiaElement = null;
            return;
        }
        var replacement = Math.Max(0.0f, speed - Settings.ScrollDeceleration * deltaTime);
        _scrollVelocity *= replacement / speed;
    }

    private void HandleScrolling(UiElement? hit)
    {
        var wheel = _input.MouseWheelDelta;
        if (wheel.LengthSquared() <= float.Epsilon)
            return;

        var scrollable = FindScrollable(hit, preferHorizontal:
            _input.IsKeyPressed(EKeyboardKey.LeftShift) ||
            _input.IsKeyPressed(EKeyboardKey.RightShift));
        if (scrollable is null)
            return;

        _inertiaElement = null;
        _scrollVelocity = Vector2.Zero;

        var speed = Settings.ScrollSpeed;
        if ((_input.IsKeyPressed(EKeyboardKey.LeftShift) ||
             _input.IsKeyPressed(EKeyboardKey.RightShift)) && wheel.X == 0.0f)
            wheel = new Vector2(wheel.Y, 0.0f);
        scrollable.ScrollBy(new Vector2(-wheel.X, -wheel.Y) * speed);
    }

    private static UiElement? FindScrollable(UiElement? element, bool preferHorizontal)
    {
        for (; element is not null; element = element.Parent)
        {
            if (preferHorizontal && element.CanScrollHorizontally)
                return element;
            if (element.CanScrollVertically || element.CanScrollHorizontally)
                return element;
        }
        return null;
    }

    private void HandleKeyboardFocus()
    {
        var tabPressed = _input.IsKeyPressed(EKeyboardKey.Tab);
        if (tabPressed && !_wasTabPressed)
        {
            var backwards =
                _input.IsKeyPressed(EKeyboardKey.LeftShift) ||
                _input.IsKeyPressed(EKeyboardKey.RightShift);
            var candidates = _documents
                .Where(document => document.IsVisible)
                .SelectMany(document => document.Root.DescendantsAndSelf())
                .Where(element =>
                    element.IsFocusable &&
                    element.ComputedStyle.Display != "none" &&
                    element.ComputedStyle.Visibility != "hidden")
                .ToArray();
            if (candidates.Length > 0)
            {
                var current = Array.IndexOf(candidates, _focusedElement);
                var next = backwards
                    ? (current <= 0 ? candidates.Length - 1 : current - 1)
                    : (current + 1) % candidates.Length;
                Focus(candidates[next], true);
            }
        }
        _wasTabPressed = tabPressed;
    }

    private static UiElement? FindDropTarget(UiElement? hit, UiElement source)
    {
        for (var candidate = hit; candidate is not null; candidate = candidate.Parent)
        {
            if (!ReferenceEquals(candidate, source) && candidate.AcceptsDrop && !candidate.IsDisabled)
                return candidate;
        }
        return null;
    }

    private void SetDropTarget(UiElement? element)
    {
        if (ReferenceEquals(_dropTarget, element))
            return;
        if (_dropTarget is not null)
            _dropTarget.IsDropTarget = false;
        _dropTarget = element;
        if (_dropTarget is not null)
            _dropTarget.IsDropTarget = true;
    }

    private void RenderOverlay(RenderOverlayContext context)
    {
        if (!_initialized || _documents.Count == 0)
            return;
        try
        {
            _uiRenderer.Draw(_documents, context.Width, context.Height, Settings);
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Could not render Vecxy UI.");
        }
    }

    public void OnShutdown()
    {
        if (!_initialized)
            return;
        _overlays.UnregisterGameOverlay(RenderOverlay);
        foreach (var document in _documents)
            document.Dispose();
        _documents.Clear();
        _assets.UnregisterImporter<UiStyleSheetAsset>();
        _assets.UnregisterImporter<UiDocumentAsset>();
        _assets.UnregisterImporter<UiSpriteAtlasAsset>();
        _assets.UnregisterImporter<UiFontAsset>();
        _settings?.Dispose();
        _settings = null;
        _pressedElement = null;
        _pressedDocument = null;
        _scrollCandidate = null;
        _scrollingElement = null;
        _inertiaElement = null;
        _scrollVelocity = Vector2.Zero;
        _touchCaptures.Clear();
        _hoveredElements.Clear();
        _nextHoveredElements.Clear();
        _wasPointerPressed = false;
        Focus(null);
        _draggingElement = null;
        SetDropTarget(null);
        _inputCapture.SuppressMouse = false;
        _inputCapture.SuppressKeyboard = false;
        _initialized = false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        OnShutdown();
        _uiRenderer.Dispose();
        _disposed = true;
    }

    private UiConfig Settings => _settings?.TryGetValue(out var value) == true
        ? value!
        : new UiConfig();
}
