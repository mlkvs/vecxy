"""Regression: removing/re-adding prototype colliders and explicit UI actions.
VECXY_TEST_CATALOG points to a generated vecxy.components.json.
"""
import os
from pathlib import Path
from types import SimpleNamespace
import sys
import bpy
from mathutils import Vector
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
import vecxy_usd as addon
addon.register()
s=bpy.context.scene.vecxy_usd
s.catalog_path=os.environ['VECXY_TEST_CATALOG']
addon.schema.reload_catalog(bpy.context,required=True)
s.selected_component='Vecxy.Physics.BoxCollider'
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
bpy.ops.mesh.primitive_cube_add(size=2)
mesh=bpy.context.object
for vertex in mesh.data.vertices:vertex.co+=Vector((3,0,2))
assert bpy.ops.vecxy.component(index=-1)=={'FINISHED'}
assert addon.schema.collider_view(mesh.vecxy).collider_center==(3,0,2)
assert bpy.ops.vecxy.mark_prototype()=={'FINISHED'}
instance=bpy.context.object;col=instance.instance_collection
assert bpy.ops.vecxy.edit_prototype()=={'FINISHED'}
assert addon.current_settings(bpy.context)==col.vecxy
for _ in range(4):
    assert bpy.ops.vecxy.component(index=0)=={'FINISHED'}
    assert len(col.vecxy.components)==0
    assert bpy.ops.vecxy.component(index=-1)=={'FINISHED'}
    assert len(col.vecxy.components)==1 and len(mesh.vecxy.components)==0
    shape=addon.schema.collider_view(col.vecxy)
    assert shape.collider_center==(3,0,2) and shape.collider_size==(2,2,2)
    assert addon.current_settings(bpy.context)==col.vecxy
# Layout must assign Add explicitly even if Blender remembers index=0 from Remove.
operations=[]
class Layout:
    def operator(self,name,**kwargs):
        props=SimpleNamespace(index=0)
        operations.append((name,kwargs.get('text'),props))
        return props
    def row(self,*a,**kw):return self
    column=row
    box=row
    def __getattr__(self,name):return lambda *a,**kw:None
addon.VECXY_PT_tools.draw(SimpleNamespace(layout=Layout()),bpy.context)
add=next(props for name,text,props in operations if name=='vecxy.component' and text=='Add selected component')
assert add.index==-1,'Add must not reuse a cached Remove index'
# Edit Mode does not change the settings owner; adding targets the shared source.
bpy.ops.object.mode_set(mode='EDIT')
assert addon.current_settings(bpy.context)==col.vecxy
assert bpy.ops.vecxy.component(index=0)=={'FINISHED'}
import bmesh
bm=bmesh.from_edit_mesh(mesh.data)
for vertex in bm.verts:vertex.co.x+=1
bmesh.update_edit_mesh(mesh.data)
assert bpy.ops.vecxy.component(index=-1)=={'FINISHED'}
assert addon.schema.collider_view(col.vecxy).collider_center==(4,0,2)
bpy.ops.object.mode_set(mode='OBJECT')
assert bpy.ops.vecxy.finish_prototype()=={'FINISHED'}
assert addon.effective_collider(instance).collider=='BOX'
assert len(instance.vecxy.components)==0
assert s.colliders_xray
# An existing component is not silently reset by a repeated Add.
instance.vecxy.edit_defaults=True
component=col.vecxy.components[0]
field=next(f for f in addon.schema.CATALOG[component.type_name]['fields'] if f['key']=='size')
addon.schema.set_param(component,field,(4,5,6))
assert bpy.ops.vecxy.component(index=-1)=={'CANCELLED'}
assert addon.schema.collider_view(col.vecxy).collider_size==(4,5,6)
# Empty markers still allow a manually sized collider without an auto-fit failure.
bpy.ops.object.empty_add()
empty=bpy.context.object
assert bpy.ops.vecxy.component(index=-1)=={'FINISHED'}
assert addon.effective_collider(empty).collider=='BOX'
print('PASS: repeated source collider removal/addition, fitted bounds, explicit Add UI action, Edit Mode owner, inherited collider and preserved existing values')
addon.unregister()
