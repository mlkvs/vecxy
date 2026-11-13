"""Blender-native geometry export plus OpenUSD composition finalization.

The Blender process needs no pxr binding. A configured OpenUSD Python performs
reference authoring, asset localization and manifest metadata after export.
"""
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import bpy
from . import ensure_id, instance_collection, safe_name


def workspace():
    if not bpy.data.filepath:raise RuntimeError('Save the .blend before exporting')
    for parent in Path(bpy.data.filepath).resolve().parents:
        if (parent/'Engine/Vecxy').exists():return parent
    return Path(bpy.data.filepath).resolve().parent


def python_executable(context):
    configured=context.scene.vecxy_usd.python_path or os.environ.get('VECXY_USD_PYTHON','')
    if configured:return bpy.path.abspath(configured)
    base=workspace()/'.agents/local/usd-env'
    for executable in [base/'bin/python',base/'Scripts/python.exe']:
        if executable.is_file():return str(executable)
    return shutil.which('python3') or shutil.which('python') or 'python3'


def resolved_path(path):
    if not path:raise RuntimeError('Set the USD export path first')
    result=Path(bpy.path.abspath(path)).resolve()
    if result.suffix.lower() not in ('.usd','.usda','.usdc'):raise RuntimeError('Export path must end in .usd, .usda or .usdc')
    return result


def components(settings):
    from . import schema
    result=[]
    for comp in settings.components:
        if not comp.key or comp.type_name not in schema.CATALOG:raise RuntimeError('Build and reload the component catalog: '+comp.type_name)
        data=schema.values(comp,include_defaults=False)
        result.append({'key':comp.key,'type':comp.type_name,'enabled':comp.enabled,'data':data})
    if len({c['key'] for c in result})!=len(result):raise RuntimeError('Component override keys must be unique')
    return result


def metadata(settings,instance=False,defaults=None):
    value={}
    if not instance or settings.override_role:
        value['role']=settings.role
    if settings.override_tint:value['tint']=list(settings.tint)
    if instance:
        value['componentOverrides']=json.dumps(components(settings),separators=(',',':'))
    elif not instance:
        value['components']=json.dumps(components(settings),separators=(',',':'))
    return value


def tag_objects(objects):
    records={};old={}
    for obj in objects:
        old[obj]={k:obj[k] for k in obj.keys() if k.startswith('vecxy:')}
        # Remove obsolete migration attributes; the finalizer authors current UI values.
        for key in list(old[obj]):del obj[key]
        key=ensure_id(obj.vecxy)
        # Shift+D copies custom IDs as well: disambiguate within the actual export.
        if key in records:obj.vecxy.identifier='';key=ensure_id(obj.vecxy)
        obj['vecxy:id']=key
        col=instance_collection(obj)
        record={'settings':metadata(obj.vecxy,bool(col),col.vecxy if col else None)}
        if col:record['prototype']=str(resolved_path(col.vecxy.export_path))
        if obj.type=='LIGHT' and 'vecxy:range' in old[obj]:record['settings']['range']=float(old[obj]['vecxy:range'])
        records[key]=record
    return records,old


def native_export(context,objects,temporary):
    selected=list(context.selected_objects);active=context.view_layer.objects.active
    hidden={o:(o.hide_get(),o.hide_render) for o in objects}
    if context.mode!='OBJECT':bpy.ops.object.mode_set(mode='OBJECT')
    records,old=tag_objects(objects)
    try:
        for obj in context.scene.objects:obj.select_set(False)
        for obj in objects:obj.hide_set(False);obj.hide_render=False;obj.select_set(True)
        if objects:context.view_layer.objects.active=objects[0]
        bpy.ops.wm.usd_export(filepath=str(temporary),check_existing=False,selected_objects_only=True,
            export_animation=False,use_instancing=True,export_materials=True,
            generate_preview_surface=True,generate_materialx_network=False,export_uvmaps=True,
            export_normals=True,export_textures_mode='NEW',overwrite_textures=True,relative_paths=True,
            export_custom_properties=True,custom_properties_namespace='',root_prim_path='/Scene',
            convert_orientation=False,triangulate_meshes=True,export_lights=True,export_cameras=False,convert_world_material=False)
    finally:
        for obj in objects:
            for key in [k for k in obj.keys() if k.startswith('vecxy:')]:del obj[key]
            for key,value in old[obj].items():obj[key]=value
            obj.hide_set(hidden[obj][0]);obj.hide_render=hidden[obj][1];obj.select_set(False)
        for obj in selected:
            if obj.name in context.view_layer.objects:obj.select_set(True)
        context.view_layer.objects.active=active
    return records


