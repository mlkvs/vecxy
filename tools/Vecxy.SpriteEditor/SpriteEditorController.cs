using StbImageSharp;
using Vecxy.Assets;
using Vecxy.Input;
using Vecxy.Rendering;
using Vecxy.UI;

namespace Vecxy.SpriteEditor;

public sealed class SpriteEditorController(AtlasRepository repository, ProjectFolderDialog dialog, ITextureResolver textures, IInputManager input)
{
    private UiDocument? _document;
    private SpriteProject? _project;
    private SpriteAtlas? _atlas;
    private string? _imagePath;
    private int _imageWidth;
    private int _imageHeight;
    private UiImage? _preview;
    private UiPanel? _overlay;
    private UiPanel? _stage;
    private UiPanel? _assetList;
    private UiPanel? _sliceList;
    private UiText? _status;
    private UiText? _projectLabel;
    private string? _selectedSlice;
    private string? _draggingSlice;
    private UiButton? _selectedFrame;

    public void Bind(UiDocument document)
    {
        _document = document;
        var workspace = document.GetElementById<UiPanel>("workspace");
        Instantiate(document, workspace, "Components/TopBar.xml");
        var content = document.GetElementById<UiPanel>("content");
        Instantiate(document, content, "Components/AssetBrowser.xml");
        Instantiate(document, content, "Components/Canvas.xml");
        Instantiate(document, content, "Components/Inspector.xml");
        Instantiate(document, workspace, "Components/StatusBar.xml");
        _assetList = document.GetElementById<UiPanel>("asset-list");
        _sliceList = document.GetElementById<UiPanel>("slice-list");
        _preview = document.GetElementById<UiImage>("sprite-preview");
        _overlay = document.GetElementById<UiPanel>("slice-overlay");
        _stage = document.GetElementById<UiPanel>("image-stage");
        _status = document.GetElementById<UiText>("status-text");
        _projectLabel = document.GetElementById<UiText>("project-label");
        Click("open-project", OpenProject);
        Click("new-atlas", NewAtlas);
        Click("save-atlas", SaveAtlas);
        Click("add-slice", AddSlice);
        Click("auto-slice", AutoSlice);
        Click("delete-slice", DeleteSlice);
        Click("pivot-center", () => SetPivot(.5f, .5f));
        Click("pivot-bottom", () => SetPivot(.5f, 0));
        BindNudge("x-minus", -1, 0, 0, 0); BindNudge("x-plus", 1, 0, 0, 0);
        BindNudge("y-minus", 0, -1, 0, 0); BindNudge("y-plus", 0, 1, 0, 0);
        BindNudge("w-minus", 0, 0, -1, 0); BindNudge("w-plus", 0, 0, 1, 0);
        BindNudge("h-minus", 0, 0, 0, -1); BindNudge("h-plus", 0, 0, 0, 1);
        var argument = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(Directory.Exists);
        if (argument is not null) OpenProject(argument);
    }

    private static void Instantiate(UiDocument document, UiElement parent, string path) => document.Instantiate(path, parent);
    private void Click(string id, Action action) => _document!.GetElementById<UiButton>(id).Clicked += _ => action();
    private void BindNudge(string id, int x, int y, int w, int h) => Click(id, () => Nudge(x, y, w, h));

    private void OpenProject()
    {
        var path = dialog.Open();
        if (path is not null) OpenProject(path);
    }

    private void OpenProject(string path)
    {
        try
        {
            _project = SpriteProject.Open(path);
            _projectLabel!.Value = _project.Root;
            RebuildAssets();
            SetStatus($"{_project.Images.Count} images · {_project.Atlases.Count} atlases");
        }
        catch (Exception exception) { SetStatus(exception.Message, true); }
    }

    private void RebuildAssets()
    {
        _assetList!.Clear();
        foreach (var path in _project!.Images) AddAssetButton(path, "IMG", () => SelectImage(path));
        foreach (var path in _project.Atlases) AddAssetButton(path, "ATL", () => OpenAtlas(path));
    }

