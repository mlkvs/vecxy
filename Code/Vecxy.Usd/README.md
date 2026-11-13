# Vecxy.Usd

Opt-in OpenUSD scene import and `Vecxy.Prototypes` spawning. USD is the editable source;
the official OpenUSD Python SDK resolves composition offline. The runtime consumes a
versioned scene document and a GLB render cache, sharing mesh data between references.
No Python process, Blender installation or native USD library is needed by the game.

## Integration

Reference `Vecxy.Usd.csproj`, then register the module in the engine layer:

```csharp
layers.Add(new EngineLayer.Definition(
    new AssetsModule.Options { AssetsDirectory = context.AssetsDirectory },
    additionalModules: [new UsdModule.Definition()]));
```

Modules are constructed in the same scope as Prototypes, so constructor injection of
`IUsdSceneSystem` into `UsdScene.Prototype` uses the standard factory discovery.
`additionalModules` is optional and leaves existing EngineLayer callers unchanged.

Use a normal `.prototype` asset:

```yaml
format: 1
type: Vecxy.Usd.UsdScene
data:
  source: Scenes/MyScene.usda
  name: My scene
```

```csharp
var location = prototypes.Instantiate<UsdScene>(
    "Prototypes/MyScene.prototype",
    new ScenePrototypeContext { Scene = scene });
var spawn = location.Instance.RequireRole("spawn");
var door = location.Instance.RequireRole("entrance");
var machine = location.Instance.GetPrim("/MyScene/Washer_01");
```

Context parenting and transforms work on the prototype anchor; USD hierarchy stays
inside it. Failed prototype configuration rolls back its anchor and children.
USD native prototypes share visual geometry. Vecxy prototypes create logical scene
instances and gameplay components; they do not copy/reimplement USD reference syntax.

## Authoring and cooking

Install the offline tools in a virtual environment:

```bash
python3 -m venv .agents/local/usd-env
.agents/local/usd-env/bin/pip install -r Engine/Vecxy/Code/Vecxy.Usd/Tools/requirements.txt
.agents/local/usd-env/bin/python Engine/Vecxy/Code/Vecxy.Usd/Tools/cook_usd.py \
    MyGame/Assets/Scenes/MyScene.usda --assets MyGame/Assets
```

Outputs: `Scenes/Compiled/MyScene.glb` and `Scenes/MyScene.usda.usdscene`.
Keep both in version control so another developer can run an unchanged scene without
installing OpenUSD. The source, references, textures and outputs must be inside the
project's Assets folder and in the same startup package for VPack deployments.

An optional build import runs this step before asset preparation/packaging:

```xml
<PropertyGroup>
  <VecxyUsdStage>$(MSBuildProjectDirectory)/Assets/Scenes/MyScene.usda</VecxyUsdStage>
</PropertyGroup>
<Import Project="../Engine/Vecxy/Code/Vecxy.Usd/build/Vecxy.Usd.targets" />
```

Set `-p:VecxyUsdPython=/path/to/venv/bin/python` after source edits. On Windows point
it to `venv/Scripts/python.exe`. Default: `python3`. Unchanged cache validation uses
only the Python standard library. Every input layer/texture and output is hashed;
missing or changed references, textures, compiler code and corrupt outputs invalidate
the cache. `--check` fails if stale; `--force` rebuilds. Re-export, cook and restart the
scene after edits; live updating existing scene topology is not implemented.

## Supported static scene subset

- USDA, USDC, USD and USDZ through OpenUSD composition, sublayers and references.
- Native scenegraph instances and instance proxies. Repeated geometry uses identical
  GLB mesh indices, not a fresh vertex buffer for every scene node.
- Hierarchy, transform ops/reset stacks, negative scales, metre conversion, Y/Z up.
- Meshes, triangles/quads/simple planar concave ngons, indexed UVs, vertex/faceVarying
  normals, mesh material subsets. Smooth normals are preserved; missing normals use
  triangle normals. Holes are omitted. Export evaluated/triangulated meshes for best
  fidelity; subdivision is the authored control mesh, not a runtime subdivision surface.
- `UsdPreviewSurface`: diffuse color and `UsdUVTexture` color, constant metallic,
  roughness, opacity, emission. Textures are embedded in the cache (PNG conversion).
- Sphere/rectangle/disk lights as point lights, distant lights as directional lights.
  Area-light shapes are approximated by point lights in this renderer.
- `vecxy:role` string: named gameplay marker, e.g. `spawn`, `entrance`, `washer`.
- `vecxy:collider = "box"` plus `vecxy:colliderSize` float3: box on the same prim.
  Transform/scale belong to that prim. Collision cubes can instead use the standard
  `UsdPhysics.CollisionAPI`; a Cube's `size` is used as the unscaled box size.
  Guide-purpose collider shapes are imported for physics and omitted from rendering.
- `vecxy:range` float: optional attenuation range for lights, in engine metres.

This is a static scene pipeline, not a full USD renderer: skeletal/mesh animation,
PointInstancer, volumes, arbitrary MaterialX/Cycles networks and texture-driven scalar
PBR inputs are not supported. The cooker reports unsupported geometry/networks instead
of silently dropping them. Bake shader inputs or export Preview Surface constants.
Only diffuse `st` UVs are supported; animated/UDIM textures require baking first.
USD visibility is applied when cooking. Cameras and environment lighting remain game
configuration. Rain shader volumes in Horror remain a separate game effect.

## Verification

```bash
.agents/local/usd-env/bin/python Engine/Vecxy/Code/Vecxy.Usd/Tools/test_cook_usd.py
dotnet run --project Engine/Vecxy/Code/Vecxy.Usd.Tests -- Horror/Assets
```

Tests exercise reference edits/cache invalidation, shared meshes, binary/archive USD,
axis/unit conversion, concave triangulation, rejected malformed/animated geometry,
asset document validation and a real authored game scene.

Primary references: [OpenUSD instances](https://openusd.org/release/api/_usd__page__scenegraph_instancing.html),
[transform cache](https://openusd.org/release/api/class_usd_geom_xform_cache.html),
[material bindings](https://openusd.org/dev/api/class_usd_shade_material_binding_a_p_i.html),
[Blender USD](https://docs.blender.org/manual/en/latest/files/import_export/usd.html).
