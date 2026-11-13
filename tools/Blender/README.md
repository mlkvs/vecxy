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

- Marking a selected hierarchy as a prototype moves its geometry into a source
  collection and leaves an instance in the same position.
- All instances use the source collection. **Edit shared model here** creates temporary
  linked mesh views at the selected instance’s exact placement and scale, keeping the
  current viewpoint. **Finish editing shared model** applies object transforms back to
  the source and removes the temporary views. Mesh edits are shared immediately.
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
[EditorComponent("Washing machine", Category = "Gameplay", Key = "machine")]
public sealed class LaundryMachine : AComponent
{
    public sealed class Prototype : APrototype<LaundryMachine, Prototype.Options>
    {
        public sealed class Options
        {
            [EditorField("Cycle duration", Min = 0.1, Unit = "s")]
            public float CycleSeconds { get; set; } = 1320;
        }
        // Instantiate and Configure implement the usual Vecxy prototype factory.
    }
}
```

Attributes belong to `Vecxy.Prototypes`. Enable `VecxyGenerateComponentCatalog` in
the game project and import `Vecxy.Prototypes/build/Vecxy.ComponentCatalog.targets`
(already imported by the USD targets). Override `VecxyComponentCatalog` for another
output path; the default is `Authoring/vecxy.components.json`.
After changing C#, build the game and press **Reload project components** in Blender.
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

Shader-graph overrides, arbitrary mesh-deformation overrides, rigid-body constraints
and skeletal animation are outside this version. Material editing on the source
collection updates all instances; per-instance tint does not copy geometry.

## Test

Install the OpenUSD tools, then run from the game repository:

```bash
blender --background --factory-startup --python Engine/Vecxy/tools/Blender/tests/test_addon.py
```

The test creates a fresh scene and checks operator behavior, fitting without helpers,
shared instances, external references, collider/tint overrides, field inheritance after
prototype-only export, override reset and prototype-edit lifecycle. All test artifacts
are stored under `.agents/local/`.

The real example and Russian workflow guide are `Horror/Authoring/Laundromat.blend`
and `Horror/Authoring/README.md`.
