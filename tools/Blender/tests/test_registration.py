"""Enable through Blender's real addon loader, which restricts scene access.
Direct module.register() tests miss the RestrictBlend startup path.
"""
from pathlib import Path
import sys
import bpy
import addon_utils
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
import vecxy_usd
module=addon_utils.enable('vecxy_usd', default_set=False, persistent=False)
assert module is not None and module.__addon_enabled__, 'The addon must fully enable under RestrictBlend'
assert hasattr(bpy.types.Scene,'vecxy_usd') and hasattr(bpy.types.Scene,'vecxy_layout')
from vecxy_usd import level_design
assert bpy.app.timers.is_registered(level_design.cleanup_when_ready)
# Cleanup happens later, once scene access is permitted, without touching real models.
preview=bpy.data.objects.new('Abandoned test preview',None)
preview['_vecxy_layout_preview']=True
bpy.context.scene.collection.objects.link(preview)
ordinary=bpy.data.objects.new('Ordinary test marker',None)
bpy.context.scene.collection.objects.link(ordinary)
assert level_design.cleanup_when_ready() is None
assert 'Abandoned test preview' not in bpy.data.objects
assert ordinary.name in bpy.data.objects
if not bpy.app.background:
    assert module._handle is not None, 'Collider draw callback must be installed after startup'
addon_utils.disable('vecxy_usd', default_set=False)
assert not bpy.app.timers.is_registered(level_design.cleanup_when_ready)
assert not hasattr(bpy.types.Scene,'vecxy_layout')
# Enabling a second time must not fail with stale classes or handlers.
module=addon_utils.enable('vecxy_usd', default_set=False, persistent=False)
assert module is not None and module.__addon_enabled__
addon_utils.disable('vecxy_usd', default_set=False)
print('PASS: real addon_utils enable/disable/re-enable, deferred cleanup, timer cleanup and preserved ordinary objects')
