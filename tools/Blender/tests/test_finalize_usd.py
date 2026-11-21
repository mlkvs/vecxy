"""Run in OpenUSD Python; VECXY_TEST_OUTPUT points to a disposable directory."""
import hashlib
import importlib.util
import os
from pathlib import Path
from pxr import Usd,UsdShade,Sdf

spec=importlib.util.spec_from_file_location('finalize',Path(__file__).resolve().parents[1]/'vecxy_usd/finalize_usd.py')
finalize=importlib.util.module_from_spec(spec);spec.loader.exec_module(finalize)
root=Path(os.environ['VECXY_TEST_OUTPUT']).resolve();root.mkdir(parents=True,exist_ok=True)
source=root/'native/scene.usda';source.parent.mkdir(exist_ok=True)
texture=source.parent/'textures/wall.png';texture.parent.mkdir(exist_ok=True)
texture.write_bytes(b'fixture texture bytes')
stage=Usd.Stage.CreateNew(str(source))
scene=stage.DefinePrim('/Scene','Xform');stage.SetDefaultPrim(scene)
mat=UsdShade.Material.Define(stage,'/Scene/Materials/Wall_material')
mat.GetPrim().CreateAttribute('userProperties:blender:data_name',Sdf.ValueTypeNames.String).Set('Wall material')
shader=UsdShade.Shader.Define(stage,'/Scene/Materials/Wall_material/Image_Texture')
shader.CreateInput('file',Sdf.ValueTypeNames.Asset).Set(Sdf.AssetPath('./textures/wall.png'))
# Both shader prims share the canonical shader's underlying authored property.
for name in ('Door','Wall'):
    stage.DefinePrim('/Scene/'+name).GetReferences().AddInternalReference('/Scene/Materials/Wall_material')
stage.GetRootLayer().Save()
output=root/'Assets/scene.usda'
manifest={'input':str(source),'output':str(output),'objects':{},'root':{},'textureSources':[{
    'material':'Wall material','node':'Image Texture','usdNode':'Image_Texture','image':'Wall.png',
    'path':'//textures/wall.png','absolute':str(texture),'objects':['Wall_3x3','Wall_Door_3x3']} ]}
finalize.run(manifest)
reopened=Usd.Stage.Open(str(output));assets=[]
for prim in reopened.Traverse():
    for attr in prim.GetAttributes():
        if attr.GetTypeName()==Sdf.ValueTypeNames.Asset:
            value=attr.Get();assert Path(value.resolvedPath).is_file(),(attr.GetPath(),value)
            assets.append(value.path)
assert len(assets)==3 and len(set(assets))==1
assert assets[0]=='Textures/'+hashlib.sha256(texture.read_bytes()).hexdigest()[:12]+'_wall.png'
# Failure must identify the authored Blender source and preserve the last good output.
previous=output.read_bytes();texture.unlink()
# Native USD can sanitize material names without preserving Blender's display name.
mat.GetPrim().RemoveProperty('userProperties:blender:data_name');stage.GetRootLayer().Save()
try:finalize.run(manifest)
except ValueError as error:
    details=str(error)
    for text in (str(texture),str(output),'/Scene/Materials/Wall_material/Image_Texture.inputs:file',
                 'Wall material','Image Texture','Wall.png','Wall_3x3','Wall_Door_3x3'):
        assert text in details,(text,details)
    assert 'Set OpenUSD Python' not in details
else:raise AssertionError('Missing asset must fail')
assert output.read_bytes()==previous
print('PASS: shared/composed material relocation, resolved assets, detailed missing-source diagnostics and preservation of last good USD')
