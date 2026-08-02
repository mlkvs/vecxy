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
    IUiManager
{
    public sealed class Definition : AModuleDefinition<UiModule>
    {
        protected override IReadOnlyList<Type> Exports => [typeof(IUiManager)];

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
    private readonly Config _yogaConfig = new();
    private readonly List<UiDocument> _documents = [];
    private ConfigRef<UiConfig>? _settings;
    private UiElement? _pressedElement;
    private UiElement? _focusedElement;
    private UiElement? _draggingElement;
    private UiElement? _dropTarget;
    private Vector2 _pressPosition;
    private bool _wasLeftPressed;
    private bool _wasTabPressed;
    private bool _initialized;
    private bool _disposed;

    public IReadOnlyList<UiDocument> Documents => _documents;

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
        _uiRenderer = new UiRenderer(graphics.GraphicsDevice);
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
        _configs.Register<UiConfig>();
        _settings = _configs.LoadConfig<UiConfig>("Configs/UI.yaml");
        _overlays.RegisterGameOverlay(RenderOverlay);
        _initialized = true;
    }

    public void OnUpdate(float deltaTime)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_initialized)
            return;

        foreach (var document in _documents)
        {
            try
            {
                document.UpdateAnimations(
                    deltaTime,
                    _renderer.GameOutputWidth,
                    _renderer.GameOutputHeight,
                    Settings);
                document.Layout(_renderer.GameOutputWidth, _renderer.GameOutputHeight, Settings);
                foreach (var element in document.Root.DescendantsAndSelf())
                {
                    element.IsHovered = false;
                    element.IsActive = ReferenceEquals(element, _pressedElement);
                    element.IsFocused = ReferenceEquals(element, _focusedElement);
                    element.IsFocusVisible = element.IsFocused && element.IsFocusVisible;
                    element.IsDragging = ReferenceEquals(element, _draggingElement);
                    element.IsDropTarget = ReferenceEquals(element, _dropTarget);
                }
            }
            catch (Exception exception)
            {
                Logger.Error(exception, $"Could not update UI document: {document.Path}");
            }
        }

        var pointer = _renderer.ScreenToGameOutput(_input.MousePosition);
        UiElement? hit = null;
        foreach (var document in _documents.AsEnumerable().Reverse())
        {
            if (!document.IsVisible)
                continue;
            hit = document.Root.HitTest(document.ToLayoutPoint(pointer));
            if (hit is not null)
                break;
        }

        for (var hovered = hit; hovered is not null; hovered = hovered.Parent)
            hovered.IsHovered = true;

        HandleKeyboardFocus();
        HandleScrolling(hit);

        var leftPressed = _input.IsMouseButtonPressed(EMouseButton.Left);
        if (leftPressed && !_wasLeftPressed)
        {
            _pressedElement = hit;
            _pressPosition = pointer;
            Focus(hit, false);
            if (_pressedElement is not null)
                _pressedElement.IsActive = true;
        }
        else if (leftPressed && _wasLeftPressed && _pressedElement is { } pressed)
        {
            if (_draggingElement is null && pressed.IsDraggable &&
                Vector2.DistanceSquared(pointer, _pressPosition) >= 36.0f)
            {
                _draggingElement = pressed;
                pressed.IsDragging = true;
                pressed.RaiseDragStarted();
            }

            if (_draggingElement is not null)
                SetDropTarget(FindDropTarget(hit, _draggingElement));
        }
        else if (!leftPressed && _wasLeftPressed)
        {
            var releasedElement = _pressedElement;
            _pressedElement = null;
            if (releasedElement is not null)
            {
                releasedElement.IsActive = false;
                if (_draggingElement is not null)
                {
                    _dropTarget?.RaiseDropped(_draggingElement);
                    _draggingElement.IsDragging = false;
                    _draggingElement.RaiseDragEnded();
                    _draggingElement = null;
                    SetDropTarget(null);
                }
                else if (ReferenceEquals(releasedElement, hit))
                    releasedElement.RaiseClicked();
            }
        }

        _wasLeftPressed = leftPressed;
        _inputCapture.SuppressMouse = hit is not null || _pressedElement is not null;
        _inputCapture.SuppressKeyboard =
            _focusedElement?.TagName is "input" or "textarea" or "select";
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
