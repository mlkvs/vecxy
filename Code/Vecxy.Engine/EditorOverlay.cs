using System.Globalization;
using System.Security;
using System.Text;
using Vecxy.Assets;
using Vecxy.Engine.Objects;
using Vecxy.Engine.Scenes;
using Vecxy.Rendering;
using Vecxy.UI;

namespace Vecxy.Engine;

public sealed class EditorLayer : AppLayer
{
    public Window Window { get; set; } = null!;
    public RenderingModule Rendering { get; set; } = null!;
    public SceneManager Scenes { get; set; } = null!;
    public AssetsManager Assets { get; set; } = null!;
    private SceneObject? _lastSelection;
    private readonly Dictionary<string, UiFloatingWindow> _floating = [];
    private int _leftWidth = 240;
    private int _rightWidth = 300;
    private int _bottomHeight = 200;
    private string _aspectPreset = "16:9";
    public bool IsActive { get; private set; }

    public override void OnTick(float dt)
    {
        foreach (var floating in _floating.Values.ToArray()) floating.Pump(dt);
        var closed = _floating.Where(x => !x.Value.IsOpen).Select(x => x.Key).ToArray();
        foreach (var key in closed) { _floating[key].Dispose(); _floating.Remove(key); BuildDocument(); }
        if (Window.ConsumeF12Pressed()) SetActive(!IsActive);
        if (!IsActive) return;
        ResizeGameScreen();
        if (!ReferenceEquals(_lastSelection, Scenes.SelectedObject)) BuildDocument();
        UpdateInspector();
    }

    public override void OnUnload()
    {
        foreach (var floating in _floating.Values) floating.Dispose();
        _floating.Clear();
        Rendering.EditorUI.Clear();
    }

    private void SetActive(bool active)
    {
        IsActive = active;
        if (!active)
        {
            foreach (var floating in _floating.Values) floating.Dispose();
            _floating.Clear();
            Rendering.EditorUI.Clear();
            Rendering.GameScreen.FillWindow(Window.Size.X, Window.Size.Y);
            return;
        }
        ResizeGameScreen();
        BuildDocument();
    }

    private void ResizeGameScreen()
    {
        var available = new ScreenRect(_leftWidth, 42, Math.Max(200, Window.Size.X - _leftWidth - _rightWidth),
            Math.Max(150, Window.Size.Y - 42 - _bottomHeight));
        Rendering.GameScreen.FitInside(available, _aspectPreset switch
        {
            "4:3" => 4f / 3f, "16:10" => 16f / 10f, "21:9" => 21f / 9f, "9:16" => 9f / 16f, _ => 16f / 9f
        });
    }

