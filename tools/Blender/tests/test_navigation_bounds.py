"""Regression for floors beyond the old fixed bounds and viewport bake boxes."""
import os
from pathlib import Path
import sys

import bpy

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import vecxy_usd as addon
from vecxy_usd import navigation

addon.register()
context = bpy.context
context.scene.vecxy_usd.catalog_path = os.environ['VECXY_TEST_CATALOG']
addon.schema.reload_catalog(context, required=True)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
collider_type = 'Vecxy.Physics.BoxCollider'
size_field = next(field for field in addon.schema.CATALOG[collider_type]['fields'] if field['key'] == 'size')
for x in (-20, 20):
    floor = bpy.data.objects.new('Floor collider', None)
    context.scene.collection.objects.link(floor)
    floor.location = (x, 0, -.1)
    floor.vecxy.is_static = True
    addon.schema.set_param(addon.schema.add_component(floor.vecxy, collider_type), size_field, (4, 4, .2))
context.view_layer.update()

settings = navigation.options(context)
assert settings.scope == 'SCENE'
original = navigation.capture(context)['settings']
assert original['min']['x'] < -22 and original['max']['x'] > 22

settings.scope = 'BOX'
assert bpy.ops.vecxy.navigation_bake_area() == {'FINISHED'}
area = navigation.bake_area(context)
assert context.object == area and navigation.is_authoring_volume(area)
area.location = (-20, 0, 1)
area.scale = (4, 4, 4)
context.view_layer.update()
cropped = navigation.capture(context)['settings']
assert cropped['min']['x'] == -22 and cropped['max']['x'] == -18

assert bpy.ops.vecxy.navigation_bake_area(fit=True) == {'FINISHED'}
fitted = navigation.capture(context)['settings']
assert abs(fitted['min']['x'] - original['min']['x']) < .0001
assert abs(fitted['max']['x'] - original['max']['x']) < .0001
settings.scope = 'SCENE'
assert area.hide_get()
assert navigation.capture(context)['settings'] == original
print('PASS: entire-scene bounds, movable bake box and scene fit')
