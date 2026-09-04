using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using StbImageSharp;
using Vecxy.Assets;
using Vecxy.Input;
using Vecxy.Kernel;
using Vecxy.Rendering;
using Vecxy.UI;

namespace Vecxy.SpriteEditor;

public sealed class SpriteEditorController(
    AtlasRepository repository, ProjectFolderDialog dialog, ITextureResolver textures,
    IInputManager input, IWindow window, IUiManager ui, RecentFiles recentFiles) : IDisposable
{
    private readonly AtlasDocument _working = new();
    private readonly HashSet<string> _selection = new(StringComparer.Ordinal);
    private readonly Dictionary<string, UiButton> _frames = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<IWindow.KeyEvent> _keys = new();
    private UiDocument? _ui;
    private SpriteProject? _project;
    private byte[] _pixels = [];
    private int _imageWidth, _imageHeight;
    private float _zoom = 1;
    private bool _sliceTool, _snap = true, _grid = true, _checkerboard = true, _syncing;
    private string? _draggingSlice;
    private ResizeMode _resizeMode;
    private UiButton? _selectedFrame;
    private UiPanel? _stage, _overlay, _previewOverlay, _viewport, _canvasContent, _assetList, _sliceList, _autoPanel, _recentList, _gridLayer;
    private UiImage? _preview;
    private UiText? _status, _documentName, _textureName, _imageInfo, _zoomLabel, _projectLabel, _sliceCount;
    private UiInputField? _selectionName, _sliceSearch;
    private UiButton? _creationFrame;
    private Vector2 _creationStart;
    private IReadOnlyList<SpriteSlice> _autoPreview = [];
    private bool _autoGrid, _assetsCollapsed;

    public void Bind(UiDocument document)
    {
        _ui = document;
        var workspace = document.GetElementById<UiPanel>("workspace");
        Instantiate(workspace, "Components/TopBar.xml");
        var content = document.GetElementById<UiPanel>("content");
        Instantiate(content, "Components/AssetBrowser.xml"); Instantiate(content, "Components/Canvas.xml"); Instantiate(content, "Components/Inspector.xml");
        _assetList = Get<UiPanel>("asset-list"); _sliceList = Get<UiPanel>("slice-list"); _preview = Get<UiImage>("sprite-preview");
        _stage = Get<UiPanel>("image-stage"); _overlay = Get<UiPanel>("slice-overlay"); _previewOverlay = Get<UiPanel>("preview-overlay"); _gridLayer = Get<UiPanel>("grid-layer");
        _viewport = Get<UiPanel>("canvas-viewport"); _canvasContent = Get<UiPanel>("canvas-content"); _autoPanel = Get<UiPanel>("auto-panel"); _recentList = Get<UiPanel>("recent-files");
        _status = Get<UiText>("status-text"); _documentName = Get<UiText>("document-name"); _textureName = Get<UiText>("texture-name");
        _imageInfo = Get<UiText>("image-info"); _zoomLabel = Get<UiText>("zoom-label"); _projectLabel = Get<UiText>("project-label"); _sliceCount = Get<UiText>("slice-count");
        _selectionName = Get<UiInputField>("selection-name"); _sliceSearch = Get<UiInputField>("slice-search");
        _selectionName.TextChanged += RenameSelected; _sliceSearch.TextChanged += _ => RebuildSliceList();
        foreach (var id in new[] { "rect-x", "rect-y", "rect-w", "rect-h", "pivot-x", "pivot-y" }) Get<UiInputField>(id).Submitted += _ => ApplyInspector();
        foreach (var id in new[] { "alpha-threshold", "min-width", "min-height", "merge-distance", "slice-padding" }) Get<UiInputField>(id).TextChanged += _ => { if (_autoPanel.IsVisible) RecomputeAuto(); };
        BindCommands();
        _overlay.PointerPressed += (_, e) => BeginCreate(e.Position);
        _overlay.PointerMoved += (_, e) => UpdateCreate(e.Position);
        _overlay.PointerReleased += (_, e) => EndCreate(e.Position);
        var argument = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(Directory.Exists);
        if (argument is not null) OpenProject(argument);
        RebuildRecentFiles(); RefreshAll();
    }

    private void BindCommands()
    {
        Click("open-project", ChooseProject); Click("open-file", OpenFile); Click("save-atlas", Save); Click("save-as", SaveAs); Click("close-document", CloseDocument);
        Click("undo", Undo); Click("redo", Redo); Click("duplicate-slice", Duplicate); Click("delete-slice-menu", DeleteSelected);
        Click("select-tool", () => SetTool(false)); Click("slice-tool", () => SetTool(true)); Click("auto-slice", () => ShowAuto(false));
        Click("auto-transparency-menu", () => ShowAuto(false)); Click("auto-grid-menu", () => ShowAuto(true)); Click("trim-menu", TrimSelected);
        Click("apply-auto", ApplyAuto); Click("cancel-auto", CancelAuto);
        Click("zoom-in", () => SetZoom(_zoom * 2)); Click("zoom-out", () => SetZoom(_zoom * .5f)); Click("zoom-100", () => SetZoom(1)); Click("zoom-100-menu", () => SetZoom(1));
        Click("zoom-fit", Fit); Click("fit-menu", Fit); Click("toggle-grid", ToggleGrid); Click("toggle-grid-menu", ToggleGrid); Click("toggle-checker-menu", ToggleChecker); Click("toggle-snap", ToggleSnap);
        Click("pivot-tl", () => SetPivot(0, 1)); Click("pivot-t", () => SetPivot(.5f, 1)); Click("pivot-tr", () => SetPivot(1, 1));
        Click("pivot-l", () => SetPivot(0, .5f)); Click("pivot-center", () => SetPivot(.5f, .5f)); Click("pivot-r", () => SetPivot(1, .5f));
        Click("pivot-bl", () => SetPivot(0, 0)); Click("pivot-bottom", () => SetPivot(.5f, 0)); Click("pivot-br", () => SetPivot(1, 0));
        Click("collapse-assets", ToggleAssets);
        Menu("menu-file", "file-menu"); Menu("menu-edit", "edit-menu"); Menu("menu-view", "view-menu"); Menu("menu-slice", "slice-menu");
    }

    private void Menu(string button, string panel) => Click(button, () =>
    {
        var target = Get<UiPanel>(panel);
        foreach (var id in new[] { "file-menu", "edit-menu", "view-menu", "slice-menu" }) if (id != panel) Get<UiPanel>(id).IsVisible = false;
        target.IsVisible = !target.IsVisible;
    });

    private void ChooseProject() { if (dialog.Open() is { } path) OpenProject(path); }
    private void OpenFile() { if (dialog.OpenAsset(_project?.AssetsDirectory) is { } path) OpenPath(path); }
    private void OpenProject(string path)
    {
        try { _project = SpriteProject.Open(path); _projectLabel!.Value = Path.GetFileName(_project.Root.TrimEnd(Path.DirectorySeparatorChar)); RebuildAssets(); SetStatus($"{_project.Images.Count} textures"); }
        catch (Exception e) { SetStatus(e.Message, true); }
    }

    private void OpenPath(string path)
    {
        if (!ConfirmDiscard()) return;
        try
        {
            if (_project is null) OpenProject(FindProjectRoot(path));
            if (Path.GetExtension(path).Equals(".atlas", StringComparison.OrdinalIgnoreCase)) OpenAtlas(path); else OpenTexture(path);
            recentFiles.Add(path); RebuildRecentFiles();
        }
        catch (Exception e) { SetStatus(e.Message, true); }
    }

    private void OpenTexture(string path)
    {
        path = Path.GetFullPath(path);
        var sameName = Path.ChangeExtension(path, ".atlas");
        var linked = File.Exists(sameName) ? sameName : _project?.Atlases.FirstOrDefault(atlas => AtlasLinksTexture(atlas, path));
        if (linked is not null) { OpenAtlas(linked); return; }
        LoadTexture(path);
        _working.Open(new SpriteAtlas { Texture = Path.GetFileName(path) }, path);
        _selection.Clear(); RefreshAll(); SetStatus("Atlas not created — add or auto-slice");
    }

    private void OpenAtlas(string path)
    {
        var atlas = repository.Load(path);
        var texture = AtlasTexturePath(atlas);
        if (!File.Exists(texture)) { _pixels=[]; _imageWidth=_imageHeight=0; _preview!.Texture=null; _working.Open(atlas, texture); _selection.Clear(); RefreshAll(); SetStatus($"Texture not found: {atlas.Texture}", true); return; }
        LoadTexture(texture); _working.Open(atlas, texture); _selection.Clear(); RefreshAll(); SetStatus($"Opened {_project?.Relative(path) ?? path}");
    }

    private void LoadTexture(string path)
    {
        using var stream = File.OpenRead(path);
        var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        _pixels = image.Data; _imageWidth = image.Width; _imageHeight = image.Height;
        _preview!.Texture = textures.Resolve(TextureAsset.FromRgba(image.Width, image.Height, image.Data));
        SetZoom(1); Fit();
    }

    private string AtlasTexturePath(SpriteAtlas atlas) => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(atlas.FilePath) ?? _project?.AssetsDirectory ?? Directory.GetCurrentDirectory(), atlas.Texture));
    private bool AtlasLinksTexture(string atlasPath, string texturePath) { try { return AtlasTexturePath(repository.Load(atlasPath)).Equals(texturePath, StringComparison.OrdinalIgnoreCase); } catch { return false; } }
    private static string FindProjectRoot(string path) { var dir = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(path))!); while (dir is not null && !dir.Name.Equals("Assets", StringComparison.OrdinalIgnoreCase)) dir = dir.Parent; return dir?.Parent?.FullName ?? Path.GetDirectoryName(path)!; }

    private void RebuildAssets()
    {
        _assetList!.Clear();
        foreach (var image in _project!.Images)
        {
            AddAsset($"▧  {_project.Relative(image)}", image, false);
            foreach (var atlas in _project.Atlases.Where(atlas => Path.ChangeExtension(image, ".atlas").Equals(atlas, StringComparison.OrdinalIgnoreCase))) AddAsset($"↳  {Path.GetFileName(atlas)}", atlas, true);
        }
    }
    private void RebuildRecentFiles()
    {
        if (_recentList is null) return;
        _recentList.Clear();
        foreach (var path in recentFiles.Items.Take(6))
        {
            var label = _project is not null && path.StartsWith(_project.AssetsDirectory, StringComparison.OrdinalIgnoreCase) ? _project.Relative(path) : Path.GetFileName(path);
            var button = _ui!.CreateButton(label, new Dictionary<string, string> { ["title"] = path });
            button.Clicked += _ => { HideMenus(); OpenPath(path); };
            _recentList.Add(button);
        }
    }
    private void AddAsset(string label, string path, bool atlas) { var button = _ui!.CreateButton(label, new Dictionary<string, string> { ["class"] = atlas ? "asset-row asset-atlas" : "asset-row" }); button.Clicked += _ => OpenPath(path); _assetList!.Add(button); }

    private void RefreshAll() { UpdateDocumentHeader(); UpdateStage(); RebuildSlices(); SyncInspector(); }
    private void UpdateDocumentHeader()
    {
        var atlas = _working.Atlas;
        var name = _working.TexturePath.Length == 0 ? "No document" : atlas.FilePath is null ? Path.GetFileName(_working.TexturePath) : Path.GetFileName(atlas.FilePath);
        _documentName!.Value = name + (_working.Dirty ? " *" : "");
        _textureName!.Value = _working.TexturePath.Length == 0 ? "Open a PNG or atlas" : atlas.FilePath is null ? "Atlas: not created" : Path.GetFileName(_working.TexturePath);
        _imageInfo!.Value = _imageWidth == 0 ? "No texture" : $"{_imageWidth} × {_imageHeight}  ·  {atlas.Sprites.Count} slices";
    }
    private void UpdateStage()
    {
        if (_stage is null || _imageWidth == 0) return;
        _stage.Style.Width = $"{_imageWidth * _zoom:0.##}ui"; _stage.Style.Height = $"{_imageHeight * _zoom:0.##}ui";
        _canvasContent!.Style.Width = $"{Math.Max(1600, _imageWidth * _zoom + 1200):0}ui"; _canvasContent.Style.Height = $"{Math.Max(1200, _imageHeight * _zoom + 800):0}ui";
        _zoomLabel!.Value = $"{_zoom * 100:0}%"; RebuildGrid();
    }

    private void RebuildGrid()
    {
        if (_gridLayer is null) return;
        _gridLayer.Clear();
        if (!_grid || _imageWidth == 0) return;
        const int step = 32;
        for (var x = step; x < _imageWidth; x += step)
        {
            var line = _ui!.CreatePanel(new Dictionary<string,string>{{"class","grid-line vertical"}});
            line.Style.Set("left",$"{100f*x/_imageWidth:0.####}%"); _gridLayer.Add(line);
        }
        for (var y = step; y < _imageHeight; y += step)
        {
            var line = _ui!.CreatePanel(new Dictionary<string,string>{{"class","grid-line horizontal"}});
            line.Style.Set("top",$"{100f*y/_imageHeight:0.####}%"); _gridLayer.Add(line);
        }
    }

    private void RebuildSlices()
    {
        _overlay!.Clear(); _frames.Clear();
        foreach (var (name, slice) in _working.Atlas.Sprites)
        {
            var frame = _ui!.CreateButton("", new Dictionary<string, string> { ["class"] = _selection.Contains(name) ? "slice-frame selected" : "slice-frame", ["draggable"] = "true" });
            UpdateFrame(frame, slice); frame.Clicked += _ => Select(name); frame.DragStarted += _ => StartDrag(name, frame, ResizeMode.Move); frame.DragEnded += _ => EndDrag();
            if (_selection.Contains(name) && name == _selection.LastOrDefault()) AddHandles(frame, name);
            _overlay.Add(frame); _frames[name] = frame;
        }
        RebuildSliceList(); UpdateDocumentHeader();
    }

    private void RebuildSliceList()
    {
        _sliceList!.Clear(); var search = _sliceSearch?.Text ?? "";
        foreach (var name in _working.Atlas.Sprites.Keys.Where(name => name.Contains(search, StringComparison.OrdinalIgnoreCase)))
        { var row = _ui!.CreateButton(name, new Dictionary<string, string> { ["class"] = _selection.Contains(name) ? "slice-row selected" : "slice-row" }); row.Clicked += _ => Select(name); _sliceList.Add(row); }
        _sliceCount!.Value = _working.Atlas.Sprites.Count.ToString(CultureInfo.InvariantCulture);
    }

    private void Select(string name)
    {
        var multi = input.IsKeyPressed(EKeyboardKey.LeftControl) || input.IsKeyPressed(EKeyboardKey.RightControl);
        if (!multi) _selection.Clear();
        if (!_selection.Add(name) && multi) _selection.Remove(name);
        RebuildSlices(); SyncInspector();
    }

    private SpriteSlice? PrimarySlice() => _selection.LastOrDefault() is { } name && _working.Atlas.Sprites.TryGetValue(name, out var slice) ? slice : null;
    private string? PrimaryName() => _selection.LastOrDefault();

    private void AddHandles(UiButton frame, string name)
    {
        foreach (var (css, mode) in new[] { ("nw",ResizeMode.TopLeft),("n",ResizeMode.Top),("ne",ResizeMode.TopRight),("w",ResizeMode.Left),("e",ResizeMode.Right),("sw",ResizeMode.BottomLeft),("s",ResizeMode.Bottom),("se",ResizeMode.BottomRight) })
        { var handle = _ui!.CreateButton("", new Dictionary<string,string>{{"class",$"resize-handle {css}"},{"draggable","true"}}); handle.DragStarted += _ => StartDrag(name, frame, mode); handle.DragEnded += _ => EndDrag(); frame.Add(handle); }
        var pivot = _ui!.CreateButton("", new Dictionary<string,string>{{"class","pivot-handle"},{"draggable","true"}}); var slice = _working.Atlas.Sprites[name];
        pivot.Style.Set("left", $"{slice.PivotX*100:0.##}%"); pivot.Style.Set("top", $"{(1-slice.PivotY)*100:0.##}%"); pivot.DragStarted += _ => StartDrag(name, frame, ResizeMode.Pivot); pivot.DragEnded += _ => EndDrag(); frame.Add(pivot);
    }

    private void StartDrag(string name, UiButton frame, ResizeMode mode) { if(!_selection.Contains(name)){_selection.Clear();_selection.Add(name);} _working.BeginEdit(); _draggingSlice=name; _selectedFrame=frame; _resizeMode=mode; }
    private void EndDrag() { _working.CommitEdit(); _draggingSlice=null; _selectedFrame=null; _resizeMode=ResizeMode.None; RebuildSlices(); SyncInspector(); }

    public void Update()
    {
        ProcessKeys();
        if ((input.IsMouseButtonPressed(EMouseButton.Middle) || input.IsKeyPressed(EKeyboardKey.Space) && input.IsPrimaryPointerPressed) && _viewport is not null) _viewport.ScrollBy(-input.PointerDelta);
        var wheel = input.MouseWheelDelta.Y;
        if (Math.Abs(wheel) > .01f)
        {
            var ctrl = input.IsKeyPressed(EKeyboardKey.LeftControl) || input.IsKeyPressed(EKeyboardKey.RightControl);
            var shift = input.IsKeyPressed(EKeyboardKey.LeftShift) || input.IsKeyPressed(EKeyboardKey.RightShift);
            if (ctrl) SetZoomAt(_zoom * (wheel > 0 ? 1.25f : .8f), input.PointerPosition);
            else if (_viewport is not null) _viewport.ScrollBy(shift ? new Vector2(-wheel * 48, 0) : new Vector2(0, -wheel * 48));
        }
        if (_draggingSlice is null || _selectedFrame is null || !_working.Atlas.Sprites.TryGetValue(_draggingSlice, out var slice) || _stage is null) return;
        var dx=(int)MathF.Round(input.PointerDelta.X/_zoom); var dy=(int)MathF.Round(input.PointerDelta.Y/_zoom);
        if (_resizeMode==ResizeMode.Pivot) { slice.PivotX=Math.Clamp(slice.PivotX+input.PointerDelta.X/Math.Max(1,_selectedFrame.Bounds.Width),0,1); slice.PivotY=Math.Clamp(slice.PivotY-input.PointerDelta.Y/Math.Max(1,_selectedFrame.Bounds.Height),0,1); }
        else if (_resizeMode == ResizeMode.Move)
        {
            foreach (var name in _selection)
            {
                var selected = _working.Atlas.Sprites[name]; Manipulate(selected,dx,dy,_resizeMode);
                if (_frames.TryGetValue(name,out var selectedFrame)) UpdateFrameVisual(selectedFrame,selected);
            }
        }
        else Manipulate(slice,dx,dy,_resizeMode);
        if (_resizeMode != ResizeMode.Move) UpdateFrameVisual(_selectedFrame,slice); SyncInspectorValues(); UpdateDocumentHeader();
    }

    private void Manipulate(SpriteSlice s,int dx,int dy,ResizeMode mode)
    {
        if(mode==ResizeMode.Move){s.X=Math.Clamp(s.X+dx,0,Math.Max(0,_imageWidth-s.Width));s.Y=Math.Clamp(s.Y+dy,0,Math.Max(0,_imageHeight-s.Height));return;}
        var l=s.X;var t=s.Y;var r=s.X+s.Width;var b=s.Y+s.Height;
        if(mode is ResizeMode.Left or ResizeMode.TopLeft or ResizeMode.BottomLeft)l=Math.Clamp(l+dx,0,r-1); if(mode is ResizeMode.Right or ResizeMode.TopRight or ResizeMode.BottomRight)r=Math.Clamp(r+dx,l+1,_imageWidth);
        if(mode is ResizeMode.Top or ResizeMode.TopLeft or ResizeMode.TopRight)t=Math.Clamp(t+dy,0,b-1); if(mode is ResizeMode.Bottom or ResizeMode.BottomLeft or ResizeMode.BottomRight)b=Math.Clamp(b+dy,t+1,_imageHeight);
        (s.X,s.Y,s.Width,s.Height)=(l,t,r-l,b-t);
    }

    private void UpdateFrame(UiButton frame,SpriteSlice s){frame.Style.Set("left",$"{100f*s.X/Math.Max(1,_imageWidth):0.####}%");frame.Style.Set("top",$"{100f*s.Y/Math.Max(1,_imageHeight):0.####}%");frame.Style.Width=$"{100f*s.Width/Math.Max(1,_imageWidth):0.####}%";frame.Style.Height=$"{100f*s.Height/Math.Max(1,_imageHeight):0.####}%";var p=frame.Children.FirstOrDefault(x=>x.Classes.Contains("pivot-handle"));if(p is not null){p.Style.Set("left",$"{s.PivotX*100:0.##}%");p.Style.Set("top",$"{(1-s.PivotY)*100:0.##}%");}}

    private void UpdateFrameVisual(UiButton frame, SpriteSlice slice)
    {
        if (_stage is null) return;
        var bounds = new Rect(
            _stage.Bounds.X + slice.X * _zoom,
            _stage.Bounds.Y + slice.Y * _zoom,
            Math.Max(1, slice.Width * _zoom),
            Math.Max(1, slice.Height * _zoom));
        frame.VisualBounds = bounds;
        const float handle = 14;
        foreach (var child in frame.Children)
        {
            var x = bounds.X + bounds.Width / 2 - handle / 2;
            var y = bounds.Y + bounds.Height / 2 - handle / 2;
            if (child.Classes.Contains("nw") || child.Classes.Contains("w") || child.Classes.Contains("sw")) x = bounds.Left - handle / 2;
            if (child.Classes.Contains("ne") || child.Classes.Contains("e") || child.Classes.Contains("se")) x = bounds.Right - handle / 2;
            if (child.Classes.Contains("nw") || child.Classes.Contains("n") || child.Classes.Contains("ne")) y = bounds.Top - handle / 2;
            if (child.Classes.Contains("sw") || child.Classes.Contains("s") || child.Classes.Contains("se")) y = bounds.Bottom - handle / 2;
            if (child.Classes.Contains("pivot-handle"))
            {
                const float pivotSize = 18;
                x = bounds.X + slice.PivotX * bounds.Width - pivotSize / 2;
                y = bounds.Y + (1 - slice.PivotY) * bounds.Height - pivotSize / 2;
                child.VisualBounds = new Rect(x, y, pivotSize, pivotSize);
            }
            else child.VisualBounds = new Rect(x, y, handle, handle);
        }
    }

    private void BeginCreate(Vector2 point) { if(!_sliceTool||_imageWidth==0)return; _creationStart=ToTexture(point); _creationFrame=_ui!.CreateButton("",new Dictionary<string,string>{{"class","preview-frame"}});_previewOverlay!.Add(_creationFrame); }
    private void UpdateCreate(Vector2 point){if(_creationFrame is null)return;var end=ToTexture(point);UpdateCreationFrame(_creationStart,end);}
    private void EndCreate(Vector2 point){if(_creationFrame is null)return;var end=ToTexture(point);var l=(int)MathF.Floor(Math.Min(_creationStart.X,end.X));var t=(int)MathF.Floor(Math.Min(_creationStart.Y,end.Y));var r=(int)MathF.Ceiling(Math.Max(_creationStart.X,end.X));var b=(int)MathF.Ceiling(Math.Max(_creationStart.Y,end.Y));_creationFrame=null;_previewOverlay!.Clear();if(r>l&&b>t)AddSlice(new SpriteSlice{X=l,Y=t,Width=r-l,Height=b-t});}
    private Vector2 ToTexture(Vector2 point){var scroll=_viewport?.ScrollOffset??Vector2.Zero;return new(Math.Clamp((point.X-_stage!.Bounds.X+scroll.X)/_zoom,0,_imageWidth),Math.Clamp((point.Y-_stage.Bounds.Y+scroll.Y)/_zoom,0,_imageHeight));}
    private void UpdateCreationFrame(Vector2 a,Vector2 b){var s=new SpriteSlice{X=(int)Math.Min(a.X,b.X),Y=(int)Math.Min(a.Y,b.Y),Width=Math.Max(1,(int)Math.Abs(a.X-b.X)),Height=Math.Max(1,(int)Math.Abs(a.Y-b.Y))};UpdatePreviewFrame(_creationFrame!,s);}

    private void AddSlice(SpriteSlice slice){EnsureAtlas();var name=UniqueName(Path.GetFileNameWithoutExtension(_working.TexturePath));_working.Edit(a=>a.Sprites[name]=slice);_selection.Clear();_selection.Add(name);RefreshAll();}
    private string UniqueName(string baseName){for(var i=0;;i++){var name=$"{baseName}_{i:000}";if(!_working.Atlas.Sprites.ContainsKey(name))return name;}}
    private void EnsureAtlas(){if(_working.Atlas.FilePath is null){_working.Atlas.FilePath=Path.ChangeExtension(_working.TexturePath,".atlas");_working.Atlas.Texture=Path.GetRelativePath(Path.GetDirectoryName(_working.Atlas.FilePath)!,_working.TexturePath).Replace('\\','/');}}

    private void ShowAuto(bool grid){if(_pixels.Length==0)return;_autoGrid=grid;_autoPanel!.IsVisible=true;Get<UiInputField>("base-name").Text=Path.GetFileNameWithoutExtension(_working.TexturePath);RecomputeAuto();}
    private void RecomputeAuto(){if(_pixels.Length==0)return;int N(string id,int fallback)=>int.TryParse(Get<UiInputField>(id).Text,out var x)?x:fallback;_autoPreview=_autoGrid?AutoSlicer.ByGrid(_imageWidth,_imageHeight,new GridSliceOptions(N("min-width",32),N("min-height",32),0,0,N("merge-distance",0),N("slice-padding",0))):AutoSlicer.ByTransparency(_pixels,_imageWidth,_imageHeight,new AutoSliceOptions((byte)Math.Clamp(N("alpha-threshold",1),0,255),N("min-width",4),N("min-height",4),N("slice-padding",0),N("merge-distance",2)));DrawAutoPreview();SetStatus($"{_autoPreview.Count} slices found");}
    private void DrawAutoPreview(){_previewOverlay!.Clear();foreach(var s in _autoPreview){var f=_ui!.CreatePanel(new Dictionary<string,string>{{"class","preview-frame"}});UpdatePreviewFrame(f,s);_previewOverlay.Add(f);}}
    private void UpdatePreviewFrame(UiElement f,SpriteSlice s){f.Style.Set("left",$"{100f*s.X/_imageWidth:0.####}%");f.Style.Set("top",$"{100f*s.Y/_imageHeight:0.####}%");f.Style.Width=$"{100f*s.Width/_imageWidth:0.####}%";f.Style.Height=$"{100f*s.Height/_imageHeight:0.####}%";}
    private void ApplyAuto(){var baseName=Get<UiInputField>("base-name").Text.Trim();if(baseName.Length==0)baseName=Path.GetFileNameWithoutExtension(_working.TexturePath);EnsureAtlas();_working.Edit(a=>{a.Sprites.Clear();for(var i=0;i<_autoPreview.Count;i++)a.Sprites[$"{baseName}_{i:000}"]=_autoPreview[i];});_selection.Clear();CancelAuto();RefreshAll();}
    private void CancelAuto(){_autoPanel!.IsVisible=false;_previewOverlay!.Clear();_autoPreview=[];}

    private void Save(){if(_working.TexturePath.Length==0)return;EnsureAtlas();repository.Save(_working.Atlas,_working.Atlas.FilePath!);_working.MarkSaved();if(_project is not null){_project=SpriteProject.Open(_project.Root);RebuildAssets();}UpdateDocumentHeader();SetStatus("Saved");}
    private void SaveAs(){if(_working.TexturePath.Length==0)return;var suggested=_working.Atlas.FilePath??Path.ChangeExtension(_working.TexturePath,".atlas");if(dialog.SaveAtlas(suggested) is { } path){if(!path.EndsWith(".atlas",StringComparison.OrdinalIgnoreCase))path+=".atlas";_working.Atlas.FilePath=path;_working.Atlas.Texture=Path.GetRelativePath(Path.GetDirectoryName(path)!,_working.TexturePath).Replace('\\','/');Save();}}
    private void CloseDocument(){if(!ConfirmDiscard())return;_working.Open(new SpriteAtlas(),string.Empty);_pixels=[];_imageWidth=_imageHeight=0;_preview!.Texture=null;_selection.Clear();RefreshAll();}
    private bool ConfirmDiscard(){if(!_working.Dirty)return true;var choice=dialog.ConfirmUnsaved(_working.Atlas.FilePath is null?Path.GetFileName(_working.TexturePath):Path.GetFileName(_working.Atlas.FilePath));if(choice==ProjectFolderDialog.UnsavedChoice.Cancel)return false;if(choice==ProjectFolderDialog.UnsavedChoice.Discard)return true;Save();return !_working.Dirty;}
    private void Undo(){if(_working.Undo()){_selection.RemoveWhere(x=>!_working.Atlas.Sprites.ContainsKey(x));RefreshAll();}}
    private void Redo(){if(_working.Redo()){_selection.RemoveWhere(x=>!_working.Atlas.Sprites.ContainsKey(x));RefreshAll();}}
    private void DeleteSelected(){if(_selection.Count==0)return;_working.Edit(a=>{foreach(var name in _selection)a.Sprites.Remove(name);});_selection.Clear();RefreshAll();}
    private void Duplicate(){if(_selection.Count==0)return;var selected=_selection.ToArray();_working.Edit(a=>{_selection.Clear();foreach(var name in selected){var s=a.Sprites[name];var copy=new SpriteSlice{X=Math.Min(_imageWidth-s.Width,s.X+1),Y=Math.Min(_imageHeight-s.Height,s.Y+1),Width=s.Width,Height=s.Height,PivotX=s.PivotX,PivotY=s.PivotY};var n=UniqueName(name);a.Sprites[n]=copy;_selection.Add(n);}});RefreshAll();}
    private void TrimSelected(){if(_selection.Count==0)return;_working.Edit(a=>{foreach(var name in _selection)if(Trim(a.Sprites[name]) is { } trim)a.Sprites[name]=trim;});RefreshAll();}
    private SpriteSlice? Trim(SpriteSlice s){var l=s.X+s.Width;var t=s.Y+s.Height;var r=-1;var b=-1;for(var y=s.Y;y<s.Y+s.Height;y++)for(var x=s.X;x<s.X+s.Width;x++)if(_pixels[(y*_imageWidth+x)*4+3]>0){l=Math.Min(l,x);t=Math.Min(t,y);r=Math.Max(r,x);b=Math.Max(b,y);}return r<l?null:new SpriteSlice{X=l,Y=t,Width=r-l+1,Height=b-t+1,PivotX=s.PivotX,PivotY=s.PivotY};}
    private void SetPivot(float x,float y){if(_selection.Count==0)return;_working.Edit(a=>{foreach(var n in _selection){a.Sprites[n].PivotX=x;a.Sprites[n].PivotY=y;}});RefreshAll();}

    private void RenameSelected(string value){if(_syncing||PrimaryName() is not { } old)return;var name=value.Trim();if(name.Length==0||name==old||_working.Atlas.Sprites.ContainsKey(name))return;_working.Edit(a=>{var s=a.Sprites[old];a.Sprites.Remove(old);a.Sprites[name]=s;});_selection.Remove(old);_selection.Add(name);RebuildSlices();}
    private void SyncInspector(){_syncing=true;var s=PrimarySlice();_selectionName!.Disabled=s is null;_selectionName.Text=PrimaryName()??"";foreach(var id in new[]{"rect-x","rect-y","rect-w","rect-h","pivot-x","pivot-y"})Get<UiInputField>(id).Disabled=s is null;SyncInspectorValues();_syncing=false;}
    private void SyncInspectorValues(){var s=PrimarySlice();if(s is null)return;SetField("rect-x",s.X);SetField("rect-y",s.Y);SetField("rect-w",s.Width);SetField("rect-h",s.Height);SetField("pivot-x",s.PivotX);SetField("pivot-y",s.PivotY);}
    private void SetField(string id,object value){Get<UiInputField>(id).Text=Convert.ToString(value,CultureInfo.InvariantCulture)??"";}
    private void ApplyInspector(){if(PrimarySlice() is not { } s)return;int I(string id,int fallback)=>int.TryParse(Get<UiInputField>(id).Text,out var x)?x:fallback;float F(string id,float fallback)=>float.TryParse(Get<UiInputField>(id).Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var x)?x:fallback;_working.Edit(_=>{s.X=Math.Clamp(I("rect-x",s.X),0,_imageWidth-1);s.Y=Math.Clamp(I("rect-y",s.Y),0,_imageHeight-1);s.Width=Math.Clamp(I("rect-w",s.Width),1,_imageWidth-s.X);s.Height=Math.Clamp(I("rect-h",s.Height),1,_imageHeight-s.Y);s.PivotX=Math.Clamp(F("pivot-x",s.PivotX),0,1);s.PivotY=Math.Clamp(F("pivot-y",s.PivotY),0,1);});RefreshAll();}

    private void ProcessKeys(){while(_keys.TryDequeue(out var e)){if(!e.IsPressed)continue;var key=(EKeyboardKey)e.Key;var ctrl=(e.Modifiers&KeyModifiers.Primary)!=0;var shift=(e.Modifiers&KeyModifiers.Shift)!=0;if(ui.FocusedElement is UiInputField){if(ctrl&&key==EKeyboardKey.S)Save();continue;}if(ctrl&&key==EKeyboardKey.S){if(shift)SaveAs();else Save();}else if(ctrl&&key==EKeyboardKey.O)OpenFile();else if(ctrl&&key==EKeyboardKey.Z)Undo();else if(ctrl&&key==EKeyboardKey.Y)Redo();else if(ctrl&&key==EKeyboardKey.D)Duplicate();else if(ctrl&&key==EKeyboardKey.A){_selection.Clear();foreach(var name in _working.Atlas.Sprites.Keys)_selection.Add(name);RebuildSlices();SyncInspector();}else if(ctrl&&key==EKeyboardKey.Number0)SetZoom(1);else if(key==EKeyboardKey.Delete)DeleteSelected();else if(key==EKeyboardKey.Escape)SetTool(false);else if(key==EKeyboardKey.V)SetTool(false);else if(key==EKeyboardKey.S)SetTool(true);else if(key==EKeyboardKey.F)Fit();else if(key is EKeyboardKey.Equal or EKeyboardKey.KeypadAdd)SetZoomAt(_zoom*2,input.PointerPosition);else if(key is EKeyboardKey.Minus or EKeyboardKey.KeypadSubtract)SetZoomAt(_zoom*.5f,input.PointerPosition);else if(key is EKeyboardKey.Left or EKeyboardKey.Right or EKeyboardKey.Up or EKeyboardKey.Down)Nudge(key,shift?10:1);}}
    private void Nudge(EKeyboardKey key,int amount){if(_selection.Count==0)return;_working.Edit(a=>{foreach(var n in _selection){var s=a.Sprites[n];if(key==EKeyboardKey.Left)s.X=Math.Max(0,s.X-amount);if(key==EKeyboardKey.Right)s.X=Math.Min(_imageWidth-s.Width,s.X+amount);if(key==EKeyboardKey.Up)s.Y=Math.Max(0,s.Y-amount);if(key==EKeyboardKey.Down)s.Y=Math.Min(_imageHeight-s.Height,s.Y+amount);}});RefreshAll();}
    private void SetTool(bool slice){_sliceTool=slice;Get<UiButton>("select-tool").ToggleClass("active",!slice);Get<UiButton>("slice-tool").ToggleClass("active",slice);}
    private void SetZoom(float value){_zoom=Math.Clamp(value,.25f,8);UpdateStage();RebuildSlices();}
    private void SetZoomAt(float value,Vector2 pointer){var old=_zoom;var texture=ToTexture(pointer);SetZoom(value);if(_viewport is not null)_viewport.ScrollBy(texture*(_zoom-old));}
    private void Fit(){if(_viewport is null||_imageWidth==0)return;var w=Math.Max(100,_viewport.Bounds.Width-80);var h=Math.Max(100,_viewport.Bounds.Height-80);SetZoom(Math.Clamp(Math.Min(w/_imageWidth,h/_imageHeight),.25f,8));}
    private void ToggleGrid(){_grid=!_grid;Get<UiPanel>("grid-layer").IsVisible=_grid;Get<UiButton>("toggle-grid").ToggleClass("active",_grid);RebuildGrid();}
    private void ToggleChecker(){_checkerboard=!_checkerboard;_canvasContent!.ToggleClass("checkerboard",_checkerboard);}
    private void ToggleSnap(){_snap=!_snap;Get<UiButton>("toggle-snap").ToggleClass("active",_snap);}
    private void ToggleAssets(){_assetsCollapsed=!_assetsCollapsed;var sidebar=_assetList!.Parent!;sidebar.Style.Width=_assetsCollapsed?"46ui":"250ui";_assetList.IsVisible=!_assetsCollapsed;_projectLabel!.IsVisible=!_assetsCollapsed;Get<UiButton>("collapse-assets").Text=_assetsCollapsed?"›":"‹";}
    private void SetStatus(string text,bool error=false){_status!.Value=text;_status.ToggleClass("error",error);}
    private T Get<T>(string id)where T:UiElement=>_ui!.GetElementById<T>(id); private void Click(string id,Action action)=>Get<UiButton>(id).Clicked+=_=>{HideMenus();action();}; private void HideMenus(){foreach(var id in new[]{"file-menu","edit-menu","view-menu","slice-menu"})Get<UiPanel>(id).IsVisible=false;} private void Instantiate(UiElement parent,string path)=>_ui!.Instantiate(path,parent);
    public void Unbind(){_ui=null;_stage=null;_overlay=null;_previewOverlay=null;_viewport=null;_assetList=null;_sliceList=null;}
    public void Dispose(){window.KeyChanged-=OnKey;}
    private void OnKey(IWindow.KeyEvent e)=>_keys.Enqueue(e);
    public void Start(){window.KeyChanged+=OnKey;}
    private enum ResizeMode{None,Move,Left,Right,Top,Bottom,TopLeft,TopRight,BottomLeft,BottomRight,Pivot}
}
