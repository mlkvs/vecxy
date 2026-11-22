# Vecxy USD Tools for Blender

A Blender 5.2+ addon, verified in Blender 5.2. Sources: `vecxy_usd/`.
It gives objects collider/component properties, draws selected collider shapes,
creates collection prototypes, edits defaults and instance overrides, and exports
referenced USD assets from one `.blend`.

```bash
python3 Engine/Vecxy/tools/Blender/package_addon.py
```

The archive is written to `.agents/local/vecxy_usd.zip`. Install it using Blender's
**Preferences → Add-ons → Install from Disk**, enable **Vecxy USD Tools**, then open
**3D View → N → Vecxy**. Packaging excludes bytecode caches.

The exporter delegates geometry/material authoring to Blender's native USD exporter.
A separate Python with `usd-core` resolves external references, moves texture assets,
and writes components. Set **OpenUSD Python** in the sidebar, or use the repository's
`.agents/local/usd-env`. The Blender process does not need Python USD bindings.

## Authoring model

For character collections, animation clips and the facial atlas workflow, see
[Character face sprites](../../docs/face-sprites.md).

- Marking a selected hierarchy as a prototype moves its geometry into a source
  collection and leaves an instance in the same position.
- All instances use the source collection. **Edit original prototype** exposes that
  actual collection and enters Blender's native Local View (`/`). No object, mesh or
  collection is duplicated. The source keeps its own authoring scale. The active
  collection is the prototype, so added/deleted parts directly change the shared model.
  **Return to scene** restores visibility, library exclusion and instance selection.
- Default components, including colliders, belong to the source collection. Individual
  instances can override collider settings, role, tint and component fields.
- Components are keyed records. The instance stores only `vecxy:componentOverrides`;
  defaults remain in the external prototype asset. Updating a prototype alone changes
  non-overridden fields in every referencing scene after cooking.
- Component fields are converted to `Prototype.Options`, then created via
  `IPrototypes`. Only registered `AComponent` prototypes are accepted.
- Box, sphere and capsule colliders are ordinary typed components, drawn as GPU wire
  overlays; no collider helper mesh is created. Center/size are object-local; capsule
  axis is selectable. Fit to model updates their component fields.
- A prototype-only export leaves scene bytes untouched. Export Scene optionally
  exports prototypes first and then references each external `.usd/.usda/.usdc` file.
- Both scene and assets use Blender's native Z-up coordinates/metres. The runtime
  cooker converts to engine Y-up once. A spawn marker's local +Y is its forward.

All source objects stay in one `.blend`. Source collections are excluded as placed
geometry when exporting the scene, even while they are linked for editing. Ordinary
object transforms are independent for every instance. Differing component/tint/physics
properties do not require making the geometry independent.

Property editor schemas live in `__init__.py`, export orchestration in `exporter.py`,
and OpenUSD composition in `finalize_usd.py`. Gameplay-specific behavior is not hidden
inside the addon. Runtime schemas and supported limitations are documented in
[`Vecxy.Usd`](../../Code/Vecxy.Usd/README.md).

## Project component catalog

The addon reads `//vecxy.components.json` relative to the `.blend` on opening the file.
The game build generates this catalog from the compiled game and referenced engine
assemblies. No C# assemblies are executed inside Blender. Only explicitly annotated
components and fields are exposed:

```csharp
[Authoring("Washing machine", Category = "Gameplay", Key = "machine")]
public sealed class LaundryMachine : AComponent
{
    public sealed class Prototype : APrototype<LaundryMachine, Prototype.Options>
    {
        public sealed class Options
        {
            [Authoring("Cycle duration", Min = 0.1, Unit = "s")]
            public float CycleSeconds { get; set; } = 1320;
        }
        // Instantiate and Configure implement the usual Vecxy prototype factory.
    }
}
```

The universal `Authoring` attribute belongs to `Vecxy.Prototypes` and is used on
both component classes and `Prototype.Options` properties. A label is optional.
Every standard Vecxy desktop project generates the catalog through the shared asset
pipeline. No USD-specific import or generator executable is needed.

Run `vecxy generate` (or `vecxy assets generate`) for the complete cycle: asset GUIDs,
`Assets.g.cs`, fresh project compilation and authoring metadata. `dotnet build` runs
the same stages automatically. The default output is `Authoring/vecxy.components.json`;
set `VecxyAuthoringCatalog` in the project to change it. Metadata is finalized after
compilation, so source changes are never scanned from a previous build.
The addon automatically reloads a changed catalog while Blender stays open;
**Reload project components** also allows a manual refresh.
Pick an item from the component dropdown, then **Add selected component**. Physics
and game types appear together under their categories. Fields are generated with
the declared labels, defaults, descriptions, units and ranges. Supported types are
float/double, int, bool, string, Vector3 and enums; unsupported annotated fields fail
the catalog build explicitly. Numeric limits are validated at export.

**Common settings (all copies)** edits prototype defaults. **Override** exposes an
inherited component locally; each field checkbox independently enables an override.
Unchecked fields display the current inherited value. Disabling an inherited component
locally suppresses it for that instance. Removing the local record restores inheritance.
`Horror/LaundryMachine.cs` is a complete factory example.

The USD scene workflow covers static geometry and components. Character collections
export skeletal animation separately as GLB. Material editing on the source
collection updates all instances; per-instance tint does not copy geometry.

## Test

Install the OpenUSD tools, then run from the game repository:

```bash
blender --background --factory-startup --python Engine/Vecxy/tools/Blender/tests/test_addon.py
```

The test creates a fresh scene and checks operator behavior, fitting without helpers,
shared instances, external references, collider/tint overrides, field inheritance after
prototype-only export, override reset and original-source isolation/visibility restoration. All test artifacts
are stored under `.agents/local/`.

