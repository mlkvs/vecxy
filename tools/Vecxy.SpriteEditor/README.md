# Vecxy Sprite Editor

Native Vecxy tool for inspecting project images and editing `.atlas` sprite metadata.

```bash
dotnet run --project tools/Vecxy.SpriteEditor -- /path/to/game
```

Open a project (or its `Assets` directory), select an image, then create an atlas.
Slices can be selected on the canvas, dragged, nudged/resized in the Inspector, assigned
a center or bottom pivot, trimmed to non-transparent pixels, and saved beside the source
image. The saved format is compatible with `UiSpriteAtlasAsset`; pivot fields are retained
for sprite rendering and future importers.

Major views are separate XML components under `Assets/UI/Components`. Every component
has a matching stylesheet under `Assets/UI/Styles`; shared design tokens and controls are
defined in `Global.css`.
