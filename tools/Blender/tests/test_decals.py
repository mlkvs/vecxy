"""Blender -> USD regression for project textures and decals on linked receivers."""
import os
from pathlib import Path
import subprocess
import sys

import bpy
from mathutils import Matrix

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import vecxy_usd as addon
from vecxy_usd import decals, level_design, schema

addon.register()
context = bpy.context
root = Path(os.environ['VECXY_TEST_OUTPUT']).resolve()
assets = root / 'Assets'
assets.mkdir(parents=True, exist_ok=True)
scene = context.scene.vecxy_usd
scene.catalog_path = os.environ['VECXY_TEST_CATALOG']
scene.python_path = os.environ['VECXY_TEST_USD_PYTHON']
scene.export_path = str(assets / 'Scenes/Main.usda')
scene.cook_after_export = False
schema.reload_catalog(context, required=True)

for name, color in (('Paint', (1, .05, .02)), ('Runtime', (.03, 1, .04))):
    image = bpy.data.images.new(name, 32, 32, alpha=True)
    image.pixels = [channel for y in range(32) for x in range(32)
                    for channel in (*color, 1.0 if 2 < x < 29 and 2 < y < 29 else 0.0)]
    image.filepath_raw = str(root / f'{name}.png')
    image.file_format = 'PNG'
    image.save()

assert bpy.ops.vecxy.decal_texture(filepath=str(root / 'Paint.png'), replace=False) == {'FINISHED'}
texture = context.scene.vecxy_decals.texture
assert texture == 'Decals/Paint.png' and decals.asset_file(context, texture).is_file()
(assets / 'Runtime.png').write_bytes((root / 'Runtime.png').read_bytes())

bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
bpy.ops.mesh.primitive_plane_add(size=2, location=(-1, 0, 0))
context.object.name = 'Left'
bpy.ops.mesh.primitive_plane_add(size=2, location=(1, 0, 0))
context.object.name = 'Right'
assert bpy.ops.vecxy.mark_prototype() == {'FINISHED'}
instance = context.object
collection = instance.instance_collection
collection.vecxy.export_path = str(assets / 'Scenes/Prototypes/Right.usda')
context.view_layer.update()
decal = bpy.data.objects.new('Paint_Decal', None)
context.collection.objects.link(decal)
decal.parent = instance
decal.matrix_parent_inverse = instance.matrix_world.inverted()
decal.matrix_basis = Matrix.Identity(4)
comp = schema.add_component(decal.vecxy, decals.TYPE)
decals.set_value(comp, 'texture', texture)
decals.set_value(comp, 'size', (1.6, .75, .3))
decals.set_value(comp, 'opacity', .8)
level_design.select_only(context, [decal])
context.view_layer.update()
assert decal.matrix_world.translation.length < 1e-5
bpy.ops.wm.save_as_mainfile(filepath=str(root / 'Decals.blend'))
assert bpy.ops.vecxy.export_scene() == {'FINISHED'}

verification = """
import json
import sys
from pxr import Usd, UsdGeom, Gf
stage = Usd.Stage.Open(sys.argv[1])
instance = stage.GetPrimAtPath('/Scene/Right_Instance')
assert instance and instance.HasAuthoredReferences()
assert any(prim.IsA(UsdGeom.Mesh) for prim in Usd.PrimRange(instance))
decal = instance.GetChild('Paint_Decal')
assert decal
component, = json.loads(decal.GetAttribute('vecxy:components').Get())
assert component['type'] == 'Vecxy.Rendering.Decal'
assert component['data']['texture'] == 'Decals/Paint.png'
assert abs(component['data']['opacity'] - .8) < 1e-5
assert UsdGeom.XformCache().GetLocalToWorldTransform(decal).Transform(Gf.Vec3d(0)).GetLength() < 1e-5
"""
subprocess.run([scene.python_path, '-c', verification, scene.export_path], check=True)
print('PASS: decal texture import, attachment to external prototype and USD component')
addon.unregister()