def finalize(context,input_path,output,records,root_settings=None):
    manifest={'input':str(input_path),'output':str(output),'objects':records,'root':root_settings or {}}
    descriptor=input_path.with_suffix('.json');descriptor.write_text(json.dumps(manifest),encoding='utf8')
    result=subprocess.run([python_executable(context),str(Path(__file__).with_name('finalize_usd.py')),str(descriptor)],capture_output=True,text=True)
    if result.returncode:raise RuntimeError((result.stderr or result.stdout)[-1500:]+'\nSet OpenUSD Python to a Python with usd-core and Pillow installed.')
    print(result.stdout.strip())


def export_prototype(context,col):
    if context.scene.vecxy_usd.editing:
        bpy.ops.vecxy.finish_prototype()
    if not col.vecxy.is_prototype:raise RuntimeError('Collection is not marked as a Vecxy prototype')
    path=resolved_path(col.vecxy.export_path);path.parent.mkdir(parents=True,exist_ok=True)
    linked=col.name in context.scene.collection.children
    if not linked:context.scene.collection.children.link(col)
    try:
        objects=list(col.all_objects)
        if not objects:raise RuntimeError('Prototype collection is empty')
        temp_root=workspace()/'.agents/local';temp_root.mkdir(parents=True,exist_ok=True)
        with tempfile.TemporaryDirectory(prefix='usd-prototype-',dir=temp_root) as tmp:
            temporary=Path(tmp)/'prototype.usda'
            records=native_export(context,objects,temporary)
            finalize(context,temporary,path,records,metadata(col.vecxy))
    finally:
        if not linked and col.name in context.scene.collection.children:context.scene.collection.children.unlink(col)
    return path


def export_scene(context):
    output=resolved_path(context.scene.vecxy_usd.export_path);output.parent.mkdir(parents=True,exist_ok=True)
    source_objects={o for col in bpy.data.collections if col.vecxy.is_prototype or col.vecxy.is_edit_proxy for o in col.all_objects}
    objects=[o for o in context.scene.objects if o not in source_objects and not o.hide_render]
    prototypes={instance_collection(o) for o in objects if instance_collection(o)}
    for col in sorted(prototypes,key=lambda c:c.name):
        if context.scene.vecxy_usd.export_prototypes or not resolved_path(col.vecxy.export_path).exists():export_prototype(context,col)
    temp_root=workspace()/'.agents/local';temp_root.mkdir(parents=True,exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='usd-scene-',dir=temp_root) as tmp:
        temporary=Path(tmp)/'scene.usda';records=native_export(context,objects,temporary)
        finalize(context,temporary,output,records)
    if context.scene.vecxy_usd.cook_after_export:
        repo=workspace();cook=repo/'Engine/Vecxy/Code/Vecxy.Usd/Tools/cook_usd.py'
        assets=next((p for p in output.parents if p.name=='Assets'),None)
        if not cook.exists() or assets is None:raise RuntimeError('Scene was exported. To cook automatically, export inside your Vecxy project Assets directory.')
        result=subprocess.run([python_executable(context),str(cook),str(output),'--assets',str(assets)],capture_output=True,text=True)
        if result.returncode:raise RuntimeError(result.stderr[-1500:])
        print(result.stdout.strip())
    return output
