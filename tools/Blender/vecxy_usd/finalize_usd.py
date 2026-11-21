"""Runs outside Blender with OpenUSD installed. Converts collection instances into
portable external USD references and authors object components without helper meshes.
"""
import hashlib
import json
import os
from pathlib import Path
import shutil
import sys
from pxr import Usd, UsdGeom, Sdf, Gf, Ar, UsdShade, Tf

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


def missing_asset_details(manifest,attr,asset_path,resolved):
    prim=attr.GetPrim()
    material=prim
    while material and not material.IsA(UsdShade.Material):material=material.GetParent()
    name=material.GetAttribute('userProperties:blender:data_name').Get() if material else None
    name=name or (material.GetName() if material else '')
    lines=['Missing exported asset: '+asset_path,
           'Export destination: '+str(Path(manifest['output']).resolve()),
           'Searched file: '+str(Path(resolved).resolve()),
           'USD property: '+str(attr.GetPath()),
           'USD source layer: '+str(Path(manifest['input']).resolve())]
    if name:lines.append('Material: '+name)
    matches=[item for item in manifest.get('textureSources',[])
             if item['material']==name or (material and Tf.MakeValidIdentifier(item['material'])==material.GetName())]
    node_matches=[item for item in matches if item.get('usdNode')==prim.GetName() or Tf.MakeValidIdentifier(item['node'])==prim.GetName()]
    if node_matches:matches=node_matches
    for item in matches:
        lines.extend(['Blender material: '+item['material'],
                      'Blender node: '+item['node'],
                      'Blender image: '+item['image'],
                      'Image path: '+item['path'],
                      'Absolute image path: '+item['absolute'],
                      'Blender objects: '+(', '.join(item['objects']) or '(no object material slots)')])
    if matches and all(Path(item['absolute']).is_file() for item in matches):
        lines.append('The Blender source image exists. The missing file is in USD export staging, not the original image.')
    else:lines.append('Check the listed image path or pack the image into the .blend, then export again.')
    return '\n'.join(lines)


def run(manifest):
    source=Path(manifest['input']);output=Path(manifest['output']);output.parent.mkdir(parents=True,exist_ok=True)
    stage=Usd.Stage.Open(str(source));layer=stage.GetRootLayer()
    for prim in stage.Traverse():
        if not prim.IsA(UsdShade.Material):continue
        name=prim.GetAttribute('userProperties:blender:data_name').Get()
        interpolation=manifest.get('textureFilters',{}).get(name)
        if not interpolation:continue
        surface=UsdShade.Material(prim).ComputeSurfaceSource()
        diffuse=surface[0].GetInput('diffuseColor') if surface else None
        if diffuse and diffuse.HasConnectedSource():
            shader=diffuse.GetConnectedSource()[0].GetPrim()
            shader.CreateAttribute('vecxy:interpolation',Sdf.ValueTypeNames.Token,custom=True).Set(interpolation)
    # Capture ALL original asset values before authoring any relocated paths.
    # Multiple composed prims can share the same underlying material property.
    # Reading after Set() would re-interpret an output-relative path in staging.
    assets=[]
    for prim in stage.Traverse():
        for attr in prim.GetAttributes():
            if attr.GetTypeName()!=Sdf.ValueTypeNames.Asset:continue
            value=attr.Get()
            if not value:continue
            resolved=value.resolvedPath or str((source.parent/value.path).resolve())
            assets.append((attr,value.path,resolved))
    relocated={}
    for attr,asset_path,resolved in assets:
        if not Path(resolved).is_file():
            raise ValueError(missing_asset_details(manifest,attr,asset_path,resolved))
        key=(resolved,Path(asset_path).name)
        if key not in relocated:
            payload=Path(resolved).read_bytes()
            digest=hashlib.sha256(payload).hexdigest()[:12]
            name=Path(asset_path).name
            while name.startswith(digest+'_'):name=name[len(digest)+1:]
            target=output.parent/'Textures'/(digest+'_'+name)
            target.parent.mkdir(exist_ok=True)
            if not target.exists() or target.read_bytes()!=payload:target.write_bytes(payload)
            relocated[key]=Path(os.path.relpath(target,output.parent)).as_posix()
        attr.Set(Sdf.AssetPath(relocated[key]))
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