    private void AddAssetButton(string path, string kind, Action action)
    {
        var button = _document!.CreateButton($"{kind}   {_project!.Relative(path)}", new Dictionary<string, string> { ["class"] = "asset-row" });
        button.Clicked += _ => action();
        _assetList!.Add(button);
    }

    private void SelectImage(string path)
    {
        using var stream = File.OpenRead(path);
        var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        _imagePath = path; _imageWidth = image.Width; _imageHeight = image.Height;
        _preview!.Texture = textures.Resolve(TextureAsset.FromRgba(image.Width, image.Height, image.Data));
        var available = 650f;
        var scale = Math.Min(available / image.Width, available / image.Height);
        _stage!.Style.Width = $"{Math.Max(1, image.Width * scale):0.##}ui";
        _stage.Style.Height = $"{Math.Max(1, image.Height * scale):0.##}ui";
        _document!.GetElementById<UiText>("canvas-title").Value = _project!.Relative(path);
        if (_atlas is null || !Path.GetFullPath(Path.Combine(Path.GetDirectoryName(_atlas.FilePath) ?? _project.AssetsDirectory, _atlas.Texture)).Equals(path))
            _atlas = new SpriteAtlas { Texture = Path.GetFileName(path) };
        RebuildSlices();
        SetStatus($"{image.Width} × {image.Height}px");
    }