    private void BuildDocument()
    {
        _lastSelection = Scenes.SelectedObject;
        var hierarchy = new StringBuilder();
        var objects = Scenes.ActiveScene?.Objects.ToArray() ?? [];
        for (var i = 0; i < objects.Length; i++)
            hierarchy.Append($"<Button id=\"object-{i}\" class=\"tree-item\" text=\"{Escape(objects[i].Name)}\" />");
        var assets = new StringBuilder();
        foreach (var path in Assets.EnumeratePaths())
            assets.Append($"<Button class=\"asset-item\" text=\"{Escape(path)}\" draggable=\"true\" />");
        var scripts = _lastSelection?.Scripts.Count > 0
            ? string.Join("", _lastSelection.Scripts.Select(x => $"<Label class=\"script\" text=\"{Escape(x.GetType().Name)}\" />"))
            : "<Label text=\"NO SCRIPTS\" />";
        var hierarchyPanel = _floating.ContainsKey("hierarchy") ? "" : $"<Panel id=\"hierarchy\" class=\"hierarchy\"><Panel class=\"panel-header\"><Label class=\"panel-title\" text=\"SCENE HIERARCHY\" /><Button id=\"detach-hierarchy\" class=\"detach\"><Icon name=\"external-link\" /></Button></Panel><ScrollView class=\"tree\">{hierarchy}</ScrollView></Panel>";
        var inspectorPanel = _floating.ContainsKey("inspector") ? "" : $"<Panel id=\"inspector\" class=\"inspector\"><Panel class=\"panel-header\"><Label class=\"panel-title\" text=\"INSPECTOR\" /><Button id=\"detach-inspector\" class=\"detach\"><Icon name=\"external-link\" /></Button></Panel><Label id=\"object-name\" class=\"object-name\" /><Label class=\"section\" text=\"TRANSFORM\" /><Label id=\"position\" /><Label id=\"rotation\" /><Label id=\"scale\" /><Label class=\"section\" text=\"SCRIPTS\" />{scripts}</Panel>";
        var assetsPanel = _floating.ContainsKey("assets") ? "" : $"<Panel id=\"assets\" class=\"assets\"><Panel class=\"panel-header\"><Label class=\"panel-title\" text=\"ASSETS\" /><Button id=\"detach-assets\" class=\"detach\"><Icon name=\"external-link\" /></Button></Panel><ScrollView class=\"asset-list\">{assets}</ScrollView></Panel>";
        var uxml = $"""
            <UI>
              <Panel class="toolbar"><Label class="brand" text="VECXY EDITOR" /><Button class="tool-icon"><Icon name="play" /></Button><Button class="tool-icon"><Icon name="pause" /></Button><Label text="ASPECT" /><Dropdown id="aspect" text="{_aspectPreset}" options="16:9|16:10|4:3|21:9|9:16" /><Label class="hint" text="F12 CLOSE" /></Panel>
              {hierarchyPanel}{inspectorPanel}
              <Panel id="game-frame" class="game-frame" picking-mode="ignore"><Label text="GAME" /></Panel>
              {assetsPanel}
              <Panel id="left-split" class="left-split" draggable="true" drag-visual="false" />
              <Panel id="right-split" class="right-split" draggable="true" drag-visual="false" />
              <Panel id="bottom-split" class="bottom-split" draggable="true" drag-visual="false" />
            </UI>
            """;
        Rendering.EditorUI.Load(uxml, BuildCss());
        for (var i = 0; i < objects.Length; i++)
        {
            var captured = objects[i];
            var button = Rendering.EditorUI.Document?.Find($"object-{i}");
            if (button is not null) button.Clicked += _ => Scenes.Select(captured);
        }
        Bind("detach-hierarchy", _ => Detach("hierarchy", "Scene Hierarchy", $"<ScrollView class=\"tree\">{hierarchy}</ScrollView>"));
        Bind("detach-inspector", _ => Detach("inspector", "Inspector", $"<Label id=\"object-name\" class=\"object-name\" /><Label class=\"section\" text=\"TRANSFORM\" /><Label id=\"position\" /><Label id=\"rotation\" /><Label id=\"scale\" /><Label class=\"section\" text=\"SCRIPTS\" />{scripts}"));
        Bind("detach-assets", _ => Detach("assets", "Assets", $"<ScrollView class=\"asset-list\">{assets}</ScrollView>"));
        Bind("aspect", element => { _aspectPreset = element.Text; ResizeGameScreen(); });
        BindDrag("left-split", delta => { _leftWidth = Math.Clamp(_leftWidth + (int)delta.X, 150, 500); ApplyDockLayout(); });
        BindDrag("right-split", delta => { _rightWidth = Math.Clamp(_rightWidth - (int)delta.X, 180, 550); ApplyDockLayout(); });
        BindDrag("bottom-split", delta => { _bottomHeight = Math.Clamp(_bottomHeight - (int)delta.Y, 100, 450); ApplyDockLayout(); });
        UpdateInspector();
    }

    private void Bind(string id, Action<UiElement> action) { var e = Rendering.EditorUI.Document?.Find(id); if (e is not null) e.Clicked += action; }
    private void BindDrag(string id, Action<System.Numerics.Vector2> action)
    {
        var element = Rendering.EditorUI.Document?.Find(id); if (element is null) return;
        var left = _leftWidth; var right = _rightWidth; var bottom = _bottomHeight;
        element.Dragged += (_, delta) =>
        {
            if (id == "left-split") _leftWidth = Math.Clamp(left + (int)delta.X, 150, 500);
            else if (id == "right-split") _rightWidth = Math.Clamp(right - (int)delta.X, 180, 550);
            else _bottomHeight = Math.Clamp(bottom - (int)delta.Y, 100, 450);
            ApplyDockLayout();
        };
    }

