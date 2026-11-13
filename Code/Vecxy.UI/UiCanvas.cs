using System.Numerics;
using Vecxy.Assets;
using Vecxy.Prototypes;
using Vecxy.Rendering;
using Vecxy.Scene;

namespace Vecxy.UI;

public enum EUiRenderMode { ScreenOverlay, WorldSpace }

/// <summary>A normal UI document displayed on a scene plane or as a screen overlay.</summary>
[Authoring("UI Canvas", Category = "UI", Key = "uiCanvas", Description = "The plane supplies the UI area; its source mesh is hidden at runtime.")]
public sealed class UiCanvas(IUiManager ui, IAssetsManager? assets = null) : AComponent
{
    public sealed class Prototype(IUiManager ui, IAssetsManager assets) : APrototype<UiCanvas, Prototype.Options>
    {
        public sealed class Options
        {
            [Authoring("Document", AssetType = "UiDocument", Description = "UI XML asset path. The same XML/CSS works in both render modes.", Order = 0)]
            public string Document { get; set; } = "";
            [Authoring("Render mode", Order = 1)]
            public EUiRenderMode RenderMode { get; set; } = EUiRenderMode.WorldSpace;
            [Authoring("Pixels per metre", Min = 16, Max = 4096, Order = 2, Description = "Canvas layout density. Physical size comes from the plane, including its transform.")]
            public float PixelsPerMeter { get; set; } = 800;
            [Authoring("Interactive", Order = 3)]
            public bool Interactive { get; set; } = true;
            [Authoring("Double sided", Order = 4)]
            public bool DoubleSided { get; set; }
            [Authoring("Pointer mode", Order = 5)]
            public EUiWorldInputMode InputMode { get; set; } = EUiWorldInputMode.Mouse;
            [Authoring("Activation key", Order = 6, Description = "Press this key or the primary mouse button to activate the pointed UI element.")]
            public EKeyboardKey ActivationKey { get; set; } = EKeyboardKey.E;
            [Authoring("Cursor image", AssetType = "Texture2D", Order = 7, Description = "Optional custom icon for SurfaceCursor. The hotspot is its top-left corner.")]
            public string CursorImage { get; set; } = "";
            [Authoring("Cursor size", Min = 4, Max = 256, Unit = "px", Order = 8)]
            public float CursorSize { get; set; } = 44;
        }
        protected override UiCanvas Instantiate(IPrototypeContext context)
        {
            var ctx = RequireContext<ScenePrototypeContext>(context);
            return (ctx.Object ?? ctx.CreateObject("UI Canvas")).AddComponent(new UiCanvas(ui,assets));
        }
        protected override void Configure(UiCanvas target, Options options)
        {
            if (!float.IsFinite(options.PixelsPerMeter) || options.PixelsPerMeter <= 0) throw new ArgumentOutOfRangeException(nameof(options.PixelsPerMeter));
            target.RenderMode = options.RenderMode; target.PixelsPerMeter = options.PixelsPerMeter;
            target.Interactive = options.Interactive; target.DoubleSided = options.DoubleSided;
            target.InputMode = options.InputMode; target.ActivationKey = options.ActivationKey;
            target.CursorImage = options.CursorImage; target.CursorSize = options.CursorSize;
            target.Load(options.Document);
        }
    }

