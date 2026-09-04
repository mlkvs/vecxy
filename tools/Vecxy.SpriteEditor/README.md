# Vecxy Sprite Editor

Native Vecxy tool for inspecting project images and editing `.atlas` sprite metadata.

```bash
dotnet run --project tools/Vecxy.SpriteEditor -- /path/to/game
```

Open a project (or its `Assets` directory), then open either a texture or an atlas. Opening
a texture automatically finds the same-name or linked atlas; opening an atlas resolves its
texture. The first edit creates a same-name atlas target, so a separate “new atlas” workflow
is not required.

The editor includes transparency- and grid-based auto slicing with a live preview, direct
slice creation, multi-selection, move/resize handles, canvas pivot editing, numeric Inspector
fields, trim, duplicate, undo/redo, recent files and explicit dirty/save state. The canvas
supports cursor-relative zoom, pan, a checkerboard, grid, and nearest-neighbor texture
sampling at 100–800%. Drag operations update only transient overlay geometry and commit one
undo action on release; they do not serialize the atlas or run full UI layout per pointer move.

Keyboard shortcuts include `Ctrl+O`, `Ctrl+S`, `Ctrl+Shift+S`, `Ctrl+Z`, `Ctrl+Y`,
`Ctrl+D`, `Ctrl+A`, arrows/Shift+arrows, `Delete`, `V`, `S`, `F`, `Ctrl+0`, `+` and `-`.
The saved format remains compatible with `UiSpriteAtlasAsset`.

Major views are separate XML components under `Assets/UI/Components`. Every component
has a matching stylesheet under `Assets/UI/Styles`; shared design tokens and controls are
defined in `Global.css`.
