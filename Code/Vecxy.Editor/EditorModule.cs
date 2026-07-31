using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using Autofac;
using ImGuiNET;
using Vecxy.Assets;
using Vecxy.Diagnostics;
using Vecxy.Diagnostics.Console;
using Vecxy.Input;
using Vecxy.Kernel;
using Vecxy.Physics;
using Vecxy.Rendering;
using Vecxy.Scene;

namespace Vecxy.Editor;

public sealed class EditorModule(
    IWindow window,
    IRenderer renderer,
    IAssetsManager assets,
    IMeshResolver meshResolver,
    IRenderOverlayStage overlays,
    ISceneManager scenes,
    IConfigProvider configs,
    IInputManager input,
    ImGuiRenderer imgui,
    GizmoRenderer gizmos,
    DebugConsolePanel debugConsolePanel,
    IDebugConsole debugConsole) :
    IModule,
    IModule.IUpdatable,
    IEditorGui,
    IEditorGizmos
{
    public sealed class Definition :
        AModuleDefinition<EditorModule>
    {
        protected override IReadOnlyList<Type> Exports =>
            [typeof(IEditorGui), typeof(IEditorGizmos)];

        protected override void RegisterModule(ContainerBuilder builder)
        {
            builder
                .RegisterType<EditorModule>()
                .AsSelf()
                .SingleInstance();

            builder
                .RegisterType<ImGuiRenderer>()
                .AsSelf()
                .SingleInstance();

            builder
                .RegisterType<GizmoRenderer>()
                .AsSelf()
                .SingleInstance();

            builder
                .RegisterType<DebugConsolePanel>()
                .AsSelf()
                .SingleInstance();

            builder
                .RegisterType<SystemFileDialog>()
                .As<ISystemFileDialog>()
                .SingleInstance();
        }
    }

    private readonly List<WindowEntry> _windowCallbacks = [];
    private readonly List<Action<IEditorGizmoDrawer>> _gizmoCallbacks = [];
    private readonly List<Action> _pendingEditorActions = [];
    private AssetRef<InputAsset>? _editorInputAsset;
    private InputMap? _editorInputMap;
    private ConfigRef<EditorLayoutConfig>? _editorLayoutRef;
    private EditorLayoutConfig _editorLayout =
        EditorLayoutConfig.CreateDefault();
    private bool _initialized;
    private bool _overlayVisible = true;
    private bool _dockLayoutDirty;
    private int _lastWindowWidth;
    private int _lastWindowHeight;
    private SceneObject? _selectedSceneObject;
    private Vecxy.Scene.SceneInstance? _selectedScene;
    private IConfigRef? _selectedConfig;
    private int _selectedConfigVersion = -1;
    private object? _selectedConfigValue;
    private bool _showStatisticsWindow;
    private bool _showGameViewWindow;
    private bool _showHierarchyWindow;
    private bool _showInspectorWindow;
    private bool _showConfigsWindow;
    private bool _showRenderSettingsWindow;
    private bool _gizmosEnabled = true;
    private bool _gameViewHovered;
    private bool _gameViewPresetPopupOpen;
    private bool _focusGameViewRequested;
    private Vector2 _gameViewScreenPosition;
    private Vector2 _gameViewScreenSize;
    private bool _wasConsoleOpen;
    private bool _consoleOpenBeforeOverlay;
    private bool _cursorCapturedBeforeConsoleOpen = true;
    private int _selectedGameViewPreset = 1;
    private string _componentSearch = string.Empty;
    private readonly Dictionary<string, AssetRef<Model>> _editorModelRefs =
        new(StringComparer.Ordinal);
    private static readonly string[] ModelExtensions = [".glb", ".gltf"];
    private static readonly string[] MaterialExtensions = [".material"];
    private static readonly string[] TextureExtensions =
        [".png", ".jpg", ".jpeg", ".bmp", ".tga", ".gif", ".webp"];
    private static readonly GameViewPreset[] GameViewPresets =
    [
        new("FreeAspect", 0, 0),
        new("1920x1080", 1920, 1080),
        new("1600x900", 1600, 900),
        new("1280x720", 1280, 720),
        new("1024x768", 1024, 768),
        new("800x600", 800, 600),
        new("375x812", 375, 812)
    ];

    public EGizmoDisplayMode GizmoDisplayMode
    {
        get => gizmos.DisplayMode;
        set => gizmos.DisplayMode = value;
    }

    public void OnInitialize()
    {
        if (_initialized)
            return;

        InitializeEditorLayout();
        RequestLayoutReset();
        imgui.Initialize();
        gizmos.Initialize();
        _editorInputAsset = assets.Load<InputAsset>("Controls.input");
        _editorInputMap = input.Create(_editorInputAsset, "Engine");
        _editorInputMap.GetAction("ToggleConsole").Started += OnToggleConsoleStarted;
        _editorInputMap.Enable();
        window.Resized += OnWindowResized;
        window.KeyChanged += OnKeyChanged;
        window.MouseButtonChanged += OnMouseButtonChanged;
        _lastWindowWidth = Math.Max(1, window.Width);
        _lastWindowHeight = Math.Max(1, window.Height);
        overlays.RegisterOverlay(RenderOverlay);
        window.SetCursorCaptured(true);
        _initialized = true;
    }

    public void OnUpdate(float deltaTime)
    {
        if (!_initialized)
            return;

        if (_wasConsoleOpen &&
            !debugConsole.IsOpen &&
            !window.IsCursorCaptured)
        {
            window.SetCursorCaptured(_overlayVisible ? false : _cursorCapturedBeforeConsoleOpen);
        }

        _wasConsoleOpen = debugConsole.IsOpen;

        var currentWidth = Math.Max(1, window.Width);
        var currentHeight = Math.Max(1, window.Height);

        if (currentWidth != _lastWindowWidth ||
            currentHeight != _lastWindowHeight)
        {
            _lastWindowWidth = currentWidth;
            _lastWindowHeight = currentHeight;
        }

        renderer.ScenePresentationEnabled =
            !_overlayVisible ||
            _showGameViewWindow;

        if (_overlayVisible || debugConsolePanel.ShouldRender)
            imgui.BeginFrame(deltaTime);
        else
            renderer.SetSceneViewportSize(0, 0);
    }

    public void OnShutdown()
    {
        if (!_initialized)
            return;

        window.Resized -= OnWindowResized;
        window.KeyChanged -= OnKeyChanged;
        window.MouseButtonChanged -= OnMouseButtonChanged;
        if (_editorInputMap is not null)
        {
            _editorInputMap.GetAction("ToggleConsole").Started -= OnToggleConsoleStarted;
            _editorInputMap.Dispose();
            _editorInputMap = null;
        }

        _editorInputAsset?.Dispose();
        _editorInputAsset = null;
        if (_editorLayoutRef is not null)
        {
            _editorLayoutRef.Changed -= OnEditorLayoutChanged;
            _editorLayoutRef.Dispose();
            _editorLayoutRef = null;
        }
        overlays.UnregisterOverlay(RenderOverlay);
        _windowCallbacks.Clear();
        _gizmoCallbacks.Clear();
        foreach (var model in _editorModelRefs.Values)
            model.Dispose();
        _editorModelRefs.Clear();
        window.SetCursorCaptured(false);
        _initialized = false;
    }

    public void RegisterWindow(Action draw)
    {
        RegisterWindow(draw.Method.Name, draw);
    }

    public void RegisterWindow(string name, Action draw)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(draw);

        if (_windowCallbacks.Any(entry => entry.Draw == draw))
            return;

        _windowCallbacks.Add(new WindowEntry(name, draw));
    }

    public void UnregisterWindow(Action draw)
    {
        ArgumentNullException.ThrowIfNull(draw);
        _windowCallbacks.RemoveAll(entry => entry.Draw == draw);
    }

    public void Register(Action<IEditorGizmoDrawer> draw)
    {
        ArgumentNullException.ThrowIfNull(draw);

        if (!_gizmoCallbacks.Contains(draw))
            _gizmoCallbacks.Add(draw);
    }

    public void Unregister(Action<IEditorGizmoDrawer> draw)
    {
        ArgumentNullException.ThrowIfNull(draw);
        _gizmoCallbacks.Remove(draw);
    }

    public void Dispose()
    {
        gizmos.Dispose();
        imgui.Dispose();
    }

    private void OnWindowResized(int _, int __)
    {
    }

    private void OnKeyChanged(IWindow.KeyEvent keyEvent)
    {
        if (!keyEvent.IsPressed)
            return;

        if (keyEvent.Key == (int)EKeyboardKey.F12)
        {
            if (_overlayVisible)
            {
                _overlayVisible = false;
                if (_consoleOpenBeforeOverlay)
                    debugConsole.Open();
                else
                    debugConsole.Close();
            }
            else
            {
                _consoleOpenBeforeOverlay = debugConsole.IsOpen;
                if (_editorLayout.GetWindow("Debug Console").Visible)
                    debugConsole.Open();
                _overlayVisible = true;
            }

            if (debugConsole.IsOpen)
                window.SetCursorCaptured(false);
            else
                window.SetCursorCaptured(!_overlayVisible);
            return;
        }

        if (keyEvent.Key == (int)EKeyboardKey.Escape &&
            window.IsCursorCaptured)
        {
            window.SetCursorCaptured(false);
        }
    }

    private void OnToggleConsoleStarted(InputActionContext _)
    {
        if (debugConsole.IsOpen)
            CloseConsole();
        else
            OpenConsole();
    }

    private void OpenConsole()
    {
        _cursorCapturedBeforeConsoleOpen = window.IsCursorCaptured;
        debugConsole.Open();
        window.SetCursorCaptured(false);
    }

    private void CloseConsole()
    {
        debugConsole.Close();
        window.SetCursorCaptured(_overlayVisible ? false : _cursorCapturedBeforeConsoleOpen);
    }

    private void OnMouseButtonChanged(IWindow.MouseButtonEvent buttonEvent)
    {
        if (!buttonEvent.IsPressed ||
            buttonEvent.Button != (int)EMouseButton.Left ||
            window.IsCursorCaptured)
        {
            return;
        }

        if (!_overlayVisible)
        {
            if (!debugConsole.IsOpen)
                window.SetCursorCaptured(true);
            return;
        }

        if (_gameViewPresetPopupOpen)
            return;

        if (_gameViewHovered || IsPointerInsideGameView())
        {
            _focusGameViewRequested = true;
            window.SetCursorCaptured(true);
        }
    }

    private bool IsPointerInsideGameView()
    {
        if (_gameViewScreenSize.X <= 0.0f ||
            _gameViewScreenSize.Y <= 0.0f)
        {
            return false;
        }

        var pointer = input.MousePosition;
        return
            pointer.X >= _gameViewScreenPosition.X &&
            pointer.Y >= _gameViewScreenPosition.Y &&
            pointer.X < _gameViewScreenPosition.X + _gameViewScreenSize.X &&
            pointer.Y < _gameViewScreenPosition.Y + _gameViewScreenSize.Y;
    }

    private void RequestLayoutReset()
    {
        _dockLayoutDirty = true;
    }

    private void InitializeEditorLayout()
    {
        configs.Register<EditorLayoutConfig>();
        _editorLayoutRef =
            configs.LoadConfig<EditorLayoutConfig>(
                "Configs/EditorLayout.yaml");
        _editorLayoutRef.Changed += OnEditorLayoutChanged;

        if (_editorLayoutRef.TryGetValue(out var layout) &&
            layout is not null)
        {
            ApplyEditorLayout(layout);
        }
        else
        {
            ApplyEditorLayout(_editorLayout);
            if (_editorLayoutRef.LastError is not null)
            {
                Logger.Error(
                    _editorLayoutRef.LastError,
                    $"Editor layout config is invalid: {_editorLayoutRef.Path}");
            }
        }
    }

    private void OnEditorLayoutChanged(EditorLayoutConfig layout)
    {
        ApplyEditorLayout(layout);
        RequestLayoutReset();
    }

    private void ApplyEditorLayout(EditorLayoutConfig layout)
    {
        _editorLayout = layout;
        _showStatisticsWindow =
            layout.GetWindow("Rendering Statistics").Visible;
        _showGameViewWindow =
            layout.GetWindow("GameView").Visible;
        _showHierarchyWindow =
            layout.GetWindow("Hierarchy").Visible;
        _showInspectorWindow =
            layout.GetWindow("Inspector").Visible;
        _showConfigsWindow =
            layout.GetWindow("Configs").Visible;
        _showRenderSettingsWindow =
            layout.GetWindow("Render Settings").Visible;

        if (_overlayVisible)
        {
            if (layout.GetWindow("Debug Console").Visible)
                debugConsole.Open();
            else
                debugConsole.Close();
        }
    }

    private void RenderOverlay()
    {
        var shouldRenderConsoleOnly = debugConsolePanel.ShouldRender && !_overlayVisible;
        if (!_overlayVisible && !shouldRenderConsoleOnly)
            return;

        if (_overlayVisible)
            DrawDockspace();

        if (_overlayVisible && _showStatisticsWindow)
            DrawStatisticsWindow();

        if (_overlayVisible && _showGameViewWindow)
            DrawGameViewWindow();
        else if (_overlayVisible)
            renderer.SetSceneViewportSize(0, 0);

        if (_overlayVisible && _showHierarchyWindow)
            DrawHierarchyWindow();

        if (_overlayVisible && _showInspectorWindow)
            DrawInspectorWindow();

        if (_overlayVisible && _showConfigsWindow)
            DrawConfigsWindow();

        if (_overlayVisible && _showRenderSettingsWindow)
            DrawRenderSettingsWindow();

        if (_overlayVisible)
        {
            foreach (var entry in _windowCallbacks.ToArray())
            {
                if (!entry.Visible)
                    continue;

                entry.Draw();
            }
        }

        debugConsolePanel.Draw(_overlayVisible);

        imgui.Render();

        if (_overlayVisible)
            DrawGizmos();

        FlushPendingEditorActions();
    }

    private void DrawDockspace()
    {
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.WorkPos);
        ImGui.SetNextWindowSize(viewport.WorkSize);
        ImGui.SetNextWindowViewport(viewport.ID);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

        const ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoTitleBar |
            ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoBringToFrontOnFocus |
            ImGuiWindowFlags.NoNavFocus |
            ImGuiWindowFlags.NoDocking |
            ImGuiWindowFlags.MenuBar;

        var open = true;
        if (!ImGui.Begin("EditorDockspace", ref open, flags))
        {
            ImGui.End();
            ImGui.PopStyleVar(3);
            return;
        }

        var dockspaceId = ImGui.GetID("EditorDockspaceRoot");
        ImGui.DockSpace(
            dockspaceId,
            Vector2.Zero,
            ImGuiDockNodeFlags.None);

        if (_dockLayoutDirty)
        {
            BuildDefaultDockLayout(dockspaceId, viewport.WorkSize);
            _dockLayoutDirty = false;
        }

        if (ImGui.BeginMenuBar())
        {
            if (ImGui.BeginMenu("Windows"))
            {
                ImGui.MenuItem("Statistics", string.Empty, ref _showStatisticsWindow);
                ImGui.MenuItem("GameView", string.Empty, ref _showGameViewWindow);
                ImGui.MenuItem("Hierarchy", string.Empty, ref _showHierarchyWindow);
                ImGui.MenuItem("Inspector", string.Empty, ref _showInspectorWindow);
                ImGui.MenuItem("Configs", string.Empty, ref _showConfigsWindow);
                ImGui.MenuItem("Render Settings", string.Empty, ref _showRenderSettingsWindow);

                var consoleVisible = debugConsole.IsOpen;
                if (ImGui.MenuItem("Debug Console", string.Empty, consoleVisible))
                {
                    if (consoleVisible)
                        CloseConsole();
                    else
                        OpenConsole();
                }

                ImGui.Separator();

                foreach (var entry in _windowCallbacks)
                {
                    var visible = entry.Visible;
                    if (ImGui.MenuItem(entry.Name, string.Empty, visible))
                        entry.Visible = !visible;
                }

                ImGui.EndMenu();
            }

            ImGui.EndMenuBar();
        }

        ImGui.End();
        ImGui.PopStyleVar(3);
    }

    private void BuildDefaultDockLayout(
        uint dockspaceId,
        Vector2 viewportSize)
    {
        unsafe
        {
            ImGuiDockBuilderNative.RemoveNode(dockspaceId);
            ImGuiDockBuilderNative.AddNode(
                dockspaceId,
                ImGuiDockBuilderNative.DockSpaceNodeFlag);
            ImGuiDockBuilderNative.SetNodeSize(dockspaceId, viewportSize);

            var root = dockspaceId;
            uint left;
            uint main;
            ImGuiDockBuilderNative.SplitNode(
                root,
                ImGuiDir.Left,
                _editorLayout.Splits.LeftWidth,
                &left,
                &main);

            uint right;
            uint center;
            ImGuiDockBuilderNative.SplitNode(
                main,
                ImGuiDir.Right,
                _editorLayout.Splits.RightWidth,
                &right,
                &center);

            uint bottom;
            uint centerTop;
            ImGuiDockBuilderNative.SplitNode(
                center,
                ImGuiDir.Down,
                _editorLayout.Splits.BottomHeight,
                &bottom,
                &centerTop);

            uint top;
            uint game;
            ImGuiDockBuilderNative.SplitNode(
                centerTop,
                ImGuiDir.Up,
                _editorLayout.Splits.TopHeight,
                &top,
                &game);

            var dockAreas = new Dictionary<string, uint>(StringComparer.Ordinal)
            {
                ["left"] = left,
                ["center"] = game,
                ["right"] = right,
                ["bottom"] = bottom,
                ["top"] = top
            };

            foreach (var (windowName, windowLayout) in
                     _editorLayout.Windows
                         .OrderBy(entry =>
                             _editorLayout.IsActiveTab(
                                 entry.Value.Dock,
                                 entry.Key)))
            {
                ImGuiDockBuilderNative.DockWindow(
                    windowName,
                    dockAreas[windowLayout.Dock]);
            }

            ImGuiDockBuilderNative.Finish(dockspaceId);
        }
    }

    private void DrawStatisticsWindow()
    {
        var statistics = renderer.Statistics;

        ImGui.SetNextWindowBgAlpha(0.9f);
        if (!ImGui.Begin("Rendering Statistics", ref _showStatisticsWindow, ImGuiWindowFlags.NoCollapse))
        {
            ImGui.End();
            return;
        }
        ImGui.Text($"FPS: {statistics.FramesPerSecond:F1}");
        ImGui.Text($"Frame: {statistics.FrameTimeMilliseconds:F2} ms");
        ImGui.Separator();
        ImGui.Text($"Views: {statistics.ActiveViews}");
        ImGui.Text($"Render items: {statistics.RenderItems}");
        ImGui.Text($"Draw calls: {statistics.DrawCalls}");

        ImGui.End();
    }

    private void DrawGameViewWindow()
    {
        _gameViewHovered = false;
        _gameViewPresetPopupOpen = false;
        _gameViewScreenPosition = Vector2.Zero;
        _gameViewScreenSize = Vector2.Zero;

        if (_focusGameViewRequested)
        {
            ImGui.SetNextWindowFocus();
            _focusGameViewRequested = false;
        }

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        if (!ImGui.Begin("GameView", ref _showGameViewWindow))
        {
            renderer.SetSceneViewportSize(0, 0);
            ImGui.End();
            ImGui.PopStyleVar();
            return;
        }

        if (ImGui.BeginCombo(
                "##gameview_preset",
                GameViewPresets[_selectedGameViewPreset].Label))
        {
            _gameViewPresetPopupOpen = true;
            for (var index = 0; index < GameViewPresets.Length; index++)
            {
                var selected = index == _selectedGameViewPreset;
                if (ImGui.Selectable(GameViewPresets[index].Label, selected))
                    _selectedGameViewPreset = index;

                if (selected)
                    ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }

        var available = ImGui.GetContentRegionAvail();
        var viewport = ResolveGameViewViewport(available);
        renderer.SetSceneViewportSize(
            Math.Max(1, (int)viewport.Size.X),
            Math.Max(1, (int)viewport.Size.Y));

        var textureId = renderer.SceneTextureId;
        if (textureId != 0)
        {
            var cursor = ImGui.GetCursorPos();
            ImGui.SetCursorPos(cursor + viewport.Offset);
            ImGui.Image(
                textureId,
                viewport.Size,
                new Vector2(0.0f, 1.0f),
                new Vector2(1.0f, 0.0f));
            _gameViewHovered = ImGui.IsItemHovered();
            _gameViewScreenPosition = ImGui.GetItemRectMin();
            _gameViewScreenSize = ImGui.GetItemRectSize();
        }
        else
        {
            ImGui.Dummy(available);
            _gameViewHovered = ImGui.IsItemHovered();
        }

        ImGui.End();
        ImGui.PopStyleVar();
    }

    private GameViewViewport ResolveGameViewViewport(Vector2 available)
    {
        var preset = GameViewPresets[
            Math.Clamp(_selectedGameViewPreset, 0, GameViewPresets.Length - 1)];
        var fullSize = new Vector2(
            Math.Max(1.0f, available.X),
            Math.Max(1.0f, available.Y));

        if (preset.Width <= 0 || preset.Height <= 0)
            return new GameViewViewport(Vector2.Zero, fullSize);

        var targetAspect = preset.Width / (float)preset.Height;
        var availableAspect = fullSize.X / Math.Max(1.0f, fullSize.Y);
        Vector2 size;

        if (availableAspect > targetAspect)
            size = new Vector2(fullSize.Y * targetAspect, fullSize.Y);
        else
            size = new Vector2(fullSize.X, fullSize.X / targetAspect);

        var offset = (fullSize - size) * 0.5f;
        return new GameViewViewport(offset, size);
    }

    private void DrawHierarchyWindow()
    {
        ImGui.SetNextWindowBgAlpha(0.9f);
        if (!ImGui.Begin("Hierarchy", ref _showHierarchyWindow))
        {
            ImGui.End();
            return;
        }

        if (scenes.LoadedScenes.Count == 0)
        {
            ImGui.TextDisabled("No loaded scenes");
            ImGui.End();
            return;
        }

        if (ImGui.TreeNodeEx("Scenes##scenes_root", ImGuiTreeNodeFlags.DefaultOpen))
        {
            foreach (var scene in scenes.LoadedScenes)
                DrawSceneNode(scene);
            ImGui.TreePop();
        }

        if (ImGui.BeginPopupContextWindow("hierarchy_window_context", ImGuiPopupFlags.MouseButtonRight | ImGuiPopupFlags.NoOpenOverItems))
        {
            var targetScene = _selectedScene ?? scenes.ActiveScene;
            if (targetScene is not null && ImGui.MenuItem("Create Empty"))
            {
                var sceneObject = targetScene.CreateObject("SceneObject");
                SelectSceneObject(sceneObject);
            }

            ImGui.EndPopup();
        }

        ImGui.End();
    }

    private void DrawSceneNode(Vecxy.Scene.SceneInstance sceneInstance)
    {
        var isActive = ReferenceEquals(scenes.ActiveScene, sceneInstance);
        var label = isActive
            ? $"● {sceneInstance.GetType().FullName}##scene_{sceneInstance.GetHashCode()}"
            : $"○ {sceneInstance.GetType().FullName}##scene_{sceneInstance.GetHashCode()}";

        var flags =
            ImGuiTreeNodeFlags.OpenOnArrow |
            ImGuiTreeNodeFlags.OpenOnDoubleClick |
            (_selectedScene == sceneInstance ? ImGuiTreeNodeFlags.Selected : ImGuiTreeNodeFlags.None) |
            (sceneInstance.RootObjects.Any()
                ? ImGuiTreeNodeFlags.None
                : ImGuiTreeNodeFlags.Leaf);

        var opened = ImGui.TreeNodeEx(label, flags);
        if (ImGui.IsItemClicked())
            SelectScene(sceneInstance);

        if (ImGui.BeginPopupContextItem($"scene_context_{sceneInstance.GetHashCode()}"))
        {
            if (ImGui.MenuItem("Create Empty"))
            {
                var sceneObject = sceneInstance.CreateObject("SceneObject");
                SelectSceneObject(sceneObject);
            }

            ImGui.EndPopup();
        }

        if (!opened)
            return;

        foreach (var root in sceneInstance.RootObjects)
            DrawHierarchyNode(root);

        ImGui.TreePop();
    }

    private void DrawHierarchyNode(SceneObject sceneObject)
    {
        var flags =
            ImGuiTreeNodeFlags.OpenOnArrow |
            ImGuiTreeNodeFlags.OpenOnDoubleClick |
            (ReferenceEquals(_selectedSceneObject, sceneObject)
                ? ImGuiTreeNodeFlags.Selected
                : ImGuiTreeNodeFlags.None) |
            (sceneObject.Children.Count == 0
                ? ImGuiTreeNodeFlags.Leaf
                : ImGuiTreeNodeFlags.None);

        if (!sceneObject.Enabled)
            ImGui.PushStyleColor(ImGuiCol.Text, 0xFF808080);
        
        var opened = ImGui.TreeNodeEx(
            $"{sceneObject.Name}##hierarchy_{sceneObject.GetHashCode()}",
            flags);

        if (ImGui.IsItemClicked())
            SelectSceneObject(sceneObject);

        if (ImGui.BeginPopupContextItem($"hierarchy_context_{sceneObject.GetHashCode()}"))
        {
            if (ImGui.MenuItem("Create Empty Child"))
            {
                var child = sceneObject.CreateChild("SceneObject");
                SelectSceneObject(child);
            }

            if (ImGui.MenuItem("Delete"))
                QueueDeleteSceneObject(sceneObject);

            ImGui.EndPopup();
        }

        if (!sceneObject.Enabled)
            ImGui.PopStyleColor();

        if (!opened)
            return;

        foreach (var child in sceneObject.Children)
            DrawHierarchyNode(child);

        ImGui.TreePop();
    }

    private void DrawInspectorWindow()
    {
        ImGui.SetNextWindowBgAlpha(0.9f);
        if (!ImGui.Begin("Inspector", ref _showInspectorWindow))
        {
            ImGui.End();
            return;
        }

        if (_selectedConfig is not null)
        {
            DrawConfigInspector();
            ImGui.End();
            return;
        }

        if (_selectedScene is not null &&
            ReferenceEquals(_selectedScene, scenes.ActiveScene))
        {
            DrawSceneInspector(_selectedScene);
            ImGui.End();
            return;
        }

        if (_selectedSceneObject is null ||
            _selectedSceneObject.IsDestroyed ||
            !ReferenceEquals(_selectedSceneObject.SceneInstance, scenes.ActiveScene))
        {
            _selectedSceneObject = null;
            ImGui.TextDisabled("Nothing selected");
            ImGui.End();
            return;
        }

        DrawSceneObjectInspector(_selectedSceneObject);
        ImGui.End();
    }

    private void DrawSceneInspector(Vecxy.Scene.SceneInstance sceneInstance)
    {
        ImGui.Text($"Scene: {sceneInstance.GetType().FullName}");
        ImGui.Separator();

        if (ImGui.CollapsingHeader("Lighting", ImGuiTreeNodeFlags.DefaultOpen))
            DrawSceneLightingInspector(sceneInstance.Lighting);
    }

    private static void DrawSceneLightingInspector(
        SceneLightingSettings lighting)
    {
        if (ImGui.CollapsingHeader("Global", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawObjectProperties(
                lighting,
                "scene_lighting_global");
        }

        if (ImGui.CollapsingHeader("Fog", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var enabled = lighting.Fog.Enabled;
            if (ImGui.Checkbox("Enabled##scene_fog_enabled", ref enabled))
                lighting.Fog.Enabled = enabled;

            DrawObjectProperties(
                lighting.Fog,
                "scene_lighting_fog",
                static property => property.Name == nameof(SceneFogSettings.Enabled));
        }

        if (ImGui.CollapsingHeader("Skybox", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var enabled = lighting.Skybox.Enabled;
            if (ImGui.Checkbox("Enabled##scene_skybox_enabled", ref enabled))
                lighting.Skybox.Enabled = enabled;

            DrawObjectProperties(
                lighting.Skybox,
                "scene_lighting_skybox",
                static property => property.Name == nameof(SceneSkyboxSettings.Enabled));
        }
    }

    private void DrawSceneObjectInspector(SceneObject sceneObject)
    {
        var name = sceneObject.Name;
        if (ImGui.InputText("Name", ref name, 256))
            sceneObject.Name = name;

        var enabled = sceneObject.Enabled;
        if (ImGui.Checkbox("Enabled", ref enabled))
            sceneObject.Enabled = enabled;

        var isStatic = sceneObject.IsStatic;
        if (ImGui.Checkbox("Is Static", ref isStatic))
            sceneObject.IsStatic = isStatic;

        ImGui.Separator();

        DrawTransformInspector(sceneObject.Transform);

        if (ImGui.Button("Delete Object"))
        {
            QueueDeleteSceneObject(sceneObject);
            return;
        }

        foreach (var component in sceneObject.Components.ToArray())
        {
            if (component is Transform)
                continue;

            if (!DrawComponentInspector(sceneObject, component))
                break;
        }

        ImGui.Separator();
        DrawAddComponentSection(sceneObject);
    }

    private void DrawConfigsWindow()
    {
        ImGui.SetNextWindowBgAlpha(0.9f);
        if (!ImGui.Begin("Configs", ref _showConfigsWindow))
        {
            ImGui.End();
            return;
        }

        foreach (var config in configs.GetLoadedConfigs())
        {
            var selected = ReferenceEquals(_selectedConfig, config);
            if (ImGui.Selectable(config.Path, selected))
                SelectConfig(config);
        }

        ImGui.End();
    }

    private void DrawRenderSettingsWindow()
    {
        ImGui.SetNextWindowBgAlpha(0.9f);
        if (!ImGui.Begin("Render Settings", ref _showRenderSettingsWindow))
        {
            ImGui.End();
            return;
        }

        var wireframe = renderer.Wireframe;
        if (ImGui.Checkbox("Wireframe", ref wireframe))
            renderer.Wireframe = wireframe;

        var gizmosEnabled = _gizmosEnabled;
        if (ImGui.Checkbox("Gizmos Enabled", ref gizmosEnabled))
            _gizmosEnabled = gizmosEnabled;

        var gizmoMode = (int)GizmoDisplayMode;
        var gizmoModeNames = new[]
        {
            "Visible Only",
            "X-Ray",
            "Hidden + Visible"
        };

        if (ImGui.Combo("Gizmos", ref gizmoMode, gizmoModeNames, gizmoModeNames.Length))
            GizmoDisplayMode = (EGizmoDisplayMode)gizmoMode;

        ImGui.End();
    }

    private void DrawConfigInspector()
    {
        if (_selectedConfig is null)
        {
            ImGui.TextDisabled("Nothing selected");
            return;
        }

        if (_selectedConfig.TryGetUntypedValue(out var currentValue) &&
            currentValue is not null)
        {
            if (!ReferenceEquals(_selectedConfigValue, currentValue) ||
                _selectedConfigVersion != _selectedConfig.Version)
            {
                _selectedConfigValue = currentValue;
                _selectedConfigVersion = _selectedConfig.Version;
            }
        }

        ImGui.TextWrapped(_selectedConfig.Path);
        ImGui.TextDisabled(_selectedConfig.ValueType.Name);
        ImGui.TextDisabled($"Version: {_selectedConfig.Version}");

        if (_selectedConfig.LastError is not null)
        {
            ImGui.Separator();
            ImGui.TextColored(new Vector4(1.0f, 0.4f, 0.4f, 1.0f), _selectedConfig.LastError.Message);
        }

        if (_selectedConfigValue is not null)
        {
            ImGui.Separator();
            DrawConfigValueEditor(
                _selectedConfigValue,
                $"config_{_selectedConfig.Path}");
            ImGui.Separator();

            if (ImGui.Button("Save Config"))
            {
                try
                {
                    configs.SaveConfig(_selectedConfig, _selectedConfigValue);
                }
                catch (Exception exception)
                {
                    Logger.Error(exception, $"Failed to save config '{_selectedConfig.Path}'.");
                }
            }
        }
        else
        {
            ImGui.Separator();
            ImGui.TextDisabled("Config has no valid value");
        }
    }

    private static void DrawTransformInspector(Transform transform)
    {
        if (!ImGui.CollapsingHeader("Transform", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        var position = transform.Position;
        if (ImGui.DragFloat3("Position", ref position, 0.01f))
            transform.Position = position;

        var rotation = ToEulerDegrees(transform.Rotation);
        if (ImGui.DragFloat3("Rotation", ref rotation, 0.25f))
            transform.Rotation = FromEulerDegrees(rotation);

        var scale = transform.Scale;
        if (ImGui.DragFloat3("Scale", ref scale, 0.01f))
            transform.Scale = scale;
    }

    private bool DrawComponentInspector(
        SceneObject sceneObject,
        AComponent component)
    {
        var type = component.GetType();
        ImGui.PushID(component.GetHashCode());

        var open = ImGui.CollapsingHeader(type.Name, ImGuiTreeNodeFlags.DefaultOpen);
        var removable = component is not Transform;

        if (!open)
        {
            ImGui.PopID();
            return true;
        }

        var enabled = component.Enabled;
        if (ImGui.Checkbox("Enabled", ref enabled))
            component.Enabled = enabled;

        if (removable)
        {
            if (ImGui.Button($"Remove Component##{component.GetHashCode()}"))
            {
                QueueRemoveComponent(sceneObject, component);
                ImGui.PopID();
                return false;
            }

            ImGui.Separator();
        }

        if (component is MeshRenderer meshRenderer)
        {
            DrawMeshRendererInspector(meshRenderer);
            ImGui.PopID();
            return true;
        }

        if (component is PostProcessing postProcessing)
        {
            DrawPostProcessingInspector(postProcessing);
            ImGui.PopID();
            return true;
        }

        foreach (var property in GetInspectableProperties(type))
            DrawProperty(component, property);

        ImGui.PopID();
        return true;
    }

    private void DrawMeshRendererInspector(MeshRenderer renderer)
    {
        foreach (var property in GetInspectableProperties(typeof(MeshRenderer)))
            DrawProperty(renderer, property);

        if (!renderer.IsConfigured)
        {
            ImGui.TextDisabled("MeshRenderer is not configured");
            return;
        }

        ImGui.Separator();
        ImGui.TextWrapped($"Mesh: {renderer.Mesh.Name}");
        ImGui.Text($"Mesh Indices: {renderer.Mesh.IndexCount}");
        ImGui.Text($"Bounds Size: {renderer.LocalBoundsSize}");
        ImGui.Text($"Bounds Center: {renderer.LocalBoundsCenter}");
        DrawMeshSelection(renderer);

        ImGui.Separator();
        ImGui.TextWrapped($"Material: {renderer.Material.SourcePath}");
        DrawMaterialSelection(renderer);

        var surface = (int)renderer.Material.Surface;
        var surfaceNames = Enum.GetNames<EMaterialSurface>();
        if (ImGui.Combo($"Surface##{renderer.GetHashCode()}", ref surface, surfaceNames, surfaceNames.Length))
            renderer.Material.Surface = (EMaterialSurface)surface;

        if (renderer.Material.Surface == EMaterialSurface.Cutout)
        {
            var alphaCutoff = renderer.Material.AlphaCutoff;
            if (ImGui.SliderFloat($"Alpha Cutoff##{renderer.GetHashCode()}", ref alphaCutoff, 0.0f, 1.0f))
                renderer.Material.AlphaCutoff = alphaCutoff;
        }

        if (ImGui.Button($"Reset Material Overrides##{renderer.GetHashCode()}"))
            renderer.Material.ClearOverrides();

        foreach (var (name, parameter) in renderer.Material.Parameters.ToArray())
        {
            DrawMaterialParameter(assets, renderer.Material, name, parameter);
        }
    }

    private static void DrawPostProcessingInspector(
        PostProcessing postProcessing)
    {
        ImGui.TextDisabled(
            "Live preview only. Config reload overwrites these runtime edits.");
        ImGui.Separator();

        foreach (var effect in postProcessing.EnumerateEffects())
        {
            if (!ImGui.CollapsingHeader(
                    effect.Name,
                    ImGuiTreeNodeFlags.DefaultOpen))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(effect.InspectorSourcePath))
            {
                ImGui.TextWrapped(effect.InspectorSourcePath);
                ImGui.TextDisabled($"Version: {effect.InspectorVersion}");
            }
            else
            {
                ImGui.TextDisabled("No bound config");
            }

            if (effect.InspectorError is not null)
            {
                ImGui.TextColored(
                    new Vector4(1.0f, 0.4f, 0.4f, 1.0f),
                    effect.InspectorError.Message);
            }

            if (effect.InspectorSettings is not null)
            {
                DrawObjectProperties(
                    effect.InspectorSettings,
                    $"postfx_{postProcessing.GetHashCode()}_{effect.Name}");
            }
            else
            {
                ImGui.TextDisabled("Effect has no editable runtime settings.");
            }

            ImGui.Separator();
        }
    }

    private static void DrawMaterialParameter(
        IAssetsManager assets,
        Material material,
        string name,
        MaterialParameter parameter)
    {
        switch (parameter)
        {
            case VectorMaterialParameter vector:
            {
                var value = vector.Value;
                var isColor = name.Contains("Color", StringComparison.OrdinalIgnoreCase) ||
                              name.Contains("Tint", StringComparison.OrdinalIgnoreCase);
                var changed = isColor
                    ? ImGui.ColorEdit4(name, ref value)
                    : ImGui.DragFloat4(name, ref value, 0.01f);

                if (changed)
                    material.SetVector(name, value);
                break;
            }

            case FloatMaterialParameter scalar:
            {
                var value = scalar.Value;
                if (ImGui.DragFloat(name, ref value, 0.01f))
                    material.SetFloat(name, value);
                break;
            }

            case TextureMaterialParameter texture:
            {
                ImGui.TextWrapped($"{name}: {texture.Texture.Metadata.Path}");
                DrawTextureSelection(
                    assets,
                    material,
                    name,
                    texture.Texture.Metadata.Path);

                var tiling = texture.Tiling;
                if (ImGui.DragFloat2($"Tiling##{name}", ref tiling, 0.01f))
                    material.SetTextureTransform(name, tiling, texture.Offset);

                var offset = texture.Offset;
                if (ImGui.DragFloat2($"Offset##{name}", ref offset, 0.01f))
                    material.SetTextureTransform(name, texture.Tiling, offset);
                break;
            }

            case EmbeddedTextureMaterialParameter texture:
            {
                ImGui.Text($"{name}: embedded {texture.Texture.Width}x{texture.Texture.Height}");
                DrawTextureSelection(
                    assets,
                    material,
                    name,
                    "<embedded>");

                var tiling = texture.Tiling;
                if (ImGui.DragFloat2($"Tiling##{name}", ref tiling, 0.01f))
                    material.SetTextureTransform(name, tiling, texture.Offset);

                var offset = texture.Offset;
                if (ImGui.DragFloat2($"Offset##{name}", ref offset, 0.01f))
                    material.SetTextureTransform(name, texture.Tiling, offset);
                break;
            }

            default:
                ImGui.TextDisabled($"{name}: unsupported");
                break;
        }

        if (material.IsOverridden(name))
        {
            ImGui.SameLine();
            if (ImGui.SmallButton($"Reset##material_{name}"))
                material.ClearOverride(name);
        }
    }

    private void DrawAddComponentSection(SceneObject sceneObject)
    {
        if (!ImGui.CollapsingHeader("Add Component", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        if (!ImGui.BeginCombo("##add_component", "Add Component..."))
            return;

        ImGui.SetNextItemWidth(-1.0f);
        ImGui.InputTextWithHint("##component_search", "Search...", ref _componentSearch, 128);
        ImGui.Separator();

        foreach (var componentType in GetAddableComponentTypes())
        {
            var disallowsMultiple =
                componentType.IsDefined(
                    typeof(SingleComponentAttribute),
                    inherit: true);
            var alreadyPresent = sceneObject.Components.Any(
                component => component.GetType() == componentType);

            if (disallowsMultiple && alreadyPresent)
                continue;

            if (!string.IsNullOrWhiteSpace(_componentSearch) &&
                !componentType.Name.Contains(_componentSearch, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!ImGui.Selectable(componentType.Name))
                continue;

            if (Activator.CreateInstance(componentType) is AComponent component)
                sceneObject.AddComponent(component);

            _componentSearch = string.Empty;
            ImGui.CloseCurrentPopup();
            break;
        }

        ImGui.EndCombo();
    }

    private IEnumerable<Type> GetAddableComponentTypes()
    {
        return AppDomain.CurrentDomain
            .GetAssemblies()
            .Where(assembly => !assembly.IsDynamic)
            .SelectMany(GetLoadableTypes)
            .Where(type =>
                type.IsClass &&
                !type.IsAbstract &&
                type.IsSubclassOf(typeof(AComponent)) &&
                type != typeof(Transform) &&
                type.GetConstructor(Type.EmptyTypes) is not null)
            .OrderBy(type => type.Name);
    }

    private void DrawMeshSelection(MeshRenderer renderer)
    {
        var meshOptions = EnumerateAvailableMeshes().ToArray();
        if (meshOptions.Length > 0)
        {
            var currentIndex = Array.FindIndex(
                meshOptions,
                option => ReferenceEquals(option.Mesh, renderer.Mesh));
            var preview = currentIndex >= 0
                ? meshOptions[currentIndex].Label
                : "<select mesh>";

            if (ImGui.BeginCombo($"Mesh##{renderer.GetHashCode()}", preview))
            {
                for (var index = 0; index < meshOptions.Length; index++)
                {
                    var selected = index == currentIndex;
                    if (ImGui.Selectable(meshOptions[index].Label, selected))
                        renderer.SetMesh(meshOptions[index].Mesh);

                    if (selected)
                        ImGui.SetItemDefaultFocus();
                }

                ImGui.EndCombo();
            }
        }
        else
        {
            ImGui.TextDisabled("No meshes available");
        }

        DrawModelLoadControl(
            "Load Model For Meshes",
            path => TryLoadMesh(renderer, path));
    }

    private void DrawMaterialSelection(MeshRenderer renderer)
    {
        var materialPaths = EnumerateAssetPaths(MaterialExtensions).ToArray();
        if (materialPaths.Length > 0)
        {
            var currentPath = renderer.Material.SourcePath;
            var currentIndex = Array.FindIndex(
                materialPaths,
                path => string.Equals(path, currentPath, StringComparison.OrdinalIgnoreCase));
            var preview = currentIndex >= 0
                ? materialPaths[currentIndex]
                : currentPath;

            if (ImGui.BeginCombo($"Material##{renderer.GetHashCode()}", preview))
            {
                for (var index = 0; index < materialPaths.Length; index++)
                {
                    var selected = index == currentIndex;
                    if (ImGui.Selectable(materialPaths[index], selected))
                        TryLoadMaterial(renderer, materialPaths[index]);

                    if (selected)
                        ImGui.SetItemDefaultFocus();
                }

                ImGui.EndCombo();
            }
        }
        else
        {
            ImGui.TextDisabled("No materials available");
        }

        if (ImGui.Button($"Browse Material##{renderer.GetHashCode()}"))
            TryBrowseAsset(MaterialExtensions, path => TryLoadMaterial(renderer, path));
    }

    private static void DrawTextureSelection(
        IAssetsManager assets,
        Material material,
        string parameterName,
        string currentPath)
    {
        var texturePaths = EnumerateAssetPaths(assets.AssetsDirectory, TextureExtensions).ToArray();
        if (texturePaths.Length > 0)
        {
            var currentIndex = Array.FindIndex(
                texturePaths,
                path => string.Equals(path, currentPath, StringComparison.OrdinalIgnoreCase));
            var preview = currentIndex >= 0
                ? texturePaths[currentIndex]
                : currentPath;

            if (ImGui.BeginCombo($"Texture##{parameterName}", preview))
            {
                for (var index = 0; index < texturePaths.Length; index++)
                {
                    var selected = index == currentIndex;
                    if (ImGui.Selectable(texturePaths[index], selected))
                        TryLoadTexture(assets, material, parameterName, texturePaths[index]);

                    if (selected)
                        ImGui.SetItemDefaultFocus();
                }

                ImGui.EndCombo();
            }
        }

        if (ImGui.Button($"Browse Texture##{parameterName}"))
            TryBrowseAsset(
                assets.AssetsDirectory,
                TextureExtensions,
                path => TryLoadTexture(assets, material, parameterName, path));
    }

    private static void TryLoadTexture(
        IAssetsManager assets,
        Material material,
        string parameterName,
        string path)
    {
        try
        {
            using var texture = assets.Load<TextureAsset>(path);
            material.SetTexture(parameterName, texture);
        }
        catch (Exception exception)
        {
            Logger.Error(exception, $"Failed to load texture '{path}'.");
        }
    }

    private void TryLoadMaterial(
        MeshRenderer renderer,
        string path)
    {
        try
        {
            using var material = assets.Load<Material>(path);
            renderer.SetMaterial(material.Value.Clone());
        }
        catch (Exception exception)
        {
            Logger.Error(exception, $"Failed to load material '{path}'.");
        }
    }

    private void TryLoadMesh(
        MeshRenderer renderer,
        string path)
    {
        try
        {
            var model = LoadEditorModel(path).Value;
            if (model.Meshes.Count == 0)
                return;

            for (var meshIndex = 0; meshIndex < model.Meshes.Count; meshIndex++)
            {
                var meshes = meshResolver.GetMeshes(model, meshIndex);
                if (meshes.Count == 0)
                    continue;

                renderer.SetMesh(meshes[0]);
                return;
            }
        }
        catch (Exception exception)
        {
            Logger.Error(exception, $"Failed to load mesh from '{path}'.");
        }
    }

    private void DrawModelLoadControl(
        string buttonLabel,
        Action<string> load)
    {
        if (ImGui.Button(buttonLabel))
            TryBrowseAsset(ModelExtensions, load);
    }

    private void TryBrowseAsset(
        IEnumerable<string> extensions,
        Action<string> onSelected)
    {
        TryBrowseAsset(assets.AssetsDirectory, extensions, onSelected);
    }

    private static void TryBrowseAsset(
        string assetsDirectory,
        IEnumerable<string> extensions,
        Action<string> onSelected)
    {
        try
        {
            var selectedPath = ShowOpenFileDialog(assetsDirectory, extensions);
            if (string.IsNullOrWhiteSpace(selectedPath))
                return;

            var relative = Path.GetRelativePath(assetsDirectory, selectedPath)
                .Replace('\\', '/');
            onSelected(relative);
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "System file dialog failed.");
        }
    }

    private static string? ShowOpenFileDialog(
        string initialDirectory,
        IEnumerable<string> extensions)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return ShowLinuxOpenFileDialog(initialDirectory, extensions);

        throw new PlatformNotSupportedException("System file dialog is not implemented for this platform.");
    }

    private static string? ShowLinuxOpenFileDialog(
        string initialDirectory,
        IEnumerable<string> extensions)
    {
        var patterns = extensions
            .Select(extension => $"*{extension}")
            .ToArray();

        if (TryRunDialog(
                "zenity",
                BuildZenityArguments(initialDirectory, patterns),
                out var zenityPath))
        {
            return zenityPath;
        }

        if (TryRunDialog(
                "kdialog",
                BuildKDialogArguments(initialDirectory, patterns),
                out var kdialogPath))
        {
            return kdialogPath;
        }

        if (TryRunDialog(
                "qarma",
                BuildZenityArguments(initialDirectory, patterns),
                out var qarmaPath))
        {
            return qarmaPath;
        }

        throw new InvalidOperationException("No supported system file dialog was found. Install zenity or kdialog.");
    }

    private static IEnumerable<string> BuildZenityArguments(
        string initialDirectory,
        IReadOnlyList<string> patterns)
    {
        yield return "--file-selection";
        yield return "--filename";
        yield return $"{Path.GetFullPath(initialDirectory).TrimEnd(Path.DirectorySeparatorChar)}/";

        if (patterns.Count > 0)
        {
            yield return "--file-filter";
            yield return $"Supported files | {string.Join(' ', patterns)}";
        }
    }

    private static IEnumerable<string> BuildKDialogArguments(
        string initialDirectory,
        IReadOnlyList<string> patterns)
    {
        yield return "--getopenfilename";
        yield return Path.GetFullPath(initialDirectory);
        yield return patterns.Count > 0
            ? string.Join(' ', patterns)
            : "*";
    }

    private static bool TryRunDialog(
        string command,
        IEnumerable<string> arguments,
        out string? selectedPath)
    {
        selectedPath = null;

        if (!CommandExists(command))
            return false;

        using var process = new global::System.Diagnostics.Process();
        process.StartInfo = new global::System.Diagnostics.ProcessStartInfo
        {
            FileName = command,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
            return true;

        selectedPath = output.Trim();
        return true;
    }

    private static bool CommandExists(string command)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
            return false;

        foreach (var path in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (File.Exists(Path.Combine(path, command)))
                return true;
        }

        return false;
    }

    private IEnumerable<MeshOption> EnumerateAvailableMeshes()
    {
        foreach (var path in EnumerateAssetPaths(ModelExtensions))
        {
            AssetRef<Model>? modelRef = null;
            try
            {
                modelRef = LoadEditorModel(path);
            }
            catch (Exception exception)
            {
                Logger.Error(exception, $"Failed to load model '{path}' for mesh selection.");
            }

            if (modelRef is null || modelRef.HasError)
                continue;

            var model = modelRef.Value;
            for (var meshIndex = 0; meshIndex < model.Meshes.Count; meshIndex++)
            {
                var meshGroup = meshResolver.GetMeshes(model, meshIndex);
                for (var primitiveIndex = 0; primitiveIndex < meshGroup.Count; primitiveIndex++)
                {
                    var mesh = meshGroup[primitiveIndex];
                    var label = $"{path} / {model.Meshes[meshIndex].Name} / Primitive {primitiveIndex}";
                    yield return new MeshOption(label, mesh);
                }
            }
        }
    }

    private AssetRef<Model> LoadEditorModel(string path)
    {
        if (_editorModelRefs.TryGetValue(path, out var existing))
            return existing;

        var model = assets.Load<Model>(path);
        _editorModelRefs.Add(path, model);
        return model;
    }

    private void QueueDeleteSceneObject(SceneObject sceneObject)
    {
        _pendingEditorActions.Add(() =>
        {
            if (sceneObject.IsDestroyed || sceneObject.IsDestroying)
                return;

            var parent = sceneObject.Parent;
            var scene = sceneObject.SceneInstance;
            sceneObject.Destroy();

            if (parent is not null && !parent.IsDestroyed)
                SelectSceneObject(parent);
            else
                SelectScene(scene);
        });
    }

    private void QueueRemoveComponent(
        SceneObject sceneObject,
        AComponent component)
    {
        _pendingEditorActions.Add(() =>
        {
            if (sceneObject.IsDestroyed || component.IsDestroyed)
                return;

            sceneObject.RemoveComponent(component);
        });
    }

    private void FlushPendingEditorActions()
    {
        if (_pendingEditorActions.Count == 0)
            return;

        var actions = _pendingEditorActions.ToArray();
        _pendingEditorActions.Clear();

        foreach (var action in actions)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Editor action failed.");
            }
        }
    }

    private IEnumerable<string> EnumerateAssetPaths(IEnumerable<string> extensions)
    {
        return EnumerateAssetPaths(assets.AssetsDirectory, extensions);
    }

    private static IEnumerable<string> EnumerateAssetPaths(
        string assetsDirectory,
        IEnumerable<string> extensions)
    {
        var extensionSet = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(assetsDirectory))
            yield break;

        foreach (var file in Directory.EnumerateFiles(assetsDirectory, "*", SearchOption.AllDirectories))
        {
            if (!extensionSet.Contains(Path.GetExtension(file)))
                continue;

            yield return Path.GetRelativePath(assetsDirectory, file)
                .Replace('\\', '/');
        }
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.Where(type => type is not null)!;
        }
    }

    private static IEnumerable<PropertyInfo> GetInspectableProperties(Type type)
    {
        return type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property =>
                property.GetIndexParameters().Length == 0 &&
                property.CanRead &&
                property.GetCustomAttribute<EditorIgnoreAttribute>() is null &&
                property.Name is not nameof(AComponent.Enabled) &&
                property.Name is not nameof(AComponent.SceneInstance) &&
                property.Name is not nameof(AComponent.SceneObject) &&
                property.Name is not nameof(AComponent.Transform) &&
                property.Name is not nameof(AComponent.IsActive) &&
                property.Name is not nameof(AComponent.IsDestroyed))
            .Where(property =>
                IsSupportedPropertyType(property.PropertyType) ||
                IsExpandablePropertyType(property.PropertyType))
            .OrderBy(property => property.GetCustomAttribute<EditorPropertyAttribute>()?.Order ?? 0)
            .ThenBy(property => property.Name);
    }

    private static bool IsSupportedPropertyType(Type type)
    {
        return type == typeof(bool) ||
               type == typeof(int) ||
               type == typeof(float) ||
               type == typeof(string) ||
               type == typeof(float[]) ||
               type == typeof(Vector2) ||
               type == typeof(Vector3) ||
               type == typeof(Vector4) ||
               type.IsEnum;
    }

    private static bool IsExpandablePropertyType(Type type)
    {
        return type.IsClass &&
               type != typeof(string) &&
               !typeof(System.Collections.IEnumerable).IsAssignableFrom(type);
    }

    private static void DrawConfigValueEditor(
        object value,
        string prefix)
    {
        if (value is PhysicsConfig physicsConfig)
        {
            DrawObjectProperties(
                physicsConfig,
                prefix,
                static property => property.Name == nameof(PhysicsConfig.CollisionLayers));
            DrawPhysicsCollisionLayerMatrix(physicsConfig, prefix);
            return;
        }

        DrawObjectProperties(value, prefix);
    }

    private static void DrawObjectProperties(
        object target,
        string prefix)
    {
        DrawObjectProperties(target, prefix, null);
    }

    private static void DrawObjectProperties(
        object target,
        string prefix,
        Func<PropertyInfo, bool>? filter)
    {
        foreach (var property in GetInspectableProperties(target.GetType()))
        {
            if (filter is not null && filter(property))
                continue;

            DrawProperty(target, property, prefix);
        }
    }

    private static void DrawPhysicsCollisionLayerMatrix(
        PhysicsConfig config,
        string prefix)
    {
        if (!ImGui.CollapsingHeader(
                "Collision Layer Matrix",
                ImGuiTreeNodeFlags.DefaultOpen))
        {
            return;
        }

        if (config.CollisionLayers.Count == 0)
        {
            ImGui.TextDisabled("No collision layers configured.");
            return;
        }

        var layers = config.CollisionLayers
            .OrderBy(pair => pair.Value.Index)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var tableFlags =
            ImGuiTableFlags.Borders |
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.SizingFixedFit |
            ImGuiTableFlags.ScrollX |
            ImGuiTableFlags.ScrollY;

        if (!ImGui.BeginTable(
                $"physics_layers_matrix##{prefix}",
                layers.Length + 1,
                tableFlags,
                new Vector2(0.0f, MathF.Min(420.0f, 72.0f + layers.Length * 28.0f))))
        {
            return;
        }

        ImGui.TableSetupScrollFreeze(1, 1);
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.TextUnformatted("Layer");

        for (var column = 0; column < layers.Length; ++column)
        {
            ImGui.TableSetColumnIndex(column + 1);
            ImGui.TextUnformatted(layers[column].Key);
        }

        for (var row = 0; row < layers.Length; ++row)
        {
            var rowName = layers[row].Key;
            var rowConfig = layers[row].Value;

            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.TextUnformatted($"{rowName} [{rowConfig.Index}]");

            for (var column = 0; column < layers.Length; ++column)
            {
                var columnName = layers[column].Key;
                var checkedValue = rowConfig.CollidesWith.Contains(
                    columnName,
                    StringComparer.OrdinalIgnoreCase);

                ImGui.TableSetColumnIndex(column + 1);

                if (row == column)
                {
                    ImGui.BeginDisabled();
                    ImGui.Checkbox(
                        $"##{prefix}_{rowName}_{columnName}",
                        ref checkedValue);
                    ImGui.EndDisabled();
                    continue;
                }

                if (!ImGui.Checkbox(
                        $"##{prefix}_{rowName}_{columnName}",
                        ref checkedValue))
                {
                    continue;
                }

                SetCollisionLayerPair(
                    config,
                    rowName,
                    columnName,
                    checkedValue);
            }
        }

        ImGui.EndTable();
    }

    private static void SetCollisionLayerPair(
        PhysicsConfig config,
        string layerA,
        string layerB,
        bool enabled)
    {
        if (!config.CollisionLayers.TryGetValue(layerA, out var configA) ||
            !config.CollisionLayers.TryGetValue(layerB, out var configB))
        {
            return;
        }

        configA.CollidesWith = UpdateCollisionTargets(
            configA.CollidesWith,
            layerB,
            enabled);
        configB.CollidesWith = UpdateCollisionTargets(
            configB.CollidesWith,
            layerA,
            enabled);
    }

    private static string[] UpdateCollisionTargets(
        string[] source,
        string target,
        bool enabled)
    {
        var values = new HashSet<string>(
            source,
            StringComparer.OrdinalIgnoreCase);

        if (enabled)
            values.Add(target);
        else
            values.Remove(target);

        return values
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void DrawProperty(
        object target,
        PropertyInfo property,
        string prefix = "")
    {
        var label =
            property.GetCustomAttribute<EditorPropertyAttribute>()?.Label
            ?? property.Name;
        var controlId = string.IsNullOrEmpty(prefix)
            ? property.Name
            : $"{prefix}_{property.Name}";

        try
        {
            if (property.PropertyType == typeof(bool))
            {
                var value = (bool)property.GetValue(target)!;
                if (property.CanWrite &&
                    ImGui.Checkbox($"{label}##{controlId}", ref value))
                    property.SetValue(target, value);
                return;
            }

            if (property.PropertyType == typeof(int))
            {
                var value = (int)property.GetValue(target)!;
                if (property.CanWrite &&
                    ImGui.DragInt($"{label}##{controlId}", ref value))
                    TrySetPropertyValue(target, property, value);
                return;
            }

            if (property.PropertyType == typeof(float))
            {
                var value = (float)property.GetValue(target)!;
                if (property.CanWrite &&
                    ImGui.DragFloat($"{label}##{controlId}", ref value, 0.01f))
                    TrySetPropertyValue(
                        target,
                        property,
                        NormalizePropertyValue(target, property, value));
                return;
            }

            if (property.PropertyType == typeof(string))
            {
                var value = (string?)property.GetValue(target) ?? string.Empty;
                if (property.CanWrite &&
                    ImGui.InputText($"{label}##{controlId}", ref value, 256))
                    property.SetValue(target, value);
                return;
            }

            if (property.PropertyType == typeof(float[]))
            {
                DrawFloatArrayProperty(target, property, label, controlId);
                return;
            }

            if (property.PropertyType == typeof(Vector2))
            {
                var value = (Vector2)property.GetValue(target)!;
                if (property.CanWrite &&
                    ImGui.DragFloat2($"{label}##{controlId}", ref value, 0.01f))
                    property.SetValue(target, value);
                return;
            }

            if (property.PropertyType == typeof(Vector3))
            {
                var value = (Vector3)property.GetValue(target)!;
                var isColor = property.Name.Contains("Color", StringComparison.OrdinalIgnoreCase);
                var changed = isColor
                    ? ImGui.ColorEdit3($"{label}##{controlId}", ref value)
                    : ImGui.DragFloat3($"{label}##{controlId}", ref value, 0.01f);

                if (property.CanWrite && changed)
                    TrySetPropertyValue(target, property, value);
                return;
            }

            if (property.PropertyType == typeof(Vector4))
            {
                var value = (Vector4)property.GetValue(target)!;
                var isColor = property.Name.Contains("Color", StringComparison.OrdinalIgnoreCase);
                var changed = isColor
                    ? ImGui.ColorEdit4($"{label}##{controlId}", ref value)
                    : ImGui.DragFloat4($"{label}##{controlId}", ref value, 0.01f);

                if (property.CanWrite && changed)
                    TrySetPropertyValue(target, property, value);
                return;
            }

            if (property.PropertyType.IsEnum)
            {
                var values = Enum.GetValues(property.PropertyType);
                var names = Enum.GetNames(property.PropertyType);
                var current = Array.IndexOf(values, property.GetValue(target));
                if (current < 0)
                    current = 0;

                if (property.CanWrite &&
                    ImGui.Combo($"{label}##{controlId}", ref current, names, names.Length))
                    TrySetPropertyValue(target, property, values.GetValue(current)!);
                return;
            }

            if (IsExpandablePropertyType(property.PropertyType))
            {
                var value = property.GetValue(target);
                if (value is null)
                {
                    ImGui.TextDisabled($"{label}: <null>");
                    return;
                }

                if (ImGui.CollapsingHeader(label, ImGuiTreeNodeFlags.DefaultOpen))
                    DrawObjectProperties(value, controlId);
            }
        }
        catch (Exception exception)
        {
            Logger.Error(exception, $"Inspector failed to edit '{property.DeclaringType?.Name}.{property.Name}'.");
            ImGui.TextDisabled($"{label}: <error>");
        }
    }

    private static void DrawFloatArrayProperty(
        object target,
        PropertyInfo property,
        string label,
        string controlId)
    {
        var values = (float[]?)property.GetValue(target);
        var length = values?.Length ?? InferArrayLength(property.Name);

        if (length <= 0)
        {
            ImGui.TextDisabled($"{label}: unsupported array");
            return;
        }

        values ??= new float[length];

        var changed = false;
        if (length == 2)
        {
            var vector = new Vector2(values[0], values[1]);
            changed = ImGui.DragFloat2($"{label}##{controlId}", ref vector, 0.01f);
            if (changed)
                values = [vector.X, vector.Y];
        }
        else if (length == 3)
        {
            var vector = new Vector3(values[0], values[1], values[2]);
            changed = IsColorArray(property.Name)
                ? ImGui.ColorEdit3($"{label}##{controlId}", ref vector)
                : ImGui.DragFloat3($"{label}##{controlId}", ref vector, 0.01f);

            if (changed)
                values = [vector.X, vector.Y, vector.Z];
        }
        else if (length == 4)
        {
            var vector = new Vector4(values[0], values[1], values[2], values[3]);
            changed = IsColorArray(property.Name)
                ? ImGui.ColorEdit4($"{label}##{controlId}", ref vector)
                : ImGui.DragFloat4($"{label}##{controlId}", ref vector, 0.01f);

            if (changed)
                values = [vector.X, vector.Y, vector.Z, vector.W];
        }
        else
        {
            ImGui.TextDisabled($"{label}: unsupported array length {length}");
            return;
        }

        if (changed && property.CanWrite)
            TrySetPropertyValue(target, property, values);
    }

    private static void TrySetPropertyValue(
        object target,
        PropertyInfo property,
        object value)
    {
        try
        {
            property.SetValue(target, value);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is ArgumentOutOfRangeException)
        {
        }
        catch (ArgumentOutOfRangeException)
        {
        }
    }

    private static object NormalizePropertyValue(
        object target,
        PropertyInfo property,
        float value)
    {
        if (target is Vecxy.Physics.SphereCollider &&
            property.Name == "Radius")
        {
            return MathF.Max(0.001f, value);
        }

        if (target is ALight &&
            property.Name is "Range" or "Intensity")
        {
            return MathF.Max(0.0f, value);
        }

        if (target is SceneLightingSettings &&
            property.Name is
                "AmbientIntensity" or
                "DirectLightIntensityScale" or
                "SpecularStrength" or
                "Exposure")
        {
            return MathF.Max(0.0f, value);
        }

        if (target is SpotLight spotLight)
        {
            if (property.Name == "InnerConeAngle")
                return Math.Clamp(value, 0.0f, spotLight.OuterConeAngle);

            if (property.Name == "OuterConeAngle")
            {
                var min = Math.Max(spotLight.InnerConeAngle, 0.001f);
                return Math.Clamp(value, min, MathF.PI * 0.5f);
            }
        }

        return value;
    }

    private static int InferArrayLength(string propertyName)
    {
        if (propertyName.Contains("Offset", StringComparison.OrdinalIgnoreCase) ||
            propertyName.Contains("Tiling", StringComparison.OrdinalIgnoreCase))
            return 2;

        if (propertyName.Contains("Color", StringComparison.OrdinalIgnoreCase) ||
            propertyName.Contains("Tint", StringComparison.OrdinalIgnoreCase) ||
            propertyName.Contains("Rotation", StringComparison.OrdinalIgnoreCase))
            return 3;

        return 0;
    }

    private static bool IsColorArray(string propertyName)
    {
        return propertyName.Contains("Color", StringComparison.OrdinalIgnoreCase) ||
               propertyName.Contains("Tint", StringComparison.OrdinalIgnoreCase);
    }

    private static Vector3 ToEulerDegrees(Quaternion rotation)
    {
        rotation = Quaternion.Normalize(rotation);

        var sinrCosp = 2.0f * (rotation.W * rotation.X + rotation.Y * rotation.Z);
        var cosrCosp = 1.0f - 2.0f * (rotation.X * rotation.X + rotation.Y * rotation.Y);
        var pitch = MathF.Atan2(sinrCosp, cosrCosp);

        var sinp = 2.0f * (rotation.W * rotation.Y - rotation.Z * rotation.X);
        var yaw = MathF.Abs(sinp) >= 1.0f
            ? MathF.CopySign(MathF.PI / 2.0f, sinp)
            : MathF.Asin(sinp);

        var sinyCosp = 2.0f * (rotation.W * rotation.Z + rotation.X * rotation.Y);
        var cosyCosp = 1.0f - 2.0f * (rotation.Y * rotation.Y + rotation.Z * rotation.Z);
        var roll = MathF.Atan2(sinyCosp, cosyCosp);

        const float radToDeg = 180.0f / MathF.PI;
        return new Vector3(pitch * radToDeg, yaw * radToDeg, roll * radToDeg);
    }

    private static Quaternion FromEulerDegrees(Vector3 eulerDegrees)
    {
        const float degToRad = MathF.PI / 180.0f;
        return Quaternion.CreateFromYawPitchRoll(
            eulerDegrees.Y * degToRad,
            eulerDegrees.X * degToRad,
            eulerDegrees.Z * degToRad);
    }

    private void SelectScene(Vecxy.Scene.SceneInstance sceneInstance)
    {
        _selectedScene = sceneInstance;
        _selectedSceneObject = null;
        _selectedConfig = null;
        _selectedConfigVersion = -1;
        _selectedConfigValue = null;
    }

    private void SelectSceneObject(SceneObject sceneObject)
    {
        _selectedSceneObject = sceneObject;
        _selectedScene = null;
        _selectedConfig = null;
        _selectedConfigVersion = -1;
        _selectedConfigValue = null;
    }

    private void SelectConfig(IConfigRef config)
    {
        _selectedConfig = config;
        _selectedScene = null;
        _selectedSceneObject = null;
        _selectedConfigVersion = -1;
        _selectedConfigValue = null;
    }

    private void DrawGizmos()
    {
        if (!_gizmosEnabled ||
            !_showGameViewWindow ||
            _gameViewScreenSize.X <= 0.0f ||
            _gameViewScreenSize.Y <= 0.0f)
        {
            return;
        }

        var scene = scenes.ActiveScene;
        var camera = FindActiveCamera(scene);

        if (scene is null || camera is null)
            return;

        var drawer = new EditorGizmoDrawer(
            gizmos);

        foreach (var sceneObject in scene.Objects)
        {
            if (!sceneObject.IsActive)
                continue;

            foreach (var component in sceneObject.Components)
            {
                try
                {
                    component.DrawGizmos(drawer);
                }
                catch (Exception exception)
                {
                    Logger.Error(exception, "Component gizmo drawing failed.");
                }
            }
        }

        foreach (var draw in _gizmoCallbacks.ToArray())
        {
            try
            {
                draw(drawer);
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Editor gizmo callback failed.");
            }
        }

        gizmos.Render(
            camera,
            Math.Max(1, (int)_gameViewScreenSize.X),
            Math.Max(1, (int)_gameViewScreenSize.Y),
            (int)_gameViewScreenPosition.X,
            Math.Max(0, window.Height - (int)(_gameViewScreenPosition.Y + _gameViewScreenSize.Y)),
            Math.Max(1, (int)_gameViewScreenSize.X),
            Math.Max(1, (int)_gameViewScreenSize.Y));
        gizmos.Clear();
    }

    private static Camera? FindActiveCamera(Vecxy.Scene.SceneInstance? scene)
    {
        if (scene is null)
            return null;

        return scene.Objects
            .Where(sceneObject => sceneObject.IsActive)
            .Select(sceneObject => sceneObject.GetComponent<Camera>())
            .Where(camera => camera is { IsActive: true })
            .OrderByDescending(camera => camera!.Priority)
            .FirstOrDefault();
    }

    private sealed class EditorGizmoDrawer(
        GizmoRenderer gizmoRenderer) : IEditorGizmoDrawer
    {
        public void Line(
            Vector3 from,
            Vector3 to,
            Vector4 color,
            float thickness = 1.0f)
        {
            gizmoRenderer.AddLine(from, to, color, thickness);
        }

        public void WireBox(
            Matrix4x4 transform,
            Vector3 size,
            Vector4 color,
            float thickness = 1.0f)
        {
            var half = size * 0.5f;
            Span<Vector3> corners =
            [
                new(-half.X, -half.Y, -half.Z),
                new(half.X, -half.Y, -half.Z),
                new(half.X, half.Y, -half.Z),
                new(-half.X, half.Y, -half.Z),
                new(-half.X, -half.Y, half.Z),
                new(half.X, -half.Y, half.Z),
                new(half.X, half.Y, half.Z),
                new(-half.X, half.Y, half.Z)
            ];

            for (var index = 0; index < corners.Length; index++)
                corners[index] = Vector3.Transform(corners[index], transform);

            DrawEdge(corners[0], corners[1], color, thickness);
            DrawEdge(corners[1], corners[2], color, thickness);
            DrawEdge(corners[2], corners[3], color, thickness);
            DrawEdge(corners[3], corners[0], color, thickness);
            DrawEdge(corners[4], corners[5], color, thickness);
            DrawEdge(corners[5], corners[6], color, thickness);
            DrawEdge(corners[6], corners[7], color, thickness);
            DrawEdge(corners[7], corners[4], color, thickness);
            DrawEdge(corners[0], corners[4], color, thickness);
            DrawEdge(corners[1], corners[5], color, thickness);
            DrawEdge(corners[2], corners[6], color, thickness);
            DrawEdge(corners[3], corners[7], color, thickness);
        }

        public void WireSphere(
            Vector3 center,
            float radius,
            Vector4 color,
            int segments = 24,
            float thickness = 1.0f)
        {
            segments = Math.Max(3, segments);
            DrawCircle(center, radius, Vector3.UnitX, Vector3.UnitY, color, segments, thickness);
            DrawCircle(center, radius, Vector3.UnitX, Vector3.UnitZ, color, segments, thickness);
            DrawCircle(center, radius, Vector3.UnitY, Vector3.UnitZ, color, segments, thickness);
        }

        public void Axes(
            Matrix4x4 transform,
            float size = 1.0f,
            float thickness = 1.0f)
        {
            var origin = Vector3.Transform(Vector3.Zero, transform);
            var x = Vector3.TransformNormal(Vector3.UnitX, transform);
            var y = Vector3.TransformNormal(Vector3.UnitY, transform);
            var z = Vector3.TransformNormal(-Vector3.UnitZ, transform);

            DrawEdge(origin, origin + Vector3.Normalize(x) * size, new Vector4(1, 0, 0, 1), thickness);
            DrawEdge(origin, origin + Vector3.Normalize(y) * size, new Vector4(0, 1, 0, 1), thickness);
            DrawEdge(origin, origin + Vector3.Normalize(z) * size, new Vector4(0, 0.7f, 1, 1), thickness);
        }

        private void DrawCircle(
            Vector3 center,
            float radius,
            Vector3 axisA,
            Vector3 axisB,
            Vector4 color,
            int segments,
            float thickness)
        {
            var previous = center + axisA * radius;

            for (var index = 1; index <= segments; index++)
            {
                var angle = MathF.Tau * index / segments;
                var point =
                    center +
                    (axisA * MathF.Cos(angle) + axisB * MathF.Sin(angle)) * radius;
                DrawEdge(previous, point, color, thickness);
                previous = point;
            }
        }

        private void DrawEdge(
            Vector3 from,
            Vector3 to,
            Vector4 color,
            float thickness)
        {
            gizmoRenderer.AddLine(from, to, color, thickness);
        }
    }

    private readonly record struct MeshOption(
        string Label,
        Mesh Mesh);

    private readonly record struct GameViewPreset(
        string Label,
        int Width,
        int Height);

    private readonly record struct GameViewViewport(
        Vector2 Offset,
        Vector2 Size);

    private static class ImGuiDockBuilderNative
    {
        public const ImGuiDockNodeFlags DockSpaceNodeFlag =
            (ImGuiDockNodeFlags)(1 << 10);

        [StructLayout(LayoutKind.Sequential)]
        private struct ImVec2Native
        {
            public float X;
            public float Y;

            public ImVec2Native(Vector2 value)
            {
                X = value.X;
                Y = value.Y;
            }
        }

        [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl, EntryPoint = "igDockBuilderRemoveNode")]
        private static extern void DockBuilderRemoveNode(uint nodeId);

        [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl, EntryPoint = "igDockBuilderAddNode")]
        private static extern uint DockBuilderAddNode(uint nodeId, ImGuiDockNodeFlags flags);

        [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl, EntryPoint = "igDockBuilderSetNodeSize")]
        private static extern void DockBuilderSetNodeSize(uint nodeId, ImVec2Native size);

        [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl, EntryPoint = "igDockBuilderSplitNode")]
        private static extern unsafe uint DockBuilderSplitNode(
            uint nodeId,
            ImGuiDir splitDir,
            float sizeRatioForNodeAtDir,
            uint* outIdAtDir,
            uint* outIdAtOppositeDir);

        [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl, EntryPoint = "igDockBuilderDockWindow")]
        private static extern void DockBuilderDockWindow(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string windowName,
            uint nodeId);

        [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl, EntryPoint = "igDockBuilderFinish")]
        private static extern void DockBuilderFinish(uint nodeId);

        public static void RemoveNode(uint nodeId) =>
            DockBuilderRemoveNode(nodeId);

        public static uint AddNode(uint nodeId, ImGuiDockNodeFlags flags) =>
            DockBuilderAddNode(nodeId, flags);

        public static void SetNodeSize(uint nodeId, Vector2 size) =>
            DockBuilderSetNodeSize(nodeId, new ImVec2Native(size));

        public static unsafe uint SplitNode(
            uint nodeId,
            ImGuiDir splitDir,
            float sizeRatioForNodeAtDir,
            uint* outIdAtDir,
            uint* outIdAtOppositeDir) =>
            DockBuilderSplitNode(
                nodeId,
                splitDir,
                sizeRatioForNodeAtDir,
                outIdAtDir,
                outIdAtOppositeDir);

        public static void DockWindow(
            string windowName,
            uint nodeId) =>
            DockBuilderDockWindow(windowName, nodeId);

        public static void Finish(uint nodeId) =>
            DockBuilderFinish(nodeId);
    }

    private sealed class WindowEntry(
        string name,
        Action draw)
    {
        public string Name { get; } = name;
        public Action Draw { get; } = draw;
        public bool Visible { get; set; }
    }
}
