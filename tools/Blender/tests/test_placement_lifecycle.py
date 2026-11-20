"""Graphical test; a driver supplies native mouse clicks/Enter/Esc on each phase.
VECXY_TEST_OUTPUT must be a disposable directory. Never opens a user .blend.
"""
import json
import os
from pathlib import Path
import sys
import traceback
import bpy
import addon_utils
from mathutils import Quaternion
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
import vecxy_usd
assert addon_utils.enable('vecxy_usd',default_set=False,persistent=False)
from vecxy_usd import level_design as ld
out=Path(os.environ['VECXY_TEST_OUTPUT']);out.mkdir(parents=True,exist_ok=True)
bpy.context.preferences.view.show_splash=False
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
bpy.ops.mesh.primitive_plane_add(size=40)
bpy.ops.mesh.primitive_cube_add(size=2,location=(0,0,1))
bpy.context.object.scale=(1,.2,1)
bpy.context.view_layer.update();bpy.ops.vecxy.mark_prototype()
source=bpy.context.object;col=source.instance_collection
bpy.context.scene.vecxy_layout.library_index=list(bpy.data.collections).index(col)
mesh_count=len(bpy.data.meshes)
phases=['move','library','copy','cancel_move','library','move','library']
state={'index':-1,'waiting':False,'ticks':0,'count':1}

def publish(phase):
    temp=out/'phase.tmp'
    temp.write_text(json.dumps({'index':state['index'],'phase':phase}))
    temp.replace(out/'phase.json')

def instances():
    return [o for o in bpy.context.scene.objects if ld.shared(o)]

def tick():
    try:
        state['ticks']+=1
        assert state['ticks']<300,'Native input test timed out'
        area=next(a for a in bpy.context.screen.areas if a.type=='VIEW_3D')
        region=next(r for r in area.regions if r.type=='WINDOW')
        if state['waiting']:
            if ld._ACTIVE:return .1
            phase=phases[state['index']]
            if phase in {'library','copy'}:state['count']+=1
            assert len(instances())==state['count'],(phase,len(instances()),state['count'])
            assert not any(o.get('_vecxy_layout_preview') for o in bpy.data.objects)
            assert len(bpy.data.meshes)==mesh_count
            if phase=='cancel_move':assert source.matrix_world==state['before']
            if phase=='move':assert source.matrix_world!=state['before']
            print('PASS completed',state['index'],phase,'instances',state['count'],flush=True)
            state['waiting']=False
            state['index']+=1
        elif state['index']<0:
            state['index']=0
            view=area.spaces.active.region_3d
            view.view_rotation=Quaternion((1,0,0,0));view.view_location=(0,0,0);view.view_distance=15;view.view_perspective='ORTHO'
        if state['index']>=len(phases):
            assert len({o.vecxy.identifier for o in instances()})==len(instances())
            print('PASS: native move-confirm, move-cancel, library/copy stamping transitions, no stale modal state or previews',flush=True)
            publish('done');bpy.ops.wm.quit_blender();return None
        phase=phases[state['index']]
        ld.select_only(bpy.context,[source])
        state['before']=source.matrix_world.copy()
        props=type('Properties',(),{})()
        class Layout:
            def operator(self,*args,**kwargs):return props
        if phase in {'move','cancel_move'}:ld.place_button(Layout(),move_existing=True)
        elif phase=='copy':ld.place_button(Layout(),from_selection=True)
        else:ld.place_button(Layout(),collection_name=col.name)
        with bpy.context.temp_override(area=area,region=region):
            result=bpy.ops.vecxy.place_shared('INVOKE_DEFAULT',collection_name=props.collection_name,
                from_selection=props.from_selection,move_existing=props.move_existing)
        assert result=={'RUNNING_MODAL'},(phase,result)
        assert len(ld._ACTIVE)==1
        operator=next(iter(ld._ACTIVE))
        assert operator.move_existing==(phase in {'move','cancel_move'})
        assert (operator._preview==source)==operator.move_existing
        state['waiting']=True
        publish(phase)
        return .1
    except Exception:
        traceback.print_exc();publish('error');bpy.ops.wm.quit_blender();return None

bpy.app.timers.register(tick,first_interval=3)
