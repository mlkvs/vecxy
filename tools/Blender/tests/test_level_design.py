"""Run with Blender --background --factory-startup --python this_file.py.
No game/user scene is opened or saved. Uses the same placement math as the viewport tool.
"""
import math
import os
from pathlib import Path
import sys

import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import vecxy_usd
vecxy_usd.register()
from vecxy_usd import level_design as ld


def near(a, b, epsilon=1e-5):
    assert (Vector(a) - Vector(b)).length < epsilon, (a, b)


def world_bounds(obj):
    bpy.context.view_layer.update()
    return ld.bounds(ld.geometry_points(obj, bpy.context.evaluated_depsgraph_get()))


bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
bpy.ops.mesh.primitive_cube_add(size=2, location=(11, 3, 2))
source = bpy.context.object
source.name = 'Wall'
# Mesh is deliberately far from the pivot; the original problem is not hidden by a centered test cube.
for vertex in source.data.vertices:
    vertex.co += Vector((4, -2, 1))
source.scale = (1, .2, 1)
bpy.context.view_layer.update()
assert bpy.ops.vecxy.mark_prototype() == {'FINISHED'}
original = bpy.context.object
col = original.instance_collection
col.instance_offset = (1, .5, .25)
settings = bpy.context.scene.vecxy_layout
settings.library_index = list(bpy.data.collections).index(col)
mesh_count = len(bpy.data.meshes)
source_matrices = {o.name: o.matrix_world.copy() for o in col.all_objects}
# Geometry support, rather than the EMPTY origin, must land on the cursor.
bpy.context.scene.cursor.location = (20, 10, 7)
assert bpy.ops.vecxy.add_shared_at_cursor() == {'FINISHED'}
a = bpy.context.object
low, high = world_bounds(a)
near(((low.x + high.x) / 2, (low.y + high.y) / 2, low.z), (20, 10, 7))
assert a.instance_collection == col
near(a.scale, original.matrix_world.decompose()[2])
assert len(bpy.data.meshes) == mesh_count
assert (a.location - bpy.context.scene.cursor.location).length > 1

# An already scaled, rotated instance is tiled in local axes by actual mesh dimensions.
a.rotation_euler.z = math.pi / 2
a.scale = (2, 1, 1)
a.vecxy.override_tint = True
a.vecxy.tint = (.2, .3, .4, 1)
bpy.context.view_layer.update()
settings.count_x, settings.count_y, settings.count_z = 3, 2, 1
settings.array_gap = (.1, .2, 0)
settings.local_axes = True
assert bpy.ops.vecxy.duplicate_grid() == {'FINISHED'}
copies = list(bpy.context.selected_objects)
assert len(copies) == 5
assert all(o.instance_collection == col and o.vecxy.override_tint for o in copies)
assert len({o.vecxy.identifier for o in [a, *copies]}) == 6
assert all(o.data is None for o in copies)
assert len(bpy.data.meshes) == mesh_count
frame = a.matrix_world.to_quaternion().to_matrix().to_4x4()
low, high = ld.bounds([frame.inverted() @ p for p in ld.geometry_points(a, bpy.context.evaluated_depsgraph_get())])
step = high - low + Vector(settings.array_gap)
deltas = [frame.inverted().to_3x3() @ (o.matrix_world.translation - a.matrix_world.translation) for o in copies]
for expected in [(step.x, 0, 0), (2 * step.x, step.y, 0)]:
    assert any((d - Vector(expected)).length < 1e-4 for d in deltas), (expected, deltas)

# Stacking snaps actual faces without moving the reference or changing source pivots.
b = copies[0]
ld.select_only(bpy.context, [b, a])
reference = a.matrix_world.copy()
settings.snap_axis, settings.snap_side, settings.snap_gap = 'Z', 'POSITIVE', .05
assert bpy.ops.vecxy.snap_to_active() == {'FINISHED'}
near(a.matrix_world.translation, reference.translation)
assert abs(world_bounds(b)[0].z - world_bounds(a)[1].z - .05) < 1e-4
# Reject impractical counts before making any object.
count = len(bpy.data.objects)
settings.count_x = settings.count_y = settings.count_z = 100
ld.select_only(bpy.context, [a])
try:
    result = bpy.ops.vecxy.duplicate_grid()
