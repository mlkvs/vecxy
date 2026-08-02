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
    private readonly IInputManager _input;
    private readonly IInputCaptureState _inputCapture;
    private readonly IRenderer _renderer;
    private readonly IRenderOverlayStage _overlays;
    private readonly ITextureResolver _textures;
    private readonly UiRenderer _uiRenderer;
    private readonly Config _yogaConfig = new();
    private readonly List<UiDocument> _documents = [];
    private UiElement? _pressedElement;
    private UiElement? _focusedElement;
    private bool _wasLeftPressed;
    private bool _initialized;
    private bool _disposed;

    public IReadOnlyList<UiDocument> Documents => _documents;

    public UiModule(
        IAssetsManager assets,
        IInputManager input,
        IInputCaptureState inputCapture,
        IRenderer renderer,
        IRenderOverlayStage overlays,
        ITextureResolver textures,
        IGraphicsDeviceProvider graphics)
    {
        _assets = assets;
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

    public void OnInitialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized)
            return;
        _assets.RegisterImporter<UiDocumentAsset>(new UiDocumentAssetImporter());
        _assets.RegisterImporter<UiStyleSheetAsset>(new UiStyleSheetAssetImporter());
        _assets.RegisterImporter<UiFontAsset>(new UiFontAssetImporter());
        _assets.RegisterImporter<UiSpriteAtlasAsset>(new UiSpriteAtlasAssetImporter());
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
                document.Layout(_renderer.GameOutputWidth, _renderer.GameOutputHeight);
                foreach (var element in document.Root.DescendantsAndSelf())
                {
                    element.IsHovered = false;
                    element.IsActive = ReferenceEquals(element, _pressedElement);
                    element.IsFocused = ReferenceEquals(element, _focusedElement);
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

        var leftPressed = _input.IsMouseButtonPressed(EMouseButton.Left);
        if (leftPressed && !_wasLeftPressed)
        {
            _pressedElement = hit;
            _focusedElement = hit;
            if (_pressedElement is not null)
                _pressedElement.IsActive = true;
        }
        else if (!leftPressed && _wasLeftPressed)
        {
            var pressed = _pressedElement;
            _pressedElement = null;
            if (pressed is not null)
            {
                pressed.IsActive = false;
                if (ReferenceEquals(pressed, hit))
                    pressed.RaiseClicked();
            }
        }

        _wasLeftPressed = leftPressed;
        _inputCapture.SuppressMouse = hit is not null || _pressedElement is not null;
        _inputCapture.SuppressKeyboard =
            _focusedElement?.TagName is "input" or "textarea" or "select";
    }

    private void RenderOverlay(RenderOverlayContext context)
    {
        if (!_initialized || _documents.Count == 0)
            return;
        try
        {
            _uiRenderer.Draw(_documents, context.Width, context.Height);
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
        _pressedElement = null;
        _focusedElement = null;
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
}
