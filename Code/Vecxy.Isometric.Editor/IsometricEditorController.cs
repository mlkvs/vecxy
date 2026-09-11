using System.Collections.Concurrent;
using System.Globalization;
using StbImageSharp;
using Vecxy.Assets;
using Vecxy.Input;
using Vecxy.Kernel;
using Vecxy.Prototypes;
using Vecxy.Rendering;
using Vecxy.UI;

namespace Vecxy.Isometric.Editor;

public sealed class IsometricEditorController(
    AssetWorkspaceService workspace,
    EditorFileDialog dialog,
    EditorUiFactory elements,
    IPrototypes prototypes,
    ITextureResolver textures,
    IWindow window,
    IUiManager uiManager) : IDisposable
{
    private enum ETool { Select, Place, Erase }

    private UiDocument? _ui;
    private readonly Dictionary<string, (DateTime Modified, TextureAsset Asset, Texture Texture)> _textureCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<IWindow.KeyEvent> _keys = new();
    private UiPanel? _assetList, _worldList, _gridLayer, _placementLayer, _previewLayer, _texturePreview;
    private UiText? _documentTitle, _workspaceTitle, _status, _stats, _levelLabel, _cursorLabel, _selectionType;
    private UiInputField? _search, _name, _category, _texture, _color, _slots, _width, _height, _length;
    private UiInputField? _instanceX, _instanceY, _instanceLevel, _instanceParent, _instanceSlot;
    private UiElement? _blocking, _hiddenContents;
    private WorldDocument? _document;
    private IsometricAssetDefinition? _selectedDefinition;
    private Guid? _selectedInstance;
    private Guid? _attachmentParent;
    private string? _attachmentSlot;
    private ETool _tool = ETool.Select;
    private int _level, _rotation;
    private (int X, int Y)? _hover;
    private bool _started;

    public void Start() => window.KeyChanged += OnKey;
    public void Dispose() => window.KeyChanged -= OnKey;

    public void Bind(UiDocument document)
    {
        _ui = document;
        elements.Bind(document);
        document.Instantiate("Components/TopBar.xml", Get<UiPanel>("top-slot"));
        var content = Get<UiPanel>("main-content");
        document.Instantiate("Components/AssetBrowser.xml", content);
        document.Instantiate("Components/Viewport.xml", content);
        document.Instantiate("Components/Inspector.xml", content);
        document.Instantiate("Components/StatusBar.xml", Get<UiPanel>("status-slot"));
        ResolveElements();
        BindCommands();
        BuildGrid();

        if (!_started)
        {
            _started = true;
            var argument = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(Directory.Exists);
            if (argument is not null) OpenWorkspace(argument);
        }
        RefreshAll();
    }

    public void Unbind()
    {
        _ui = null;
        _assetList = _worldList = _gridLayer = _placementLayer = _previewLayer = null;
    }

    public void Update()
    {
        while (_keys.TryDequeue(out var input))
        {
            if (!input.IsPressed) continue;
            var key = (EKeyboardKey)input.Key;
            var primary = (input.Modifiers & KeyModifiers.Primary) != 0;
            if (uiManager.FocusedElement is UiInputField)
            {
                if (primary && key == EKeyboardKey.S) SaveWorld();
                continue;
            }
            if (primary && key == EKeyboardKey.S) SaveWorld();
            else if (primary && key == EKeyboardKey.Z) Undo();
            else if (primary && key == EKeyboardKey.Y) Redo();
            else if (key == EKeyboardKey.Delete) DeleteSelection();
            else if (key == EKeyboardKey.R) Rotate(1);
            else if (key == EKeyboardKey.V) SetTool(ETool.Select);
            else if (key == EKeyboardKey.P) SetTool(ETool.Place);
            else if (key == EKeyboardKey.E) SetTool(ETool.Erase);
            else if (key is EKeyboardKey.Left or EKeyboardKey.Right or EKeyboardKey.Up or EKeyboardKey.Down) Nudge(key);
        }
    }

    private void ResolveElements()
    {
        _assetList = Get<UiPanel>("asset-list"); _worldList = Get<UiPanel>("world-list");
        _gridLayer = Get<UiPanel>("grid-layer"); _placementLayer = Get<UiPanel>("placement-layer");
        _previewLayer = Get<UiPanel>("preview-layer");
        _texturePreview = Get<UiPanel>("texture-preview");
        _documentTitle = Get<UiText>("document-title"); _workspaceTitle = Get<UiText>("workspace-title");
        _status = Get<UiText>("status-text"); _stats = Get<UiText>("world-stats");
        _levelLabel = Get<UiText>("level-label"); _cursorLabel = Get<UiText>("cursor-label");
        _selectionType = Get<UiText>("selection-type");
        _search = Get<UiInputField>("asset-search"); _name = Get<UiInputField>("property-name");
        _category = Get<UiInputField>("property-category"); _texture = Get<UiInputField>("property-texture");
        _color = Get<UiInputField>("property-color"); _slots = Get<UiInputField>("property-slots");
        _width = Get<UiInputField>("property-width");
        _height = Get<UiInputField>("property-height"); _length = Get<UiInputField>("property-length");
        _instanceX = Get<UiInputField>("instance-x"); _instanceY = Get<UiInputField>("instance-y");
        _instanceLevel = Get<UiInputField>("instance-level"); _instanceParent = Get<UiInputField>("instance-parent");
        _instanceSlot = Get<UiInputField>("instance-slot");
        _blocking = Get<UiElement>("property-blocking"); _hiddenContents = Get<UiElement>("property-hidden-contents");
    }

    private void BindCommands()
    {
        Click("open-assets", ChooseWorkspace); Click("refresh-assets", RefreshWorkspace);
        Click("new-world", CreateWorld); Click("save-world", SaveWorld);
        Click("undo-world", Undo); Click("redo-world", Redo);
        Click("select-tool", () => SetTool(ETool.Select)); Click("place-tool", () => SetTool(ETool.Place));
        Click("erase-tool", () => SetTool(ETool.Erase));
        Click("level-down", () => SetLevel(_level - 1)); Click("level-up", () => SetLevel(_level + 1));
        Click("rotate-left", () => Rotate(-1)); Click("rotate-right", () => Rotate(1));
        Click("toggle-grid", ToggleGrid); Click("apply-properties", ApplyInspector);
        Click("choose-texture", ChooseTexture);
        Click("remember-parent", RememberParent); Click("clear-parent", ClearParent);
        Click("delete-selection", DeleteSelection);
        Click("create-surface", () => CreateDefinition(EEditorAssetKind.Surface));
        Click("create-wall", () => CreateDefinition(EEditorAssetKind.Wall));
        Click("create-object", () => CreateDefinition(EEditorAssetKind.Object));
        Click("create-opening", () => CreateDefinition(EEditorAssetKind.Opening));
        Click("create-connection", () => CreateDefinition(EEditorAssetKind.Connection));
        _search!.TextChanged += _ => RebuildAssets();
    }

    private void ChooseWorkspace()
    {
        if (dialog.OpenAssetsFolder() is { } path) OpenWorkspace(path);
    }

    private void OpenWorkspace(string path)
    {
        Try(() =>
        {
            workspace.Open(path);
            _document = null; _selectedDefinition = null; _selectedInstance = null;
            SetStatus($"Opened {workspace.AssetsDirectory}");
            RefreshAll();
        });
    }

    private void RefreshWorkspace() => Try(() => { workspace.Refresh(); RebuildAssets(); RebuildWorlds(); SetStatus("Assets refreshed"); });

    private void CreateWorld()
    {
        Try(() =>
        {
            _document = workspace.CreateWorld("New World");
            _selectedInstance = null;
            SetStatus("World created");
            RefreshAll();
        });
    }

    private void OpenWorld(string path) => Try(() =>
    {
        _document = workspace.OpenWorld(path); _level = 0; _selectedInstance = null;
        SetStatus($"Opened {workspace.Relative(path)}"); RefreshAll();
    });

    private void SaveWorld()
    {
        if (_document is null) { SetStatus("Create or open a world first", true); return; }
        Try(() => { workspace.SaveWorld(_document); SetStatus("World saved"); UpdateHeader(); });
    }

    private void CreateDefinition(EEditorAssetKind kind) => Try(() =>
    {
        _selectedDefinition = workspace.CreateDefinition(kind, $"New {kind}");
        _selectedInstance = null; SetTool(ETool.Place); RebuildAssets(); SyncInspector();
        SetStatus($"Created {_selectedDefinition.Name}");
    });

    private void SetTool(ETool tool)
    {
        _tool = tool;
        Get<UiButton>("select-tool").IsSelected = tool == ETool.Select;
        Get<UiButton>("place-tool").IsSelected = tool == ETool.Place;
        Get<UiButton>("erase-tool").IsSelected = tool == ETool.Erase;
        foreach (var pair in new[] { ("select-tool", ETool.Select), ("place-tool", ETool.Place), ("erase-tool", ETool.Erase) })
        {
            if (pair.Item2 == tool) Get<UiButton>(pair.Item1).AddClass("active");
            else Get<UiButton>(pair.Item1).RemoveClass("active");
        }
        RenderPreview();
    }

    private void SetLevel(int level) { _level = level; _levelLabel!.Value = $"Level {_level}"; RebuildPlacements(); RenderPreview(); }
    private void Rotate(int delta)
    {
        var placement = _document?.World.Placements.FirstOrDefault(p => p.InstanceId == _selectedInstance);
        if (_tool == ETool.Select && placement is not null)
        {
            _document!.Execute(_ => placement.Rotation = (placement.Rotation + delta + 4) % 4);
            _rotation = placement.Rotation;
            RebuildPlacements(); SyncInspector(); UpdateHeader();
        }
        else
        {
            _rotation = (_rotation + delta + 4) % 4;
            RenderPreview();
        }
        SetStatus($"Rotation {_rotation * 90}°");
    }
    private void ToggleGrid() { _gridLayer!.IsVisible = !_gridLayer.IsVisible; }

    private void BuildGrid()
    {
        _gridLayer!.Clear();
        for (var y = -12; y <= 12; y++)
        for (var x = -12; x <= 12; x++)
        {
            var cell = elements.Button(_gridLayer, string.Empty, "iso-grid-cell");
            Position(cell, x, y, 0);
            var captured = (X: x, Y: y);
            cell.PointerMoved += (_, _) => Hover(captured.X, captured.Y);
            cell.Clicked += _ => ClickCell(captured.X, captured.Y);
            cell.SetAttribute("title", $"X {x}, Y {y}");
        }
    }

    private void Hover(int x, int y)
    {
        if (_hover == (x, y)) return;
        _hover = (x, y); _cursorLabel!.Value = $"X {x}  Y {y}  Level {_level}"; RenderPreview();
    }

    private void ClickCell(int x, int y)
    {
        if (_document is null) { SetStatus("Create or open a world first", true); return; }
        if (_tool == ETool.Place) Place(x, y);
        else
        {
            var placement = PlacementAt(x, y);
            if (_tool == ETool.Erase && placement is not null)
            {
                _document.Execute(world => world.Placements.RemoveAll(item => item.InstanceId == placement.InstanceId));
                _selectedInstance = null; RebuildPlacements(); SyncInspector(); UpdateHeader();
            }
            else SelectPlacement(placement);
        }
    }

    private void Place(int x, int y)
    {
        if (_selectedDefinition is null) { SetStatus("Select a definition in the Assets panel", true); return; }
        if (!CanPlace(_selectedDefinition, x, y)) { SetStatus("Placement is blocked at this position", true); return; }
        var attachment = ResolveAttachment(_selectedDefinition, x, y);
        var placement = prototypes.Instantiate<EditorPlacement>(new EditorPrototypeContext(), new EditorPlacement.Prototype.Options
        {
            DefinitionId = _selectedDefinition.AssetId, Kind = _selectedDefinition.Kind,
            X = x, Y = y, Level = _level, Rotation = _rotation,
            ParentId = attachment.ParentId, SlotId = attachment.SlotId
        });
        _document!.Execute(world =>
        {
            if (placement.Kind == EEditorAssetKind.Surface)
                world.Placements.RemoveAll(item => item.Kind == EEditorAssetKind.Surface && item.X == x && item.Y == y && item.Level == _level);
            world.Placements.Add(placement);
        });
        _selectedInstance = placement.InstanceId;
        RebuildPlacements(); SyncInspector(); UpdateHeader();
    }

    private bool CanPlace(IsometricAssetDefinition definition, int x, int y)
    {
        if (_document is null) return false;
        if (definition.Kind == EEditorAssetKind.Surface) return true;
        if (definition.Kind == EEditorAssetKind.Opening)
            return _document.World.Placements.Any(p => p.Level == _level && p.Kind == EEditorAssetKind.Wall && PlacementCells(p).Contains((x, y))) &&
                   !_document.World.Placements.Any(p => p.Level == _level && p.Kind == EEditorAssetKind.Opening && PlacementCells(p).Contains((x, y)));
        if (definition.Kind == EEditorAssetKind.Object && ValidAttachmentParent() is { } parent)
        {
            var slot = FindDefinition(parent.DefinitionId)?.Slots.FirstOrDefault(candidate => candidate.Id == _attachmentSlot);
            return slot is not null && _document.World.Placements.Count(item =>
                item.ParentId == parent.InstanceId && item.SlotId == slot.Id) < slot.Capacity;
        }
        if (definition.Kind == EEditorAssetKind.Object && !definition.Blocking) return true;
        var desired = DefinitionCells(definition, x, y, _rotation).ToHashSet();
        return !_document.World.Placements
            .Where(p => p.Level == _level &&
                        (((p.Kind is EEditorAssetKind.Object or EEditorAssetKind.Connection) && FindDefinition(p.DefinitionId)?.Blocking != false) ||
                         p.Kind == definition.Kind && definition.Kind == EEditorAssetKind.Wall))
            .SelectMany(PlacementCells)
            .Any(desired.Contains);
    }

    private EditorPlacement? PlacementAt(int x, int y) => _document?.World.Placements
        .LastOrDefault(p => p.Level == _level && PlacementCells(p).Contains((x, y)));

    private void SelectPlacement(EditorPlacement? placement)
    {
        _selectedInstance = placement?.InstanceId;
        if (placement is not null) _selectedDefinition = FindDefinition(placement.DefinitionId);
        RebuildAssets(); RebuildPlacements(); SyncInspector();
    }

    private void RebuildPlacements()
    {
        if (_placementLayer is null) return;
        _placementLayer.Clear();
        if (_document is null) return;
        foreach (var placement in _document.World.Placements.Where(p => p.Level == _level))
        {
            if (IsHiddenContent(placement)) continue;
            var definition = FindDefinition(placement.DefinitionId);
            if (definition is null) continue;
            var texture = ResolveTexture(definition);
            var first = true;
            foreach (var cell in PlacementCells(placement))
            {
                var css = $"iso-placement {placement.Kind.ToString().ToLowerInvariant()}" +
                          (placement.InstanceId == _selectedInstance ? " selected" : "");
                var visual = elements.Button(_placementLayer, first ? definition.Name : string.Empty, css);
                visual.Style.Set("background-color", definition.Color);
                if (texture is not null) elements.Image(visual, texture, "iso-placement-texture");
                Position(visual, cell.X, cell.Y, placement.Level);
                visual.Clicked += _ => SelectPlacement(placement);
                first = false;
            }
        }
        UpdateStats();
    }

    private void RenderPreview()
    {
        if (_previewLayer is null) return;
        _previewLayer.Clear();
        if (_tool != ETool.Place || _selectedDefinition is null || _hover is not { } hover) return;
        var valid = CanPlace(_selectedDefinition, hover.X, hover.Y);
        foreach (var cell in DefinitionCells(_selectedDefinition, hover.X, hover.Y, _rotation))
        {
            var preview = elements.Panel(_previewLayer, valid ? "iso-preview-cell" : "iso-preview-cell invalid");
            Position(preview, cell.X, cell.Y, _level);
        }
    }

    private IEnumerable<(int X, int Y)> PlacementCells(EditorPlacement placement)
    {
        var definition = FindDefinition(placement.DefinitionId);
        return definition is null ? [(placement.X, placement.Y)] :
            DefinitionCells(definition, placement.X, placement.Y, placement.Rotation);
    }

    private static IEnumerable<(int X, int Y)> DefinitionCells(IsometricAssetDefinition definition, int anchorX, int anchorY, int rotation)
    {
        var width = definition.Kind == EEditorAssetKind.Wall ? Math.Max(1, definition.Length) : Math.Max(1, definition.Width);
        var height = definition.Kind == EEditorAssetKind.Wall ? 1 : Math.Max(1, definition.Height);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var offset = rotation switch { 1 => (-y, x), 2 => (-x, -y), 3 => (y, -x), _ => (x, y) };
            yield return (anchorX + offset.Item1, anchorY + offset.Item2);
        }
    }

    private void RebuildAssets()
    {
        if (_assetList is null) return;
        _assetList.Clear();
        var search = _search?.Text ?? string.Empty;
        foreach (var definition in workspace.Definitions.Where(d =>
                     d.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                     d.Category.Contains(search, StringComparison.OrdinalIgnoreCase)))
        {
            var selected = definition.AssetId == _selectedDefinition?.AssetId;
            var card = elements.Button(_assetList, string.Empty, selected ? "iso-asset-card selected" : "iso-asset-card");
            var icon = elements.Panel(card, "iso-asset-icon"); icon.Style.Set("background-color", definition.Color);
            if (ResolveTexture(definition) is { } texture) elements.Image(icon, texture, "iso-asset-texture");
            var copy = elements.Panel(card, "iso-asset-copy");
            elements.Text(copy, definition.Name, "iso-asset-name");
            elements.Text(copy, $"{definition.Kind} / {definition.Category}", "iso-asset-meta");
            card.Clicked += _ => { _selectedDefinition = definition; _selectedInstance = null; SetTool(ETool.Place); RebuildAssets(); SyncInspector(); RenderPreview(); };
        }
        UpdateStats();
    }

    private void RebuildWorlds()
    {
        if (_worldList is null) return;
        _worldList.Clear();
        foreach (var path in workspace.Worlds)
        {
            var button = elements.Button(_worldList, workspace.Relative(path));
            button.Clicked += _ => OpenWorld(path);
        }
    }

    private void SyncInspector()
    {
        var placement = _document?.World.Placements.FirstOrDefault(p => p.InstanceId == _selectedInstance);
        var definition = placement is null ? _selectedDefinition : FindDefinition(placement.DefinitionId);
        _selectionType!.Value = placement is not null ? $"Instance: {definition?.Name ?? "Missing definition"}" :
            definition is not null ? $"Definition: {definition.Kind}" : "Nothing selected";
        Set(_name, definition?.Name); Set(_category, definition?.Category); Set(_texture, definition?.Texture); Set(_color, definition?.Color);
        Set(_slots, definition is null ? null : string.Join(", ", definition.Slots.Select(slot => $"{slot.Id}:{slot.Capacity}")));
        _blocking!.IsChecked = definition?.Blocking == true;
        _hiddenContents!.IsChecked = definition?.HiddenContents == true;
        Set(_width, definition?.Width); Set(_height, definition?.Height); Set(_length, definition?.Length);
        Set(_instanceX, placement?.X); Set(_instanceY, placement?.Y); Set(_instanceLevel, placement?.Level);
        Set(_instanceParent, placement?.ParentId?.ToString()); Set(_instanceSlot, placement?.SlotId);
        _texturePreview!.Clear();
        if (definition is not null && ResolveTexture(definition) is { } texture)
            elements.Image(_texturePreview, texture, "iso-inspector-texture");
    }

    private void ChooseTexture()
    {
        if (_selectedDefinition is null) { SetStatus("Select or create a definition first", true); return; }
        if (workspace.AssetsDirectory is null) { SetStatus("Open an Assets folder first", true); return; }
        if (dialog.OpenTexture(workspace.AssetsDirectory) is not { } path) return;
        var full = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(workspace.AssetsDirectory, full);
        if (relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}"))
        {
            SetStatus("Choose a texture inside the opened Assets folder", true);
            return;
        }
        _selectedDefinition.Texture = relative.Replace('\\', '/');
        SyncInspector();
        RebuildAssets();
        SetStatus($"Selected texture {_selectedDefinition.Texture}; press Apply to save");
    }

    private void ApplyInspector()
    {
        var placement = _document?.World.Placements.FirstOrDefault(p => p.InstanceId == _selectedInstance);
        if (placement is not null)
        {
            _document!.Execute(_ =>
            {
                placement.X = Integer(_instanceX, placement.X); placement.Y = Integer(_instanceY, placement.Y);
                placement.Level = Integer(_instanceLevel, placement.Level);
                placement.ParentId = Guid.TryParse(_instanceParent!.Text, out var parent) ? parent : null;
                placement.SlotId = string.IsNullOrWhiteSpace(_instanceSlot!.Text) ? null : _instanceSlot.Text.Trim();
            });
            RebuildPlacements(); UpdateHeader(); SetStatus("Instance updated"); return;
        }
        if (_selectedDefinition is null) return;
        _selectedDefinition.Name = NonEmpty(_name!.Text, _selectedDefinition.Name);
        _selectedDefinition.Category = NonEmpty(_category!.Text, _selectedDefinition.Category);
        _selectedDefinition.Texture = _texture!.Text.Trim(); _selectedDefinition.Color = NonEmpty(_color!.Text, "#6b7280");
        _selectedDefinition.Slots = ParseSlots(_slots!.Text, _selectedDefinition.Slots);
        _selectedDefinition.Blocking = _blocking!.IsChecked;
        _selectedDefinition.HiddenContents = _hiddenContents!.IsChecked;
        _selectedDefinition.Width = Math.Max(1, Integer(_width, 1)); _selectedDefinition.Height = Math.Max(1, Integer(_height, 1));
        _selectedDefinition.Length = Math.Max(1, Integer(_length, 1));
        Try(() => { workspace.SaveDefinition(_selectedDefinition); workspace.Refresh(); _selectedDefinition = FindDefinition(_selectedDefinition.AssetId); RebuildAssets(); RebuildPlacements(); SetStatus("Definition saved"); });
    }

    private void DeleteSelection()
    {
        if (_selectedInstance is { } id && _document is not null)
        {
            _document.Execute(world => world.Placements.RemoveAll(p => p.InstanceId == id || p.ParentId == id));
            _selectedInstance = null; RebuildPlacements(); SyncInspector(); UpdateHeader(); return;
        }
        SetStatus("Select an instance to delete", true);
    }

    private void RememberParent()
    {
        var placement = _document?.World.Placements.FirstOrDefault(p => p.InstanceId == _selectedInstance);
        if (placement is null) { SetStatus("Select a placed object or wall first", true); return; }
        var definition = FindDefinition(placement.DefinitionId);
        var slot = definition?.Slots.FirstOrDefault();
        if (slot is null) { SetStatus("The selected definition has no attachment slots", true); return; }
        _attachmentParent = placement.InstanceId;
        _attachmentSlot = slot.Id;
        SetStatus($"Placement parent: {definition!.Name} / {slot.Id}. Select an asset and place it.");
    }

    private void ClearParent()
    {
        _attachmentParent = null;
        _attachmentSlot = null;
        SetStatus("Placement parent cleared");
    }

    private void Nudge(EKeyboardKey key)
    {
        var placement = _document?.World.Placements.FirstOrDefault(p => p.InstanceId == _selectedInstance);
        if (placement is null) return;
        _document!.Execute(_ =>
        {
            if (key == EKeyboardKey.Left) placement.X--;
            if (key == EKeyboardKey.Right) placement.X++;
            if (key == EKeyboardKey.Up) placement.Y--;
            if (key == EKeyboardKey.Down) placement.Y++;
        });
        RebuildPlacements(); SyncInspector(); UpdateHeader();
    }

    private void OnKey(IWindow.KeyEvent input) => _keys.Enqueue(input);

    private EditorPlacement? ValidAttachmentParent() => _document?.World.Placements.FirstOrDefault(p =>
        p.InstanceId == _attachmentParent && p.Level == _level);

    private bool IsHiddenContent(EditorPlacement placement)
    {
        if (placement.ParentId is not { } parentId || _document is null) return false;
        var parent = _document.World.Placements.FirstOrDefault(item => item.InstanceId == parentId);
        if (parent is null) return false;
        var definition = FindDefinition(parent.DefinitionId);
        var slot = definition?.Slots.FirstOrDefault(candidate => candidate.Id == placement.SlotId);
        return definition?.HiddenContents == true && slot?.Type == "Container";
    }

    private (Guid? ParentId, string? SlotId) ResolveAttachment(IsometricAssetDefinition definition, int x, int y)
    {
        if (definition.Kind == EEditorAssetKind.Opening)
        {
            var wall = _document?.World.Placements.LastOrDefault(p =>
                p.Level == _level && p.Kind == EEditorAssetKind.Wall && PlacementCells(p).Contains((x, y)));
            return (wall?.InstanceId, wall is null ? null : "opening");
        }
        var parent = definition.Kind == EEditorAssetKind.Object ? ValidAttachmentParent() : null;
        return parent is null ? (null, null) : (parent.InstanceId, _attachmentSlot);
    }

    private void Undo() { if (_document?.Undo() == true) { _selectedInstance = null; RebuildPlacements(); SyncInspector(); UpdateHeader(); } }
    private void Redo() { if (_document?.Redo() == true) { _selectedInstance = null; RebuildPlacements(); SyncInspector(); UpdateHeader(); } }

    private void RefreshAll() { RebuildWorlds(); RebuildAssets(); RebuildPlacements(); SyncInspector(); UpdateHeader(); SetLevel(_level); }
    private void UpdateHeader()
    {
        _documentTitle!.Value = _document is null ? "No world" : _document.World.Name + (_document.IsDirty ? " *" : "");
        _workspaceTitle!.Value = workspace.AssetsDirectory ?? "Open an Assets folder";
        UpdateStats();
    }
    private void UpdateStats()
    {
        if (_document is null)
        {
            _stats!.Value = $"{workspace.Definitions.Count} assets | 0 instances";
            return;
        }
        var world = _document.World;
        var config = new WorldGridConfig(world.ChunkWidth, world.ChunkHeight, world.RegionWidth, world.RegionHeight);
        var addresses = world.Placements.SelectMany(PlacementCells)
            .Select(cell => config.GetAddress(new TileCoord(cell.X, cell.Y, 0))).ToArray();
        var chunks = addresses.Select(address => address.Chunk).Distinct().Count();
        var regions = addresses.Select(address => address.Region).Distinct().Count();
        var levels = world.Placements.Select(placement => placement.Level).Distinct().Count();
        _stats!.Value = $"{workspace.Definitions.Count} assets | {world.Placements.Count} instances | {levels} levels | {chunks} chunks | {regions} regions";
    }
    private IsometricAssetDefinition? FindDefinition(Guid id) => workspace.Definitions.FirstOrDefault(d => d.AssetId == id);
    private Texture? ResolveTexture(IsometricAssetDefinition definition)
    {
        if (workspace.AssetsDirectory is null || string.IsNullOrWhiteSpace(definition.Texture)) return null;
        try
        {
            var path = Path.GetFullPath(Path.Combine(workspace.AssetsDirectory, definition.Texture.Replace('/', Path.DirectorySeparatorChar)));
            var relative = Path.GetRelativePath(workspace.AssetsDirectory, path);
            if (relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}") || !File.Exists(path)) return null;
            var modified = File.GetLastWriteTimeUtc(path);
            if (_textureCache.TryGetValue(path, out var cached) && cached.Modified == modified) return cached.Texture;
            using var stream = File.OpenRead(path);
            var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            var asset = TextureAsset.FromRgba(image.Width, image.Height, image.Data);
            var texture = textures.Resolve(asset);
            _textureCache[path] = (modified, asset, texture);
            return texture;
        }
        catch
        {
            return null;
        }
    }
    private static List<EditorSlotDefinition> ParseSlots(string value, List<EditorSlotDefinition> fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];
        var result = new List<EditorSlotDefinition>();
        foreach (var entry in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = entry.Split(':', 2, StringSplitOptions.TrimEntries);
            if (parts[0].Length == 0) continue;
            var previous = fallback.FirstOrDefault(slot => slot.Id.Equals(parts[0], StringComparison.OrdinalIgnoreCase));
            result.Add(new EditorSlotDefinition
            {
                Id = parts[0],
                Type = previous?.Type ?? "SupportSurface",
                Capacity = parts.Length == 2 && int.TryParse(parts[1], out var capacity) ? Math.Max(1, capacity) : previous?.Capacity ?? 1
            });
        }
        return result;
    }
    private T Get<T>(string id) where T : UiElement => _ui!.GetElementById<T>(id);
    private void Click(string id, Action action) => Get<UiButton>(id).Clicked += _ => action();
    private void SetStatus(string value, bool error = false) { _status!.Value = value; _status.Style.Set("color", error ? "#fca5a5" : "#c9cbd0"); }
    private void Try(Action action) { try { action(); } catch (Exception exception) { SetStatus(exception.Message, true); } }

    private static void Position(UiElement element, int x, int y, int level)
    {
        element.Style.Set("left", $"{850 + (x - y) * 31}ui");
        element.Style.Set("top", $"{190 + (x + y) * 15 - level * 48}ui");
    }
    private static void Set(UiInputField? input, object? value) => input!.Text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    private static int Integer(UiInputField? input, int fallback) => int.TryParse(input?.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    private static string NonEmpty(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