    private void ApplyDockLayout()
    {
        SetStyle("hierarchy", s => { s.Width = new(UiUnit.Pixel, _leftWidth); s.Bottom = new(UiUnit.Pixel, _bottomHeight); });
        SetStyle("inspector", s => { s.Width = new(UiUnit.Pixel, _rightWidth); s.Bottom = new(UiUnit.Pixel, _bottomHeight); });
        SetStyle("assets", s => s.Height = new(UiUnit.Pixel, _bottomHeight));
        SetStyle("game-frame", s => { s.Left = new(UiUnit.Pixel, _leftWidth); s.Right = new(UiUnit.Pixel, _rightWidth); s.Bottom = new(UiUnit.Pixel, _bottomHeight); });
        SetStyle("left-split", s => { s.Left = new(UiUnit.Pixel, _leftWidth - 3); s.Bottom = new(UiUnit.Pixel, _bottomHeight); });
        SetStyle("right-split", s => { s.Right = new(UiUnit.Pixel, _rightWidth - 3); s.Bottom = new(UiUnit.Pixel, _bottomHeight); });
        SetStyle("bottom-split", s => s.Bottom = new(UiUnit.Pixel, _bottomHeight - 3));
        ResizeGameScreen();
    }
    private void SetStyle(string id, Action<UiStyle> change) => Rendering.EditorUI.Document?.Find(id)?.ModifyStyle(change);

    private void Detach(string key, string title, string content)
    {
        if (_floating.ContainsKey(key)) return;
        try
        {
            var floating = new UiFloatingWindow(title, 420, 520, Window,
                $"<UI><Panel class=\"floating\"><Panel class=\"panel-header\"><Label class=\"panel-title\" text=\"{title.ToUpperInvariant()}\" /></Panel>{content}</Panel></UI>", BuildCss());
            _floating[key] = floating;
            if (key == "hierarchy") BindHierarchy(floating.Document);
        }
        catch (Exception exception)
        {
            Vecxy.Diagnostics.Logger.Error(exception, $"Unable to detach editor panel '{key}'.");
            Window.MakeCurrent();
            return;
        }
        Window.MakeCurrent();
        BuildDocument();
    }

    private void BindHierarchy(UiDocument? document)
    {
        var objects = Scenes.ActiveScene?.Objects.ToArray() ?? [];
        for (var i = 0; i < objects.Length; i++)
        {
            var captured = objects[i];
            var button = document?.Find($"object-{i}");
            if (button is not null) button.Clicked += _ => Scenes.Select(captured);
        }
    }

    private void UpdateInspector()
    {
        var selected = Scenes.SelectedObject;
        SetText("object-name", selected?.Name ?? "NOTHING SELECTED");
        SetText("position", selected is null ? "POSITION  -" : $"POSITION  {Vector(selected.Transform.Position)}");
        SetText("rotation", selected is null ? "ROTATION  -" : $"ROTATION  {Vector(selected.Transform.Rotation.X, selected.Transform.Rotation.Y, selected.Transform.Rotation.Z)}");
        SetText("scale", selected is null ? "SCALE     -" : $"SCALE     {Vector(selected.Transform.Scale)}");
        if (_floating.TryGetValue("inspector", out var inspector))
        {
            SetText(inspector.Document, "object-name", selected?.Name ?? "NOTHING SELECTED");
            SetText(inspector.Document, "position", selected is null ? "POSITION  -" : $"POSITION  {Vector(selected.Transform.Position)}");
            SetText(inspector.Document, "rotation", selected is null ? "ROTATION  -" : $"ROTATION  {Vector(selected.Transform.Rotation.X, selected.Transform.Rotation.Y, selected.Transform.Rotation.Z)}");
            SetText(inspector.Document, "scale", selected is null ? "SCALE     -" : $"SCALE     {Vector(selected.Transform.Scale)}");
        }
    }

    private void SetText(string id, string text) { var element = Rendering.EditorUI.Document?.Find(id); if (element is not null) element.Text = text; }
    private static void SetText(UiDocument? document, string id, string text) { var element = document?.Find(id); if (element is not null) element.Text = text; }
    private static string Vector(System.Numerics.Vector3 value) => Vector(value.X, value.Y, value.Z);
    private static string Vector(float x, float y, float z) => string.Create(CultureInfo.InvariantCulture, $"{x:0.00}, {y:0.00}, {z:0.00}");
    private static string Escape(string value) => SecurityElement.Escape(value) ?? string.Empty;