    private void OpenAtlas(string path)
    {
        try
        {
            _atlas = repository.Load(path);
            var texture = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, _atlas.Texture));
            SelectImage(texture);
            _atlas.FilePath = path;
            RebuildSlices();
            SetStatus($"Opened {_project!.Relative(path)}");
        }
        catch (Exception exception) { SetStatus(exception.Message, true); }
    }

    private void NewAtlas()
    {
        if (_imagePath is null) { SetStatus("Select an image first", true); return; }
        _atlas = new SpriteAtlas { Texture = Path.GetFileName(_imagePath), FilePath = Path.ChangeExtension(_imagePath, ".atlas") };
        AddSlice();
    }

    private void AddSlice()
    {
        if (_atlas is null || _imagePath is null) { SetStatus("Select an image first", true); return; }
        var baseName = Path.GetFileNameWithoutExtension(_imagePath);
        var index = _atlas.Sprites.Count + 1;
        var name = baseName;
        while (_atlas.Sprites.ContainsKey(name)) name = $"{baseName}_{index++}";
        _atlas.Sprites[name] = new SpriteSlice { Width = _imageWidth, Height = _imageHeight };
        _selectedSlice = name;
        RebuildSlices();
    }

    private void AutoSlice()
    {
        if (_atlas is null || _imagePath is null) { SetStatus("Select an image first", true); return; }
        using var stream = File.OpenRead(_imagePath);
        var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        var bounds = AlphaBounds(image.Data, image.Width, image.Height);
        if (bounds is null) { SetStatus("Image is fully transparent", true); return; }
        _atlas.Sprites.Clear();
        _atlas.Sprites[Path.GetFileNameWithoutExtension(_imagePath)] = bounds;
        _selectedSlice = _atlas.Sprites.Keys.Single();
        RebuildSlices();
        SetStatus("Transparent margins trimmed");
    }

    private static SpriteSlice? AlphaBounds(byte[] pixels, int width, int height)
    {
        var left = width; var top = height; var right = -1; var bottom = -1;
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
            if (pixels[(y * width + x) * 4 + 3] > 0) { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y); }
        return right < left ? null : new SpriteSlice { X = left, Y = top, Width = right - left + 1, Height = bottom - top + 1 };
    }

    private void DeleteSlice()
    {
        if (_selectedSlice is not null && _atlas?.Sprites.Remove(_selectedSlice) == true) { _selectedSlice = null; RebuildSlices(); }
    }

    private void Nudge(int x, int y, int width, int height)
    {
        if (Selected() is not { } slice) return;
        slice.X = Math.Clamp(slice.X + x, 0, Math.Max(0, _imageWidth - 1));
        slice.Y = Math.Clamp(slice.Y + y, 0, Math.Max(0, _imageHeight - 1));
        slice.Width = Math.Clamp(slice.Width + width, 1, Math.Max(1, _imageWidth - slice.X));
        slice.Height = Math.Clamp(slice.Height + height, 1, Math.Max(1, _imageHeight - slice.Y));
        RebuildSlices();
    }

    private void SetPivot(float x, float y) { if (Selected() is { } slice) { slice.PivotX = x; slice.PivotY = y; RebuildSlices(); } }
    private SpriteSlice? Selected() => _selectedSlice is not null && _atlas?.Sprites.TryGetValue(_selectedSlice, out var value) == true ? value : null;

    private void RebuildSlices()
    {
        _sliceList!.Clear(); _overlay!.Clear();
        if (_atlas is null) return;
        foreach (var (name, slice) in _atlas.Sprites)
        {
            var row = _document!.CreateButton(name, new Dictionary<string, string> { ["class"] = name == _selectedSlice ? "slice-row selected" : "slice-row" });
            row.Clicked += _ => { _selectedSlice = name; RebuildSlices(); };
            _sliceList.Add(row);
            var frame = _document.CreateButton("", new Dictionary<string, string> { ["class"] = name == _selectedSlice ? "slice-frame selected" : "slice-frame", ["draggable"] = "true" });
            frame.Style.Set("left", $"{100f * slice.X / Math.Max(1, _imageWidth):0.###}%");
            frame.Style.Set("top", $"{100f * slice.Y / Math.Max(1, _imageHeight):0.###}%");
            frame.Style.Width = $"{100f * slice.Width / Math.Max(1, _imageWidth):0.###}%";
            frame.Style.Height = $"{100f * slice.Height / Math.Max(1, _imageHeight):0.###}%";
            frame.Clicked += _ => { _selectedSlice = name; RebuildSlices(); };
            frame.DragStarted += _ => { _selectedSlice = name; _draggingSlice = name; _selectedFrame = frame; };
            frame.DragEnded += _ => { _draggingSlice = null; _selectedFrame = null; RebuildSlices(); };
            _overlay.Add(frame);
        }
        var selected = Selected();
        _document!.GetElementById<UiText>("selection-name").Value = _selectedSlice ?? "No slice selected";
        _document.GetElementById<UiText>("rect-value").Value = selected is null ? "—" : $"{selected.X}, {selected.Y}  ·  {selected.Width} × {selected.Height}";
        _document.GetElementById<UiText>("pivot-value").Value = selected is null ? "—" : $"{selected.PivotX:0.##}, {selected.PivotY:0.##}";
    }

    private void SaveAtlas()
    {
        if (_atlas is null || _project is null) { SetStatus("Nothing to save", true); return; }
        var path = _atlas.FilePath ?? Path.Combine(_project.AssetsDirectory, "Textures", "Sprites.atlas");
        repository.Save(_atlas, path); RebuildAssets(); SetStatus($"Saved {_project.Relative(path)}");
    }

    private void SetStatus(string value, bool error = false) { if (_status is null) return; _status.Value = value; _status.ToggleClass("error", error); }
    public void Update()
    {
        if (_draggingSlice is null || _stage is null || _selectedFrame is null || Selected() is not { } slice) return;
        var bounds = _stage.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        slice.X = Math.Clamp(slice.X + (int)MathF.Round(input.PointerDelta.X * _imageWidth / bounds.Width), 0, Math.Max(0, _imageWidth - slice.Width));
        slice.Y = Math.Clamp(slice.Y + (int)MathF.Round(input.PointerDelta.Y * _imageHeight / bounds.Height), 0, Math.Max(0, _imageHeight - slice.Height));
        _selectedFrame.Style.Set("left", $"{100f * slice.X / Math.Max(1, _imageWidth):0.###}%");
        _selectedFrame.Style.Set("top", $"{100f * slice.Y / Math.Max(1, _imageHeight):0.###}%");
        _document!.GetElementById<UiText>("rect-value").Value = $"{slice.X}, {slice.Y}  ·  {slice.Width} × {slice.Height}";
    }

    public void Unbind() { _document = null; _preview = null; _overlay = null; _stage = null; _assetList = null; _sliceList = null; }
}
