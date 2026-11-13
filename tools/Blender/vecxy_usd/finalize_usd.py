"""Runs outside Blender with OpenUSD installed. Converts collection instances into
portable external USD references and authors object components without helper meshes.
"""
import hashlib
import json
import os
from pathlib import Path
import shutil
import sys
from pxr import Usd, UsdGeom, Sdf, Gf, Ar

TYPES={'collider':Sdf.ValueTypeNames.Token,'colliderAxis':Sdf.ValueTypeNames.Token,
       'colliderSize':Sdf.ValueTypeNames.Float3,'colliderCenter':Sdf.ValueTypeNames.Float3,
       'colliderRadius':Sdf.ValueTypeNames.Float,'colliderHeight':Sdf.ValueTypeNames.Float,
       'friction':Sdf.ValueTypeNames.Float,'restitution':Sdf.ValueTypeNames.Float,
       'trigger':Sdf.ValueTypeNames.Bool,'role':Sdf.ValueTypeNames.String,
       'components':Sdf.ValueTypeNames.String,'componentOverrides':Sdf.ValueTypeNames.String,'tint':Sdf.ValueTypeNames.Float4,'range':Sdf.ValueTypeNames.Float}


def apply_settings(prim,values):
    for key,value in values.items():
        if key not in TYPES:raise ValueError('Unknown Vecxy property '+key)
        if key in ('colliderSize','colliderCenter'):value=Gf.Vec3f(*value)
        if key=='tint':value=Gf.Vec4f(*value)
        prim.CreateAttribute('vecxy:'+key,TYPES[key],custom=True).Set(value)
    if values.get('role')=='spawn':prim.CreateAttribute('vecxy:markerBasis',Sdf.ValueTypeNames.Token,custom=True).Set('blender')


def run(manifest):
    source=Path(manifest['input']);output=Path(manifest['output']);output.parent.mkdir(parents=True,exist_ok=True)
    stage=Usd.Stage.Open(str(source));layer=stage.GetRootLayer()
    # Relocate texture assets from native export staging into stable per-asset storage.
    for prim in stage.Traverse():
        for attr in prim.GetAttributes():
            if attr.GetTypeName()!=Sdf.ValueTypeNames.Asset:continue
            value=attr.Get()
            if not value:continue
            resolved=value.resolvedPath or str((source.parent/value.path).resolve())
            if not Path(resolved).is_file():raise ValueError('Missing exported asset: '+str(value.path)+'; check image paths in Blender')
            payload=Path(resolved).read_bytes()
            digest=hashlib.sha256(payload).hexdigest()[:12]
            name=Path(value.path).name
            while name.startswith(digest+'_'):name=name[len(digest)+1:]
            target=output.parent/'Textures'/(digest+'_'+name)
            target.parent.mkdir(exist_ok=True)
            if not target.exists() or target.read_bytes()!=payload:target.write_bytes(payload)
            attr.Set(Sdf.AssetPath(Path(os.path.relpath(target,output.parent)).as_posix()))
    # Re-anchor the layer beside its final destination before resolving references.
    # This keeps external links relative and makes them resolvable during composition.
    import tempfile
    with tempfile.NamedTemporaryFile(suffix='.usda',prefix='.vecxy-',dir=output.parent,delete=False) as handle:
        temporary=Path(handle.name)
    layer.Export(str(temporary))
    stage=Usd.Stage.Open(str(temporary));layer=stage.GetRootLayer()
    # References must remain composition arcs; do not flatten the scene.
    tagged=[p.GetPath() for p in stage.Traverse() if p.GetAttribute('vecxy:id').HasValue()]
    for path in tagged:
        prim=stage.GetPrimAtPath(path)
        if not prim or prim.IsInstanceProxy():continue
        record=manifest['objects'].get(prim.GetAttribute('vecxy:id').Get())
        if not record:continue
        if 'prototype' in record:
            asset=Path(record['prototype'])
            if not asset.is_file():raise ValueError('Prototype asset has not been exported: '+str(asset))
            for child in list(prim.GetChildren()):stage.RemovePrim(child.GetPath())
            prim.GetReferences().ClearReferences()
            prim.GetReferences().AddReference(Path(os.path.relpath(asset,output.parent)).as_posix())
            # Relative references resolve against the final output location, not the staging layer.
            prim.SetInstanceable(True)
        apply_settings(prim,record['settings'])
        prim.RemoveProperty('vecxy:id')
    root=stage.GetDefaultPrim()
    if not root:
        root=stage.GetPrimAtPath('/Scene');stage.SetDefaultPrim(root)
    apply_settings(root,manifest['root'])
    # Drop unused Blender-internal prototypes once scene instances use external assets.
    internal=stage.GetPrimAtPath('/Scene/prototypes')
    if internal:
        uses_internal=False
        for candidate in stage.Traverse():
            if candidate.IsAbstract():continue
            refs=candidate.GetMetadata('references')
            if refs and any(not ref.assetPath and str(ref.primPath).startswith('/Scene/prototypes') for ref in refs.GetAddedOrExplicitItems()):
                uses_internal=True;break
        if not uses_internal:stage.RemovePrim('/Scene/prototypes')
    UsdGeom.SetStageUpAxis(stage,UsdGeom.Tokens.z);UsdGeom.SetStageMetersPerUnit(stage,1)
    layer.Export(str(output))
    if output.suffix.lower()=='.usda':output.write_text(output.read_text().rstrip()+'\n')
    temporary.unlink(missing_ok=True)
    # Validate all newly external references after relocation, not relative to staging.
    reopened=Usd.Stage.Open(str(output))
    if not reopened:raise ValueError('Exported USD could not be reopened')
    print('Vecxy USD exported:',output)


if __name__=='__main__':
    try:run(json.loads(Path(sys.argv[1]).read_text()))
    except Exception as error:
        print('Vecxy USD export failed: '+str(error),file=sys.stderr);sys.exit(1)