    private string BuildCss() => $$"""
        UI { width: 100%; height: 100%; position: relative; color: #D7DEE8FF; }
        Panel { background-color: #171B22FF; border-color: #343B47FF; border-width: 1px; }
        Label { height: 18px; color: #C9D1DCFF; font-size: 11px; }
        Button { height: 26px; padding: 5px 8px; background-color: #252B35FF; border-color: #414A58FF; border-width: 1px; color: #DCE5F0FF; }
        Button:hover { background-color: #344052FF; }
        Button:active { background-color: #151A21FF; }
        .toolbar { position: absolute; left: 0; top: 0; width: 100%; height: 42px; padding: 7px 10px; gap: 8px; flex-direction: row; align-items: center; background-color: #11141AFF; }
        .toolbar Button { width: 72px; }
        .brand { width: 150px; color: #70B7FFFF; font-size: 13px; }
        .hint { width: 90px; margin-left: 12px; color: #8290A2FF; }
        Dropdown { width: 82px; height: 26px; padding: 5px; background-color: #252B35FF; border-color: #4A5666FF; border-width: 1px; color: #FFFFFFFF; }
        .hierarchy { position: absolute; left: 0; top: 42px; width: {{_leftWidth}}px; bottom: {{_bottomHeight}}px; padding: 9px; gap: 7px; }
        .panel-title { height: 22px; color: #78BFFFFF; font-size: 12px; }
        .tree { width: 100%; flex-grow: 1; gap: 2px; }
        .tree-item { width: 100%; height: 25px; border-width: 0px; }
        .inspector { position: absolute; right: 0; top: 42px; width: {{_rightWidth}}px; bottom: {{_bottomHeight}}px; padding: 12px; gap: 7px; }
        .object-name { height: 28px; color: #FFFFFFFF; font-size: 14px; }
        .section { margin-top: 10px; color: #70B7FFFF; }
        .script { height: 26px; padding: 5px; background-color: #232A34FF; border-color: #3B4553FF; border-width: 1px; }
        .game-frame { position: absolute; left: {{_leftWidth}}px; top: 42px; right: {{_rightWidth}}px; bottom: {{_bottomHeight}}px; background-color: #00000000; border-color: #556273FF; border-width: 1px; }
        .game-frame Label { width: 55px; padding: 3px 6px; background-color: #111722DD; color: #77BDFFFF; }
        .assets { position: absolute; left: 0; right: 0; bottom: 0; height: {{_bottomHeight}}px; padding: 9px; gap: 6px; }
        .asset-list { width: 100%; flex-grow: 1; gap: 3px; }
        .asset-item { width: 100%; height: 24px; border-width: 0px; color: #B9C5D3FF; }
        .panel-header { width: 100%; height: 28px; flex-direction: row; align-items: center; background-color: #11151CFF; }
        .panel-header .panel-title { flex-grow: 1; }
        .detach { width: 42px; height: 22px; padding: 3px; font-size: 9px; }
        .detach Icon { icon-size: 14px; color: #B9D9F7FF; }
        .tool-icon { width: 30px; height: 28px; padding: 5px; align-items: center; justify-content: center; }
        .tool-icon Icon { icon-size: 16px; color: #DCEBFAFF; }
        .left-split { position: absolute; left: {{_leftWidth - 3}}px; top: 42px; bottom: {{_bottomHeight}}px; width: 6px; background-color: #4B5868AA; }
        .right-split { position: absolute; right: {{_rightWidth - 3}}px; top: 42px; bottom: {{_bottomHeight}}px; width: 6px; background-color: #4B5868AA; }
        .bottom-split { position: absolute; left: 0; right: 0; bottom: {{_bottomHeight - 3}}px; height: 6px; background-color: #4B5868AA; }
        .left-split:hover, .right-split:hover, .bottom-split:hover { background-color: #70B7FFFF; }
        .floating { width: 100%; height: 100%; padding: 10px; gap: 8px; }
        """;
}
