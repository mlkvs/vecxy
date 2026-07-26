using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using Autofac;
using ImGuiNET;
using Vecxy.Assets;
using Vecxy.Diagnostics;
using Vecxy.Kernel;
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
    ImGuiRenderer imgui,
    GizmoRenderer gizmos) :
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
        }
    }

    private readonly List<WindowEntry> _windowCallbacks = [];
    private readonly List<Action<IEditorGizmoDrawer>> _gizmoCallbacks = [];
    private readonly List<Action> _pendingEditorActions = [];
    private bool _initialized;
    private bool _overlayVisible;
    private bool _layoutResetPending;
    private int _layoutResetFramesRemaining;
    private int _lastWindowWidth;
    private int _lastWindowHeight;
    private float _customWindowsBaseX = 16.0f;
    private float _customWindowsBaseY = 16.0f;
    private float _customWindowsMaxY = 16.0f;
    private SceneObject? _selectedSceneObject;
    private Vecxy.Scene.Scene? _selectedScene;
    private IConfigRef? _selectedConfig;
    private int _selectedConfigVersion = -1;
    private object? _selectedConfigValue;
    private bool _showStatisticsWindow;
    private bool _showHierarchyWindow;
    private bool _showInspectorWindow;
    private bool _showConfigsWindow;
    private bool _showRenderSettingsWindow;
    private bool _gizmosEnabled = true;
    private string _componentSearch = string.Empty;
    private readonly Dictionary<string, AssetRef<Model>> _editorModelRefs =
        new(StringComparer.Ordinal);
    private static readonly string[] ModelExtensions = [".glb", ".gltf"];
    private static readonly string[] MaterialExtensions = [".material"];
    private static readonly string[] TextureExtensions =
        [".png", ".jpg", ".jpeg", ".bmp", ".tga", ".gif", ".webp"];

    public EGizmoDisplayMode GizmoDisplayMode
    {
        get => gizmos.DisplayMode;
        set => gizmos.DisplayMode = value;
    }

    public void OnInitialize()
    {
        if (_initialized)
            return;

        imgui.Initialize();
        gizmos.Initialize();
        window.Resized += OnWindowResized;
        window.KeyChanged += OnKeyChanged;
        _lastWindowWidth = Math.Max(1, window.Width);
        _lastWindowHeight = Math.Max(1, window.Height);
        overlays.RegisterOverlay(RenderOverlay);
        _initialized = true;
    }

    public void OnUpdate(float deltaTime)
    {
        if (!_initialized)
            return;

        var currentWidth = Math.Max(1, window.Width);
        var currentHeight = Math.Max(1, window.Height);

        if (currentWidth != _lastWindowWidth ||
            currentHeight != _lastWindowHeight)
        {
            RequestLayoutReset();
            _lastWindowWidth = currentWidth;
            _lastWindowHeight = currentHeight;
        }

        if (_overlayVisible)
            imgui.BeginFrame(deltaTime);
    }

    public void OnShutdown()
    {
        if (!_initialized)
            return;

        window.Resized -= OnWindowResized;
        window.KeyChanged -= OnKeyChanged;
        overlays.UnregisterOverlay(RenderOverlay);
        _windowCallbacks.Clear();
        _gizmoCallbacks.Clear();
        foreach (var model in _editorModelRefs.Values)
            model.Dispose();
        _editorModelRefs.Clear();
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
        RequestLayoutReset();
    }

    private void OnKeyChanged(IWindow.KeyEvent keyEvent)
    {
        if (!keyEvent.IsPressed)
            return;

        if (keyEvent.Key == (int)EKeyboardKey.F12)
            _overlayVisible = !_overlayVisible;
    }

    private void RequestLayoutReset()
    {
        _layoutResetPending = true;
        _layoutResetFramesRemaining = 8;
    }

    private void RestoreWindowLayout()
    {
        var viewportWidth = Math.Max(1, window.Width);
        var viewportHeight = Math.Max(1, window.Height);

        _customWindowsBaseX = MathF.Max(16.0f, viewportWidth - 420.0f);
        _customWindowsBaseY = 48.0f;
        _customWindowsMaxY = MathF.Max(48.0f, viewportHeight - 120.0f);
    }

    private void RenderOverlay()
    {
        if (!_overlayVisible)
            return;

        if (_layoutResetPending)
        {
            RestoreWindowLayout();
        }

        DrawMenuBar();

        if (_showStatisticsWindow)
            DrawStatisticsWindow();

        if (_showHierarchyWindow)
            DrawHierarchyWindow();

        if (_showInspectorWindow)
            DrawInspectorWindow();

        if (_showConfigsWindow)
            DrawConfigsWindow();

        if (_showRenderSettingsWindow)
            DrawRenderSettingsWindow();

        var customY = _customWindowsBaseY;
        foreach (var entry in _windowCallbacks.ToArray())
        {
            if (!entry.Visible)
                continue;

            if (_layoutResetPending)
            {
                ImGui.SetNextWindowPos(
                    new Vector2(_customWindowsBaseX, customY),
                    ImGuiCond.Always);

                customY += 140.0f;
                if (customY > _customWindowsMaxY)
                    customY = _customWindowsBaseY;
            }

            entry.Draw();
        }

        if (_layoutResetFramesRemaining > 0)
        {
            _layoutResetFramesRemaining--;
            _layoutResetPending = _layoutResetFramesRemaining > 0;
        }
        else
        {
            _layoutResetPending = false;
        }

        DrawGizmos();
        imgui.Render();
        FlushPendingEditorActions();
    }

    private void DrawMenuBar()
    {
        if (!ImGui.BeginMainMenuBar())
            return;

        if (ImGui.BeginMenu("Windows"))
        {
            ImGui.MenuItem("Statistics", string.Empty, ref _showStatisticsWindow);
            ImGui.MenuItem("Hierarchy", string.Empty, ref _showHierarchyWindow);
            ImGui.MenuItem("Inspector", string.Empty, ref _showInspectorWindow);
            ImGui.MenuItem("Configs", string.Empty, ref _showConfigsWindow);
            ImGui.MenuItem("Render Settings", string.Empty, ref _showRenderSettingsWindow);
            ImGui.Separator();

            foreach (var entry in _windowCallbacks)
            {
                var visible = entry.Visible;
                if (ImGui.MenuItem(entry.Name, string.Empty, visible))
                    entry.Visible = !visible;
            }

            ImGui.EndMenu();
        }

        ImGui.EndMainMenuBar();
    }

    private void DrawStatisticsWindow()
    {
        var statistics = renderer.Statistics;

        if (_layoutResetPending)
        {
            ImGui.SetNextWindowPos(
                new Vector2(16.0f, 16.0f),
                ImGuiCond.Always);
        }

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

    private void DrawHierarchyWindow()
    {
        if (_layoutResetPending)
        {
            ImGui.SetNextWindowPos(
                new Vector2(16.0f, 140.0f),
                ImGuiCond.Always);
        }

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

    private void DrawSceneNode(Vecxy.Scene.Scene scene)
    {
        var isActive = ReferenceEquals(scenes.ActiveScene, scene);
        var label = isActive
            ? $"● {scene.Name}##scene_{scene.GetHashCode()}"
            : $"○ {scene.Name}##scene_{scene.GetHashCode()}";

        var flags =
            ImGuiTreeNodeFlags.OpenOnArrow |
            ImGuiTreeNodeFlags.OpenOnDoubleClick |
            (_selectedScene == scene ? ImGuiTreeNodeFlags.Selected : ImGuiTreeNodeFlags.None) |
            (scene.RootObjects.Any()
                ? ImGuiTreeNodeFlags.None
                : ImGuiTreeNodeFlags.Leaf);

        var opened = ImGui.TreeNodeEx(label, flags);
        if (ImGui.IsItemClicked())
            SelectScene(scene);

        if (ImGui.BeginPopupContextItem($"scene_context_{scene.GetHashCode()}"))
        {
            if (ImGui.MenuItem("Create Empty"))
            {
                var sceneObject = scene.CreateObject("SceneObject");
                SelectSceneObject(sceneObject);
            }

            ImGui.EndPopup();
        }

        if (!opened)
            return;

        foreach (var root in scene.RootObjects)
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
        if (_layoutResetPending)
        {
            ImGui.SetNextWindowPos(
                new Vector2(MathF.Max(16.0f, window.Width - 420.0f), 16.0f),
                ImGuiCond.Always);
        }

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
            !ReferenceEquals(_selectedSceneObject.Scene, scenes.ActiveScene))
        {
            _selectedSceneObject = null;
            ImGui.TextDisabled("Nothing selected");
            ImGui.End();
            return;
        }

        DrawSceneObjectInspector(_selectedSceneObject);
        ImGui.End();
    }

    private void DrawSceneInspector(Vecxy.Scene.Scene scene)
    {
        ImGui.Text($"Scene: {scene.Name}");
        ImGui.Separator();

        if (ImGui.CollapsingHeader("Lighting", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawObjectProperties(scene.Lighting, "scene_lighting");
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
        if (_layoutResetPending)
        {
            ImGui.SetNextWindowPos(
                new Vector2(16.0f, 420.0f),
                ImGuiCond.Always);
        }

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
        if (_layoutResetPending)
        {
            ImGui.SetNextWindowPos(
                new Vector2(16.0f, 540.0f),
                ImGuiCond.Always);
        }

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
            DrawObjectProperties(_selectedConfigValue, $"config_{_selectedConfig.Path}");
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

        if (removable)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("Remove"))
            {
                QueueRemoveComponent(sceneObject, component);
                ImGui.PopID();
                return false;
            }
        }

        if (!open)
        {
            ImGui.PopID();
            return true;
        }

        var enabled = component.Enabled;
        if (ImGui.Checkbox("Enabled", ref enabled))
            component.Enabled = enabled;

        if (component is MeshRenderer meshRenderer)
        {
            DrawMeshRendererInspector(meshRenderer);
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
            var alreadyPresent = sceneObject.Components.Any(component => component.GetType() == componentType);
            if (alreadyPresent)
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
            var scene = sceneObject.Scene;
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
                property.Name is not nameof(AComponent.Scene) &&
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

    private static void DrawObjectProperties(
        object target,
        string prefix)
    {
        foreach (var property in GetInspectableProperties(target.GetType()))
            DrawProperty(target, property, prefix);
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

        if (target is Light &&
            property.Name is "Range" or "Intensity")
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

    private void SelectScene(Vecxy.Scene.Scene scene)
    {
        _selectedScene = scene;
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
        if (!_gizmosEnabled)
            return;

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
            Math.Max(1, window.Width),
            Math.Max(1, window.Height));
        gizmos.Clear();
    }

    private static Camera? FindActiveCamera(Vecxy.Scene.Scene? scene)
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

    private sealed class WindowEntry(
        string name,
        Action draw)
    {
        public string Name { get; } = name;
        public Action Draw { get; } = draw;
        public bool Visible { get; set; }
    }
}
