"""Run: blender --background --factory-startup --python .../tests/test_addon.py.
Tests UI operators and real Blender -> external USD prototype export in a temp workspace.
"""
from pathlib import Path
import json
import subprocess
import sys
import tempfile
import bpy

sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
import vecxy_usd
from vecxy_usd import exporter
vecxy_usd.register()
repo=next(p for p in Path(__file__).resolve().parents if (p/'.agents').exists() and (p/'Horror').exists())
python=repo/'.agents/local/usd-env/bin/python'
root=repo/'.agents/local/addon-tests';root.mkdir(parents=True,exist_ok=True)
# Load the generated project schema. No field definitions are hardcoded in the addon.
bpy.context.scene.vecxy_usd.catalog_path=str(repo/'Horror/Authoring/vecxy.components.json')
assert bpy.ops.vecxy.reload_catalog()=={'FINISHED'}
schema=vecxy_usd.schema
assert 'Horror.LaundryMachine' in schema.CATALOG
assert 'Vecxy.Physics.BoxCollider' in schema.CATALOG
assert any(f['label']=='Номер машинки' and f['kind']=='INT' for f in schema.CATALOG['Horror.LaundryMachine']['fields'])
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
bpy.ops.mesh.primitive_plane_add(size=4)
plane=bpy.context.object
schema.add_component(plane.vecxy,'Vecxy.Physics.BoxCollider')
assert bpy.ops.vecxy.fit_collider()=={'FINISHED'}
shape=schema.collider_view(plane.vecxy)
assert abs(shape.collider_size[2]-.02)<1e-5 and abs(shape.collider_center[2]+.01)<1e-5
bpy.data.objects.remove(plane,do_unlink=True)
bpy.ops.mesh.primitive_cube_add(size=2,location=(0,0,1))
obj=bpy.context.object;obj.name='TestWasher'
# Re-exporting already localized images must not grow content-hash prefixes.
import hashlib
image=bpy.data.images.new('TestTexture',width=2,height=2)
image.filepath_raw=str(root/'test.png');image.file_format='PNG';image.save()
payload=(root/'test.png').read_bytes();digest=hashlib.sha256(payload).hexdigest()[:12]
localized=root/(digest+'_test.png');localized.write_bytes(payload);image.filepath=str(localized)
material=bpy.data.materials.new('Textured');material.use_nodes=True
texture=material.node_tree.nodes.new('ShaderNodeTexImage');texture.image=image
material.node_tree.links.new(texture.outputs['Color'],material.node_tree.nodes.get('Principled BSDF').inputs['Base Color'])
obj.data.materials.append(material)

