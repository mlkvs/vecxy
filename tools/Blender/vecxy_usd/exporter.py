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
        if any((parent/layout/'Code/Vecxy.Usd').is_dir() for layout in ('Engine/Vecxy','Vendors/Vecxy')):return parent
    return Path(bpy.data.filepath).resolve().parent


def python_executable(context):
    configured=context.scene.vecxy_usd.python_path or os.environ.get('VECXY_USD_PYTHON','')
    if configured:return bpy.path.abspath(configured)
    bases=[parent/'.vecxy/usd-env' for parent in Path(bpy.data.filepath).resolve().parents]
    bases.append(workspace()/'.agents/local/usd-env')
    for base in bases:
        for executable in [base/'bin/python',base/'Scripts/python.exe']:
            if executable.is_file():return str(executable)
    return shutil.which('python3') or shutil.which('python') or 'python3'


def resolved_path(path):
    if not path:raise RuntimeError('Set the USD export path first')
    result=Path(bpy.path.abspath(path)).resolve()
    if result.suffix.lower() not in ('.usd','.usda','.usdc'):raise RuntimeError('Export path must end in .usd, .usda or .usdc')
    return result


def components(settings, mesh_source=None, base=None):
    from . import collider_mesh, schema
    result=[]
    geometry=None
    inherited_meshes={c.key for c in base.components if c.type_name==schema.MESH_COLLIDER and c.enabled} if base else set()
    for comp in settings.components:
        if not comp.key or comp.type_name not in schema.CATALOG:raise RuntimeError('Build and reload the component catalog: '+comp.type_name)
        data=schema.values(comp,include_defaults=False)
        if comp.enabled and comp.type_name == schema.MESH_COLLIDER and comp.key not in inherited_meshes:
            if geometry is None:
                vertices, indices = collider_mesh.triangles(**mesh_source)
                if not indices:
                    owner = mesh_source.get('obj') or mesh_source['collection']
                    raise RuntimeError(f'{owner.name}: Mesh Collider requires nondegenerate mesh geometry')
                geometry = {'vertices': [dict(zip('xyz', vertex)) for vertex in vertices],
                            'indices': indices}
            data.update(geometry)
        result.append({'key':comp.key,'type':comp.type_name,'enabled':comp.enabled,'data':data})
    if len({c['key'] for c in result})!=len(result):raise RuntimeError('Component override keys must be unique')
    return result


def metadata(settings,instance=False,mesh_source=None,base=None):
    value={}
    if not instance or settings.override_role:
        value['role']=settings.role
    if not instance or settings.override_static:
        value['isStatic']=settings.is_static
    if settings.override_tint:value['tint']=list(settings.tint)
    if instance:
        value['componentOverrides']=json.dumps(components(settings,mesh_source,base),separators=(',',':'))
    elif not instance:
        value['components']=json.dumps(components(settings,mesh_source),separators=(',',':'))
    return value


def tag_objects(context, objects, depsgraph, old):
    from . import face_sprites
    records={}
    for obj in objects:
        # Remove obsolete migration attributes; the finalizer authors current UI values.
        for key in list(old[obj]):del obj[key]
        key=ensure_id(obj.vecxy)
        # Shift+D copies custom IDs as well: disambiguate within the actual export.
        if key in records:obj.vecxy.identifier='';key=ensure_id(obj.vecxy)
        obj['vecxy:id']=key
        col=instance_collection(obj)
        record={'settings':metadata(obj.vecxy,bool(col),{'depsgraph':depsgraph,'obj':obj},col.vecxy if col else None)}
        if col:record['prototype']=str(resolved_path(col.vecxy.export_path))
        elif face := face_sprites.component(obj, context):
            settings = record['settings']
            entries = json.loads(settings['components'])
            entries.append(face)
            settings['components'] = json.dumps(entries, separators=(',', ':'))
        if obj.type=='LIGHT' and 'vecxy:range' in old[obj]:record['settings']['range']=float(old[obj]['vecxy:range'])
        records[key]=record
    return records


def native_export(context,objects,temporary):
    selected=list(context.selected_objects);active=context.view_layer.objects.active
    hidden={o:(o.hide_get(),o.hide_render) for o in objects}
    if context.mode!='OBJECT':bpy.ops.object.mode_set(mode='OBJECT')
    old={obj:{k:obj[k] for k in obj.keys() if k.startswith('vecxy:')} for obj in objects}
    try:
        for obj in selected:obj.select_set(False)
        for obj in objects:obj.hide_set(False);obj.hide_render=False;obj.select_set(True)
        if objects:context.view_layer.objects.active=objects[0]
        context.view_layer.update()
        records=tag_objects(context,objects,context.evaluated_depsgraph_get(),old)
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