except RuntimeError:  # bpy converts an ERROR report on cancellation into an exception.
    result = {'CANCELLED'}
assert result == {'CANCELLED'} and len(bpy.data.objects) == count

# Nested instances and nonzero offsets must be included exactly once.
nested = bpy.data.collections.new('Nested')
nested.vecxy.is_prototype = True
nested.instance_offset = (2, 0, 0)
inner = bpy.data.objects.new('Nested piece', None)
inner.instance_type = 'COLLECTION'
inner.instance_collection = col
inner.location = (3, 0, 0)
nested.objects.link(inner)
outer = ld.create_shared(bpy.context, nested)
bpy.context.view_layer.update()
expected = ld.geometry_points(original, bpy.context.evaluated_depsgraph_get(), Matrix.Translation((1, 0, 0)))
actual = ld.geometry_points(outer, bpy.context.evaluated_depsgraph_get())
assert len(expected) == len(actual)
for x, y in zip(expected, actual):
    near(x, y)

# Bounds magnet should join edges and ignore unrelated distant geometry.
points = ld.box_points(Vector((1.08, 0, 0)), Vector((3.08, .2, 2)))
targets = [(Vector((-1, 0, 0)), Vector((1, .2, 2))), (Vector((50, 50, 0)), Vector((52, 52, 2)))]
near(ld.magnet_offset(points, targets, .15), (-.08, 0, 0))
near(ld.magnet_offset(points, targets, .01), (0, 0, 0))
# Rotated, nonuniform-scale geometry can be placed on a wall, not only on a floor.
points = ld.geometry_points(a, bpy.context.evaluated_depsgraph_get(), Matrix.Identity(4))
basis = Matrix.Rotation(.6, 4, 'Z') @ Matrix.Diagonal((2, .7, 1, 1))
normal = Vector((0, -1, 0))
hit = Vector((3, 8, 2))
position = ld.support_location(points, basis.to_3x3(), hit, normal)
assert abs(min((basis.to_3x3() @ p + position - hit).dot(normal) for p in points)) < 1e-5

# Tool operations never mutate original source transforms and keep USD instance references.
for obj in col.all_objects:
    assert obj.matrix_world == source_matrices[obj.name]
from vecxy_usd import exporter
assert all(exporter.components(o.vecxy) == [] for o in copies)
assert all(ld.shared(o) == col for o in copies)
# Lifecycle must support disabling/re-enabling without losing authored properties.
ld.unregister()
ld.register()
assert col.vecxy.is_prototype and a.vecxy.override_tint
assert bpy.types.Scene.bl_rna.properties.get('vecxy_layout') is not None
# Optional real USD round-trip uses the existing project's OpenUSD environment.
python = os.environ.get('VECXY_TEST_USD_PYTHON')
if python:
    import subprocess
    root = Path(os.environ['VECXY_TEST_OUTPUT']).resolve()
    root.mkdir(parents=True, exist_ok=True)
    bpy.data.objects.remove(outer, do_unlink=True)
    col.vecxy.export_path = str(root / 'Assets/Prototypes/Wall.usda')
    bpy.context.scene.vecxy_usd.export_path = str(root / 'Assets/Scene.usda')
    bpy.context.scene.vecxy_usd.python_path = python
    bpy.context.scene.vecxy_usd.cook_after_export = False
    bpy.ops.wm.save_as_mainfile(filepath=str(root / 'Layout.blend'))
    assert bpy.ops.vecxy.export_scene() == {'FINISHED'}
    verification = '''from pxr import Usd
import sys
stage = Usd.Stage.Open(sys.argv[1])
refs = [p for p in stage.Traverse() if p.HasAuthoredReferences()]
assert len(refs) == 7, [(str(p.GetPath()), str(p.GetReferences())) for p in refs]
assert all('Wall.usda' in str(p.GetMetadata('references')) for p in refs)
print('PASS: all seven placed instances retain external USD prototype references')
'''
    subprocess.run([python, '-c', verification, str(root / 'Assets/Scene.usda')], check=True)
print('PASS: geometry anchors, offsets/nested prototypes, scaled local arrays, linked geometry, overrides, IDs, face snapping, limits and registration lifecycle')
vecxy_usd.unregister()