The real example and Russian workflow guide are `Horror/Authoring/Sandbox.blend`
and `Horror/Authoring/README.md`.

## UI document fields

`[Authoring("Document", AssetType = "UiDocument")]` exposes a searchable XML asset
picker. Choices come from the shared generation pipeline and Assets.manifest. The
UI displays paths while the component stores the stable AssetId; asset renames keep
the reference. Run normal generation after adding/renaming assets.
`UI / UI Canvas` can be attached to a plane: choose a document and WorldSpace. The
plane defines bounds/orientation and stays visible as an authoring region, while its
renderer is suppressed in the game. The XML/CSS document is the same as for Overlay.

`Pointer mode` selects normal mouse, centre-camera `ViewRay`, or `SurfaceCursor`.
`Activation key` defaults to E in addition to primary click. For SurfaceCursor,
`Cursor image` uses the same generic asset picker (`AssetType = "Texture2D"`);
choose **None** to clear it. Cursor size is in logical UI pixels, hotspot is top-left.
The two examples in the Horror scene demonstrate both modes and custom cursors.


## Level layout (addon 1.4)

**N → Vecxy → Расстановка уровня** lists every shared prototype in this `.blend`,
including its unlinked source collection. The search field above the list filters by asset or
collection name. Click the plus next to an item, or select it and use **Расставить
мышью**. The mesh follows the mouse, and its geometric support face lands on the
surface under the cursor; the collection empty's origin is irrelevant. The 3D cursor's
Z defines a fallback plane when there is no surface under the ray.

- Left click stamps linked copies; R / Shift+R rotates by the configured angle.
- G toggles the grid, M the geometry-bounds magnet, A surface orientation.
- Hold Shift to temporarily bypass grid/magnet. Middle mouse and wheel navigate.
- Enter / Esc ends a stamping session and removes the pending preview; stamped copies
  remain. Undo reverts the session as one operation. When moving an existing instance,
  Esc restores its previous transform, while click or Enter accepts the new position.
- **Копии мышью** preserves the selected instance's scale and local component/tint
  overrides. **Переставить** positions the existing instance without creating a copy.
- **Добавить у 3D-курсора** aligns the bottom center of the geometry with the cursor.
- **Создать ряд / сетку копий** creates linked arrays along local or world axes. Counts
  include the originals; geometry size plus gap determines spacing, or set a manual
  step. Multiple selected instances are repeated as one group (maximum 1000 new
  instances per operation).
- **Состыковать с активным** moves the other selected objects as a group against the
  chosen positive/negative face of the active object. Bounds are measured in the
  active object's orientation when local axes are enabled. The reference stays still.

The library retains the scale used when making a prototype; older files infer it
from an existing instance. Source pivots and canonical geometry stay unchanged.
The magnet uses geometry bounds, includes collection instance offsets, nested
prototypes, evaluated mesh bounds, instance rotation and scale. It is intended for
modular pieces; it does not weld vertices or change source pivots, collider data,
shared mesh data, material assignments, or USD reference semantics. Surface alignment
is optional. The library is local to the current `.blend`, not an external asset browser.
Saving/loading/disabling the addon cancels pending previews so they are not serialized.

Regression checks:

```bash
blender --background --factory-startup --python tools/Blender/tests/test_level_design.py
```


Collider fixes in 1.4.1: **Show collider** draws selected outlines in Object and mesh
Edit Mode. **Colliders through geometry** is enabled by default; turn it off for
normal depth occlusion. Add explicitly resets its operator action after Remove,
a newly added collider is fitted to the current object/prototype geometry, and
repeated Add preserves the values of an existing component. Regression check:
`VECXY_TEST_CATALOG=/path/to/vecxy.components.json blender --background --factory-startup --python tools/Blender/tests/test_colliders.py`.


Startup fix in 1.4.2: abandoned placement-preview cleanup is deferred until Blender
permits scene access. Blender's normal addon loader uses RestrictBlend during
register(); accessing bpy.data.objects there previously stopped registration before
the collider draw callback was installed, leaving the sidebar visible without outlines.
`blender --background --factory-startup --python tools/Blender/tests/test_registration.py`
checks the real addon_utils enable/disable/re-enable path, not a direct register() call.
After upgrading from a partially registered 1.4/1.4.1 session, save the file and fully
restart Blender.


Placement-mode fix in 1.4.3: library placement, the library-row plus, copy stamping
and Move explicitly set all operator action properties. Those transient properties
use SKIP_SAVE so Blender cannot reuse Move as a later library-placement action.
`test_placement_modes.py` checks every button with stale cached properties;
`test_placement_lifecycle.py` is a graphical native-input regression fixture driven
through move-confirm, move-cancel and library/copy placement sessions.


USD texture fixes in 1.4.4: relocation snapshots asset paths before changing any USD
properties. Composed material references can alias the same underlying property;
re-reading them after rewriting a path previously looked for an output texture in
the native staging directory. Export no longer fails on these shared materials.

Missing-file errors now include the export destination, searched absolute path, USD
property/layer, Blender material, image node, original image path and object users.
The sidebar offers **Подробности ошибки экспорта** after a failure; the complete,
copyable report is in **Text Editor → Vecxy Export Report**. Error text is preserved,
and the OpenUSD/Pillow setup hint is shown only for a missing Python dependency.
Unused image datablocks are not treated as required assets: relocation follows the
asset attributes actually emitted by the native USD exporter.

Regression checks: run `tests/test_finalize_usd.py` with Python containing usd-core;
run `tests/test_export_reports.py` with Blender in background/factory mode. Both take
`VECXY_TEST_OUTPUT` pointing to a disposable test directory.