    private readonly List<(MeshRenderer Renderer, bool Enabled)> _sourceMeshes = [];
    private MeshRenderer? _surface;
    private Camera? _camera;
    private int _outputWidth = 1, _outputHeight = 1;
    internal UiConfig WorldSettings { get; } = new() { ScaleMode = "none" };
    public UiDocument? Document { get; private set; }
    public EUiRenderMode RenderMode { get; set; } = EUiRenderMode.WorldSpace;
    private float _pixelsPerMeter = 800;
    public float PixelsPerMeter
    {
        get => _pixelsPerMeter;
        set
        {
            if (!float.IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
            _pixelsPerMeter = value;
        }
    }
    private bool _interactive = true;
    public bool Interactive
    {
        get => _interactive;
        set { _interactive = value; if (!value) ReleaseFocus(); }
    }
    public bool DoubleSided { get; set; }

    public EUiWorldInputMode InputMode { get; set; }
    public EKeyboardKey ActivationKey { get; set; } = EKeyboardKey.E;
    public string CursorImage { get; set; } = "";
    public float CursorSize { get; set; } = 44;
    private readonly UiSurfacePointer _surfacePointer = new();
    private bool _capturedInput;
    private UiElement? _cursor;
    private string _cursorImage = "";
    /// <summary>Optional scene blocker query: origin, normalized direction and distance to the canvas.</summary>
    public Func<Vector3,Vector3,float,bool>? IsRayOccluded { get; set; }
    private int _occlusionFrame;
    public bool CursorVisible => _cursor?.IsVisible == true && CapturesMouseLook;
    public Vector2? CursorPosition => CapturesMouseLook && RaycastOutput(_surfacePointer.Output,true,out var p,out _)
        ? p / Math.Max(.0001f,Document?.LayoutScale ?? 1) : null;
    public bool CapturesMouseLook => IsActive && Interactive && RenderMode == EUiRenderMode.WorldSpace &&
        _capturedInput && InputMode == EUiWorldInputMode.SurfaceCursor && _surfacePointer.Active;
    internal bool UsesViewRay => _capturedInput && InputMode == EUiWorldInputMode.ViewRay;

    public void Load(IAssetHandle handle) { ArgumentNullException.ThrowIfNull(handle); Load(handle.Id.ToString()); }

    public void Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (SceneObject is null) throw new InvalidOperationException("Attach UI Canvas to a scene object before loading its document.");
        if (_surface is null)
        {
            foreach (var item in SceneObject.EnumerateHierarchy())
                if (item.GetComponent<MeshRenderer>() is { IsConfigured: true } mesh)
                { _sourceMeshes.Add((mesh, mesh.Enabled)); _surface ??= mesh; }
        }
        if (RenderMode == EUiRenderMode.WorldSpace && !TryGetPlane(out _))
            throw new InvalidOperationException("World UI Canvas requires a plane mesh on its object or a child.");
        var next = Guid.TryParse(path, out var id) ? ui.Load(new AssetHandle(id)) : ui.Load(path);
        next.Canvas = this;
        if (Document is not null) { Document.Reloaded -= OnDocumentReloaded; ui.Unload(Document); }
        Document = next; _cursor = null;
        next.Reloaded += OnDocumentReloaded;
        foreach (var (mesh, _) in _sourceMeshes) mesh.Enabled = false;
    }

    internal void UpdateView(int width, int height, UiConfig settings, Vector2 pointerDelta, bool captured)
    {
        _outputWidth = Math.Max(1, width); _outputHeight = Math.Max(1, height); _camera = null;
        if (SceneInstance.IsActive)
            foreach (var obj in SceneInstance.Objects)
                if (obj.IsActive && obj.GetComponent<Camera>() is { IsActive: true, TargetTexture: null } camera &&
                    (_camera is null || camera.Priority > _camera.Priority)) _camera = camera;
        WorldSettings.EnableShadows = settings.EnableShadows;
        WorldSettings.SpriteAtlases = settings.SpriteAtlases;
        if (RenderMode == EUiRenderMode.WorldSpace && TryGetPlane(out var plane))
        { WorldSettings.ReferenceResolution[0] = plane.Width; WorldSettings.ReferenceResolution[1] = plane.Height; }
        if (IsRayOccluded is not null) _occlusionFrame++;
        _capturedInput = captured && RenderMode == EUiRenderMode.WorldSpace && Interactive && Document is { CanRender: true };
        var centre = new Vector2(_outputWidth*.5f,_outputHeight*.5f);
        if (_capturedInput && InputMode == EUiWorldInputMode.SurfaceCursor && IsActive)
        {
            var centreInside = RaycastOutput(centre,true,out _,out _);
            var movedInside = RaycastOutput(_surfacePointer.Output+pointerDelta,true,out _,out _);
            _surfacePointer.Update(centreInside,movedInside,centre,pointerDelta);
        }
        else _surfacePointer.Reset();
        UpdateCursor();
    }

    internal bool TryGetPlane(out UiWorldPlane plane)
    {
        plane = default;
        if (_surface is not { IsDestroyed: false, IsConfigured: true }) return false;
        plane = UiWorldPlane.FromBounds(_surface.LocalBoundsMin, _surface.LocalBoundsMax, _surface.Transform.WorldMatrix, PixelsPerMeter);
        return true;
    }

    internal int HitSignature => HashCode.Combine(
        HashCode.Combine(HashCode.Combine(IsActive, Interactive, RenderMode, PixelsPerMeter, DoubleSided, InputMode, _surfacePointer.Output, _surfacePointer.Active),_occlusionFrame,_capturedInput),
        _surface is { IsDestroyed: false, IsConfigured: true } ? _surface.Mesh : null,
        _surface is { IsDestroyed: false } ? _surface.Transform.WorldMatrix : Matrix4x4.Identity,
        _camera is { IsDestroyed: false } ? _camera.Transform.WorldMatrix : Matrix4x4.Identity,
        _camera is { IsDestroyed: false } ? _camera.GetProjectionMatrix(_outputWidth / (float)_outputHeight) : Matrix4x4.Identity);