# A rotated child under a nonuniform instance must retain its exact shape.
bpy.ops.mesh.primitive_cube_add(size=.5,location=(.8,0,1))
child=bpy.context.object;child.name='Angled detail';child.parent=obj
child.location=(0,0,0);child.rotation_euler=(.3,.4,.6)
bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
schema.add_component(obj.vecxy,'Vecxy.Physics.BoxCollider')
assert bpy.ops.vecxy.fit_collider()=={'FINISHED'}
assert schema.collider_view(obj.vecxy).collider_size==(2,2,2)
assert bpy.ops.vecxy.mark_prototype()=={'FINISHED'}
instance=bpy.context.object;col=instance.instance_collection
assert col.vecxy.is_prototype and instance.type=='EMPTY'
assert len(obj.vecxy.components)==0 and any(c.type_name=='Vecxy.Physics.BoxCollider' for c in col.vecxy.components)
col.vecxy.asset_name='TestWasher';col.vecxy.export_path=str(root/'Assets/Prototypes/TestWasher.usda')
comp=schema.add_component(col.vecxy,'Horror.LaundryMachine')
assert bpy.ops.vecxy.create_instance()=={'FINISHED'}
second=bpy.context.object;second.scale=(.75,1.2,1.1)
assert second.instance_collection==col
sphere=schema.add_component(second.vecxy,'Vecxy.Physics.SphereCollider',key='collider')
field=next(f for f in schema.CATALOG[sphere.type_name]['fields'] if f['key']=='radius');schema.set_param(sphere,field,.7)
second.vecxy.override_tint=True;second.vecxy.tint=(.5,.8,.9,1)
own=schema.add_component(second.vecxy,'Horror.LaundryMachine',override=True,key='machine')
field=next(f for f in schema.CATALOG[own.type_name]['fields'] if f['key']=='number');schema.set_param(own,field,42)
settings=bpy.context.scene.vecxy_usd;settings.python_path=str(python);settings.export_path=str(root/'Assets/Scene.usda');settings.cook_after_export=False
bpy.ops.wm.save_as_mainfile(filepath=str(root/'Authoring.blend'))
count=len(bpy.data.objects)
assert bpy.ops.vecxy.export_scene()=={'FINISHED'}
assert len(bpy.data.objects)==count,'Export must not create collider helper objects'
assert not any(digest+'_'+digest in p.name for p in (root/'Assets').rglob('*.png'))
scene_bytes=(root/'Assets/Scene.usda').read_bytes()
field=next(f for f in schema.CATALOG[comp.type_name]['fields'] if f['key']=='cycleSeconds');schema.set_param(comp,field,600)
assert bpy.ops.vecxy.export_prototype()=={'FINISHED'}
assert (root/'Assets/Scene.usda').read_bytes()==scene_bytes,'Prototype-only export must not rewrite scene'
command=[str(python),str(repo/'Engine/Vecxy/Code/Vecxy.Usd/Tools/cook_usd.py'),str(root/'Assets/Scene.usda'),'--assets',str(root/'Assets')]
subprocess.run(command,check=True)
doc=json.loads((root/'Assets/Scene.usda.usdscene').read_text())
instances=[p for p in doc['prims'] if p.get('prototype')]
assert len(instances)==2 and len({p['prototype'] for p in instances})==1
shapes=[next(c['type'] for c in p['components'] if c['key']=='collider') for p in instances]
assert sorted(shapes)==['Vecxy.Physics.BoxCollider','Vecxy.Physics.SphereCollider']
values=[next(c['data'] for c in p['components'] if c['key']=='machine') for p in instances]
assert all(v['cycleSeconds']==600 for v in values),'Prototype default changes must reach overridden instances'
assert any(v.get('number')==42 for v in values)
assert any(p.get('tint') for p in instances)
assert bpy.ops.vecxy.reset_overrides()=={'FINISHED'}
assert len(second.vecxy.components)==0
original_matrices={o:o.matrix_world.copy() for o in col.all_objects}
assert bpy.ops.vecxy.edit_prototype()=={'FINISHED'}
proxy=settings.editing_proxy
assert proxy and col.name not in bpy.context.scene.collection.children
for copy in proxy.all_objects:
    if not copy.get('vecxy_edit_original'):continue
    original=bpy.data.objects[copy['vecxy_edit_original']]
    assert copy.data is original.data
    expected=second.matrix_world @ original_matrices[original]
    assert max(abs(copy.matrix_world[i][j]-expected[i][j]) for i in range(4) for j in range(4))<.00001,'Editing must preserve instance position, scale and proportions'
    assert max(abs(original.matrix_world[i][j]-original_matrices[original][i][j]) for i in range(4) for j in range(4))<.00001
# Shared vertex edits and object-mode changes survive a scaled edit placement.
copy=next(o for o in proxy.all_objects if o.type=='MESH')
original=bpy.data.objects[copy['vecxy_edit_original']]
copy.data.vertices[0].co.x+=.25
assert original.data.vertices[0].co.x==copy.data.vertices[0].co.x
copy.location.x+=.3
bpy.context.view_layer.update()
expected=second.matrix_world.inverted() @ copy.matrix_world
assert bpy.ops.vecxy.finish_prototype()=={'FINISHED'}
assert max(abs(original.matrix_world[i][j]-expected[i][j]) for i in range(4) for j in range(4))<1e-5
assert settings.editing_proxy is None and not second.hide_get()
assert len(bpy.data.objects)==count
print('PASS: generated project/engine catalog, typed colliders as components, field inheritance, external prototype update, shared GPU source, no collider objects, in-place editing with exact nonuniform instance scale')
