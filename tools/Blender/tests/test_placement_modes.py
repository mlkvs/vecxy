"""UI must never reuse the last Move/Copy/Library operator action."""
from pathlib import Path
from types import SimpleNamespace
import sys
import bpy
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
import vecxy_usd
vecxy_usd.register()
from vecxy_usd import level_design as ld
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
bpy.ops.mesh.primitive_cube_add()
bpy.ops.vecxy.mark_prototype()
obj=bpy.context.object;col=obj.instance_collection
bpy.context.scene.vecxy_layout.library_index=list(bpy.data.collections).index(col)
# Simulate Blender's cached properties left by a previous Move invocation.
operations=[]
class Layout:
    def operator(self,name,**kwargs):
        props=SimpleNamespace(collection_name='Previously used collection',from_selection=True,move_existing=True)
        operations.append((name,kwargs.get('text'),props));return props
    def row(self,*a,**kw):return self
    box=row
    def __getattr__(self,name):return lambda *a,**kw:None
ld.VECXY_PT_level_design.draw(SimpleNamespace(layout=Layout()),bpy.context)
buttons=[(text,props) for name,text,props in operations if name=='vecxy.place_shared']
assert len(buttons)==3
library=next(p for text,p in buttons if text is None)
copy=next(p for text,p in buttons if text=='Копии мышью')
move=next(p for text,p in buttons if text=='Переставить')
assert (library.collection_name,library.from_selection,library.move_existing)==(col.name,False,False)
assert (copy.collection_name,copy.from_selection,copy.move_existing)==('',True,False)
assert (move.collection_name,move.from_selection,move.move_existing)==('',False,True)
operations.clear()
ld.VECXY_UL_shared_library.draw_item(None,bpy.context,Layout(),bpy.data,col,0,None,None,0)
props=operations[0][2]
assert (props.collection_name,props.from_selection,props.move_existing)==(col.name,False,False)
# These transient actions should not become last-used defaults at all.
rna=bpy.ops.vecxy.place_shared.get_rna_type()
assert all(rna.properties[key].is_skip_save for key in ('collection_name','from_selection','move_existing'))
print('PASS: library, row plus, copy and move buttons reset all cached action properties')
vecxy_usd.unregister()