    public bool Raycast(Vector3 origin, Vector3 direction, out UiRaycastHit hit, float maxDistance = 1000)
    {
        hit = default;
        if (!Interactive || !IsActive || RenderMode != EUiRenderMode.WorldSpace || Document is not { CanRender: true } document ||
            !TryGetPlane(out var plane) || direction.LengthSquared() < 1e-12f || !float.IsFinite(direction.LengthSquared())) return false;
        if (!plane.TryRaycast(origin,Vector3.Normalize(direction),maxDistance,true,DoubleSided,out var pixel,out var distance)) return false;
        if (IsRayOccluded?.Invoke(origin,Vector3.Normalize(direction),distance) == true) return false;
        var point = pixel / Math.Max(.0001f,document.LayoutScale);
        hit = new UiRaycastHit(this,document,document.Root.HitTest(point),point,distance);
        return true;
    }

    private void OnDocumentReloaded(UiDocument document) { _cursor = null; }

    private void UpdateCursor()
    {
        if (Document is not { } document) return;
        if (InputMode != EUiWorldInputMode.SurfaceCursor)
        { if (_cursor is not null) _cursor.IsVisible = false; return; }
        if (_cursor is null || _cursorImage != CursorImage)
        {
            _cursor?.RemoveFromParent(); _cursor?.ReleaseLayout();
            var path = CursorImage;
            if (Guid.TryParse(path,out var id)) path = assets?.GetPath(new AssetHandle(id)) ?? "";
            _cursor = path.Length > 0 ? document.CreateImage("/"+path.TrimStart('/')) : document.CreatePanel();
            _cursorImage = CursorImage;
            _cursor.SetAttribute("id","__vecxy-world-cursor");
            _cursor.SetStyle("position","absolute"); _cursor.SetStyle("left","0px"); _cursor.SetStyle("top","0px");
            _cursor.SetStyle("pointer-events","none"); _cursor.SetStyle("z-index","100000");
            if (path.Length == 0) { _cursor.SetStyle("background-color","white"); _cursor.SetStyle("border-radius","50%"); }
            document.Root.Add(_cursor);
        }
        var pixel = Vector2.Zero;
        var visible = CapturesMouseLook && RaycastOutput(_surfacePointer.Output,true,out pixel,out _);
        _cursor.IsVisible = visible;
        if (!visible) return;
        var point = pixel / Math.Max(.0001f,document.LayoutScale);
        var size = CursorImage.Length > 0 ? Math.Clamp(CursorSize,4,256) : 6;
        _cursor.SetStyle("width",size.ToString(System.Globalization.CultureInfo.InvariantCulture)+"px");
        _cursor.SetStyle("height",size.ToString(System.Globalization.CultureInfo.InvariantCulture)+"px");
        _cursor.SetStyle("transform",FormattableString.Invariant($"translate({point.X}px, {point.Y}px)"));
    }

    internal bool TryMapOutputPoint(Vector2 output, bool clip, out Vector2 point, out float distance)
    {
        if (_capturedInput && InputMode == EUiWorldInputMode.ViewRay) output = new Vector2(_outputWidth*.5f,_outputHeight*.5f);
        else if (_capturedInput && InputMode == EUiWorldInputMode.SurfaceCursor)
        { if (!_surfacePointer.Active) { point = default; distance = 0; return false; } output = _surfacePointer.Output; }
        return RaycastOutput(output,clip,out point,out distance);
    }

    private bool RaycastOutput(Vector2 output, bool clip, out Vector2 point, out float distance)
    {
        point = default; distance = 0;
        if (_camera is not { IsDestroyed: false } || !TryGetPlane(out var plane)) return false;
        var viewProjection = _camera.ViewMatrix * _camera.GetProjectionMatrix(_outputWidth / (float)_outputHeight);
        if (!Matrix4x4.Invert(viewProjection, out var inverse)) return false;
        var x = output.X / _outputWidth * 2 - 1; var y = 1 - output.Y / _outputHeight * 2;
        static Vector3 Unproject(Vector4 p, Matrix4x4 m) { var v = Vector4.Transform(p, m); return new Vector3(v.X, v.Y, v.Z) / v.W; }
        var near = Unproject(new Vector4(x, y, -1, 1), inverse);
        var far = Unproject(new Vector4(x, y, 1, 1), inverse); var delta = far - near;
        var direction = Vector3.Normalize(delta);
        if (!plane.TryRaycast(near,direction,delta.Length(),clip,DoubleSided,out point,out distance)) return false;
        return !clip || IsRayOccluded?.Invoke(near,direction,distance) != true;
    }

    private void ReleaseFocus()
    {
        if (Document is { } doc && ui.FocusedElement is { } focused && doc.Root.DescendantsAndSelf().Contains(focused)) ui.Focus(null);
    }

    public override void OnDisable() => ReleaseFocus();

    public override void OnDestroy()
    {
        if (Document is not null) { Document.Reloaded -= OnDocumentReloaded; ui.Unload(Document); Document = null; }
        _surfacePointer.Reset(); _cursor = null; IsRayOccluded = null;
        foreach (var (mesh, enabled) in _sourceMeshes) if (!mesh.IsDestroyed) mesh.Enabled = enabled;
        _sourceMeshes.Clear(); _surface = null;
    }
}
