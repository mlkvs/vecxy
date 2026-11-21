"""Blender and OpenUSD regression for nested references, mesh colliders and detach."""
import os
from pathlib import Path
import subprocess
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import vecxy_usd as addon
from vecxy_usd import collider_mesh, level_design

addon.register()
context = bpy.context
root = Path(os.environ['VECXY_TEST_OUTPUT']).resolve()
root.mkdir(parents=True, exist_ok=True)
scene = context.scene.vecxy_usd
scene.catalog_path = os.environ['VECXY_TEST_CATALOG']
scene.python_path = os.environ['VECXY_TEST_USD_PYTHON']
scene.cook_after_export = False
scene.export_path = str(root / 'Assets/Scenes/Main.usda')
addon.schema.reload_catalog(context, required=True)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)

# The hole in this floor must survive evaluation, USD composition and detaching.
coords = [(-4, -4), (4, -4), (4, 4), (-4, 4), (-1, -1), (1, -1), (1, 1), (-1, 1)]
mesh = bpy.data.meshes.new('Ring')
mesh.from_pydata([(x, y, 0) for x, y in coords], [], [
    (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)])
mesh.update()
floor = bpy.data.objects.new('Ring', mesh)
context.collection.objects.link(floor)
level_design.select_only(context, [floor])
scene.selected_component = addon.schema.MESH_COLLIDER
assert bpy.ops.vecxy.component(index=-1) == {'FINISHED'}
assert bpy.ops.vecxy.mark_prototype() == {'FINISHED'}
inner = context.object
inner_source = inner.instance_collection
inner_source.vecxy.export_path = str(root / 'Assets/Scenes/Prototypes/Ring.usda')
inner_source.instance_offset = (.25, .5, 0)
inner.location = inner_source.instance_offset
inner.vecxy.edit_defaults = True
scene.selected_component = 'Vecxy.Physics.BoxCollider'
assert bpy.ops.vecxy.component(index=-1) == {'FINISHED'}
box = next(comp for comp in inner_source.vecxy.components if comp.type_name == scene.selected_component)
fields = {f['key']: f for f in addon.schema.CATALOG[box.type_name]['fields']}
addon.schema.set_param(box, fields['size'], (.5, .5, .5))
addon.schema.set_param(box, fields['center'], (3, 0, -1))
inner.vecxy.edit_defaults = False
base_mesh = inner_source.vecxy.components[0]
assert bpy.ops.vecxy.override_component(index=0) == {'FINISHED'}
offset = next(f for f in addon.schema.CATALOG[base_mesh.type_name]['fields'] if f['key'] == 'center')
addon.schema.set_param(inner.vecxy.components[0], offset, (0, 0, .125))

collection = bpy.data.collections.new('Assembly')
context.scene.collection.children.link(collection)
for owner in list(inner.users_collection):
    owner.objects.unlink(inner)
collection.objects.link(inner)
collection.instance_offset = (.5, -.25, 0)
context.view_layer.update()
assert bpy.ops.vecxy.prototype_from_collection(collection_name=collection.name) == {'FINISHED'}
outer = context.object
collection.vecxy.export_path = str(root / 'Assets/Scenes/Prototypes/Assembly.usda')
collection['_vecxy_layout_scale'] = (.5, 2, 1)
del collection['_vecxy_placement_scale']
clone = level_design.create_shared(context, collection)
assert tuple(clone.scale) == (1, 1, 1)
clone.rotation_euler.z = .4
clone.location = (20, 3, 0)
context.view_layer.update()
graph = context.evaluated_depsgraph_get()
original_points = level_design.geometry_points(clone, graph)
level_design.select_only(context, [clone])
assert bpy.ops.vecxy.detach_prototype() == {'FINISHED'}
detached = next(child for child in clone.children_recursive if child.type == 'MESH')
assert detached.data != mesh and not any(child.instance_collection for child in clone.children_recursive)
assert not clone.instance_collection
detached_inner = detached.parent
shapes = list(addon.schema.collider_views(detached_inner.vecxy))
assert len(shapes) == 2 and shapes[0].collider_center[2] == .125, shapes
vertices, indices = collider_mesh.triangles(context.evaluated_depsgraph_get(), obj=detached_inner)
assert len(vertices) == 8 and len(indices) == 24
decomposed_points = level_design.geometry_points(detached, context.evaluated_depsgraph_get())
assert all((a - b).length < 1e-5 for a, b in zip(original_points, decomposed_points))

level_design.select_only(context, [outer])
assert bpy.ops.vecxy.library_from_selection() == {'FINISHED'}
assert bpy.ops.vecxy.rename_library_prototype(name='Floor Assembly') == {'FINISHED'}
collection.vecxy.library_category = 'Rooms'
settings = context.scene.vecxy_layout
settings.count_x, settings.count_y, settings.count_z = 2, 1, 1
settings.use_dimensions = False
settings.array_step = (3, 2, 2)
settings.reverse_axes = (True, False, False)
assert bpy.ops.vecxy.duplicate_grid() == {'FINISHED'}
reverse_copy = context.object
assert (reverse_copy.location - outer.location - Vector((-3, 0, 0))).length < 1e-5
bpy.data.objects.remove(reverse_copy, do_unlink=True)
level_design.select_only(context, [outer])
settings.library_search = 'rooms floor'
settings.library_category = 'CATEGORY:Rooms'
assert level_design.library_collection(context) == collection
assert bpy.ops.vecxy.hide_library_prototype() == {'FINISHED'}
assert level_design.library_collection(context) is None and outer.instance_collection == collection
assert bpy.ops.vecxy.library_from_selection() == {'FINISHED'}

bpy.ops.object.empty_add(location=(0, 2, .125))
context.object.name = 'PlayerSpawn'
context.object.vecxy.role = 'spawn'
bpy.ops.wm.save_as_mainfile(filepath=str(root / 'Layout.blend'))
assert bpy.ops.vecxy.export_scene() == {'FINISHED'}
verification = """
import json
import sys
from pxr import Usd, UsdGeom, Gf
stage = Usd.Stage.Open(sys.argv[1])
refs = [p for p in Usd.PrimRange.Stage(stage, Usd.TraverseInstanceProxies()) if p.HasAuthoredReferences()]
assert any('Assembly.usda' in str(p.GetMetadata('references')) for p in refs)
assert any('Ring.usda' in str(p.GetMetadata('references')) for p in refs)
meshes = [p for p in Usd.PrimRange.Stage(stage, Usd.TraverseInstanceProxies()) if p.IsA(UsdGeom.Mesh)]
placed = next(p for p in meshes if p.IsInstanceProxy())
transform = UsdGeom.XformCache().GetLocalToWorldTransform(placed)
assert transform.Transform(Gf.Vec3d(0)).GetLength() < 1e-5, transform
owner = placed.GetParent()
while owner and not owner.GetAttribute('vecxy:componentOverrides').HasValue():
    owner = owner.GetParent()
components = json.loads(owner.GetAttribute('vecxy:components').Get())
overrides = json.loads(owner.GetAttribute('vecxy:componentOverrides').Get())
assert len(components) == 2
assert len(components[0]['data']['indices']) == 24
assert overrides[0]['data']['center']['z'] == .125
"""
subprocess.run([scene.python_path, '-c', verification, scene.export_path], check=True)
print('PASS: nested USD composition, collection offsets, mesh collider geometry and overrides, detached geometry and library filters')
addon.unregister()
