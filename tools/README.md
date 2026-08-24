# Vecxy asset pipeline

Полное руководство на русском: [Asset Pipeline](../../../Docs/AssetPipeline.md).

From a game repository containing Vecxy at `Engine/Vecxy`, run:

```sh
./vecxy.sh assets scan
./vecxy.sh assets generate
./vecxy.sh assets validate
./vecxy.sh assets build
./vecxy.sh build
```

Windows uses the equivalent `vecxy.cmd`. Commands resolve `Assets/`,
`Assets.manifest`, `Generated/Assets.g.cs`, and `obj/` from the game project, never
from the engine checkout. The manifest owns stable GUIDs; a rename is reconciled by
content hash. Generated code uses typed handles, while the runtime resolves those IDs
to paths when the manifest is loaded.

Both `assets build` and `build` run scan, code generation, reference analysis,
validation, and `dotnet build`. Every file under `Assets/` is represented; known
formats receive typed handles and unknown/custom formats receive `AssetHandle`.

In a repository containing several games, select the project explicitly:

```powershell
.\vecxy.cmd --project HardCore.Cultivation assets generate
.\vecxy.cmd --project Sponza assets build
```

Each project receives its own `Assets.manifest`, `Generated/Assets.g.cs`, and
`obj/vecxy.asset.references.json`. When running from a directory containing exactly
one `.csproj`, `--project` can be omitted.

To collect references during every normal MSBuild compilation, add the analyzer:

```xml
<ProjectReference Include="Engine/Vecxy/tools/Vecxy.AssetAnalyzer/Vecxy.AssetAnalyzer.csproj"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

The CLI `build` command also performs reference analysis itself before validation, so
the wrapper works without globally installing a tool or modifying the game project.