def finalize(context,input_path,output,records,root_settings=None,collection_offset=None):
    # Blender's USD exporter omits Image Texture interpolation. Preserve the
    # connected base-color node's setting alongside the native export.
    filters={}
    texture_sources=[]
    for material in bpy.data.materials:
        if not material.use_nodes:continue
        for node in material.node_tree.nodes:
            if node.type=='TEX_IMAGE' and node.image:
                image=node.image
                # The native USD exporter sanitizes names into USD identifiers.
                import re
                texture_sources.append({'material':material.name,'node':node.name,
                    'usdNode':re.sub(r'[^A-Za-z0-9_]', '_', node.name),
                    'image':image.name,'path':image.filepath,
                    'absolute':bpy.path.abspath(image.filepath,library=image.library),
                    'objects':[obj.name for obj in bpy.data.objects
                        if any(slot.material==material for slot in obj.material_slots)]})
            if node.type!='BSDF_PRINCIPLED':continue
            links=node.inputs['Base Color'].links
            if links and links[0].from_node.type=='TEX_IMAGE':
                filters[material.name]=links[0].from_node.interpolation
    manifest={'input':str(input_path),'output':str(output),'objects':records,'root':root_settings or {},
              'collectionOffset':collection_offset,'textureFilters':filters,'textureSources':texture_sources}
    descriptor=input_path.with_suffix('.json');descriptor.write_text(json.dumps(manifest),encoding='utf8')
    result=subprocess.run([python_executable(context),str(Path(__file__).with_name('finalize_usd.py')),str(descriptor)],capture_output=True,text=True)
    if result.returncode:
        details=(result.stderr or result.stdout).strip()
        if 'No module named' in details and any(name in details for name in ("'pxr'", "'PIL'")):
            details+='\nSet OpenUSD Python to a Python with usd-core and Pillow installed.'
        raise RuntimeError(details)
    print(result.stdout.strip())


def finish_prototype_edits(context):
    while context.scene.vecxy_usd.editing:
        if bpy.ops.vecxy.finish_prototype()!={'FINISHED'}:
            raise RuntimeError('Return to the scene before exporting')


def export_prototype(context,col):
    finish_prototype_edits(context)
    if not col.vecxy.is_prototype:raise RuntimeError('Collection is not marked as a Vecxy prototype')
    export_prototypes(context,[col],refresh=True)
    return resolved_path(col.vecxy.export_path)


def export_prototypes(context, prototypes, refresh):
    from . import characters
    visiting, exported, paths = set(), set(), {}

    def visit(col):
        if col in visiting:
            raise RuntimeError(f'Cyclic Shared Prototype reference: {col.name}')
        if col in exported:
            return
        path=resolved_path(col.vecxy.export_path)
        if path in paths:
            raise RuntimeError(f'{paths[path].name} and {col.name} use the same USD export path: {path}')
        paths[path]=col
        visiting.add(col)
        children={nested for obj in col.all_objects if (nested:=instance_collection(obj))
                  and not characters.is_character(nested)}
        for child in sorted(children,key=lambda c:c.name):
            visit(child)
        if refresh or not path.is_file():
            write_prototype(context,col)
        visiting.remove(col)
        exported.add(col)

    for col in sorted(prototypes,key=lambda c:c.name):
        visit(col)


def write_prototype(context,col):
    from . import characters
    path=resolved_path(col.vecxy.export_path);path.parent.mkdir(parents=True,exist_ok=True)
    linked=col.name in context.scene.collection.children
    if not linked:context.scene.collection.children.link(col)
    try:
        excluded = characters.character_objects()
        objects=[o for o in col.all_objects if not o.get('_vecxy_decal_preview')
                 and characters.scene_object(o, excluded)]
        if not objects:raise RuntimeError('Prototype collection is empty')
        temp_root=workspace()/'.agents/local';temp_root.mkdir(parents=True,exist_ok=True)
        with tempfile.TemporaryDirectory(prefix='usd-prototype-',dir=temp_root) as tmp:
            temporary=Path(tmp)/'prototype.usda'
            records=native_export(context,objects,temporary)
            finalize(context,temporary,path,records,metadata(col.vecxy,mesh_source={
                'depsgraph':context.evaluated_depsgraph_get(),'collection':col}),
                collection_offset=list(col.instance_offset))
    finally:
        if not linked and col.name in context.scene.collection.children:context.scene.collection.children.unlink(col)


def export_scene(context):
    from . import characters
    finish_prototype_edits(context)
    output=resolved_path(context.scene.vecxy_usd.export_path);output.parent.mkdir(parents=True,exist_ok=True)
    source_objects={o for col in bpy.data.collections if col.vecxy.is_prototype or col.vecxy.is_edit_proxy for o in col.all_objects}
    excluded = characters.character_objects()
    objects=[o for o in context.scene.objects if o not in source_objects and not o.hide_render
             and characters.scene_object(o, excluded)
             and not o.get('_vecxy_decal_preview')]
    prototypes={instance_collection(o) for o in objects if instance_collection(o)}
    export_prototypes(context,prototypes,refresh=context.scene.vecxy_usd.export_prototypes)
    temp_root=workspace()/'.agents/local';temp_root.mkdir(parents=True,exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='usd-scene-',dir=temp_root) as tmp:
        temporary=Path(tmp)/'scene.usda';records=native_export(context,objects,temporary)
        finalize(context,temporary,output,records)
    if context.scene.vecxy_usd.cook_after_export:
        repo=workspace()
        cook=next((repo/layout/'Code/Vecxy.Usd/Tools/cook_usd.py' for layout in ('Engine/Vecxy','Vendors/Vecxy') if (repo/layout/'Code/Vecxy.Usd/Tools/cook_usd.py').is_file()),Path('missing-cook_usd.py'))
        assets=next((p for p in output.parents if p.name=='Assets'),None)
        if not cook.exists() or assets is None:raise RuntimeError('Scene was exported. To cook automatically, export inside your Vecxy project Assets directory.')
        result=subprocess.run([python_executable(context),str(cook),str(output),'--assets',str(assets)],capture_output=True,text=True)
        if result.returncode:raise RuntimeError(result.stderr[-1500:])
        print(result.stdout.strip())
    return output
