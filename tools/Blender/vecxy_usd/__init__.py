bl_info = {
    'name': 'Vecxy USD Tools', 'author': 'Vecxy', 'version': (1, 0, 0),
    'blender': (5, 2, 0), 'location': '3D View > Sidebar > Vecxy',
    'description': 'Components, collider previews, shared prototypes and referenced USD export',
    'category': 'Import-Export',
}

import math
from pathlib import Path
import uuid
import bpy
from . import schema
from bpy.props import BoolProperty, CollectionProperty, EnumProperty, FloatProperty, FloatVectorProperty, IntProperty, PointerProperty, StringProperty
from mathutils import Matrix, Vector


def override_changed(param,context):
    if not param.is_override:return
    owner=param.id_data
    if not isinstance(owner,bpy.types.Object):return
    col=instance_collection(owner)
    if not col:return
    comp=next((c for c in owner.vecxy.components if any(p.as_pointer()==param.as_pointer() for p in c.parameters)),None)
    if not comp:return
    base=next((c for c in col.vecxy.components if c.key==comp.key),None)
    if not base:return
    value=schema.values(base).get(param.key)
    if value is None:return
    if param.kind=='VECTOR3':param.vector=tuple(value.values()) if isinstance(value,dict) else value
    else:setattr(param,{'FLOAT':'number','INT':'integer','BOOL':'boolean','STRING':'text','ENUM':'enum_value'}[param.kind],value)


class VECXY_Parameter(bpy.types.PropertyGroup):
    key: StringProperty(name='Parameter', default='value')
    is_override: BoolProperty(name='Override this field',default=True,update=override_changed)
    choices: StringProperty(default='[]')
    enum_value: EnumProperty(name='Value',items=schema.enum_items)
    kind: EnumProperty(name='Type', items=[(x,x.title(),'') for x in ('FLOAT','INT','BOOL','STRING','VECTOR3','ENUM')])
    number: FloatProperty(name='Value')
    integer: IntProperty(name='Value')
    boolean: BoolProperty(name='Value')
    text: StringProperty(name='Value')
    vector: FloatVectorProperty(name='Value', size=3)


class VECXY_Component(bpy.types.PropertyGroup):
    type_name: StringProperty(name='Component type', description='Full C# name of an AComponent with a Vecxy prototype', default='Game.MyComponent')
    key: StringProperty(name='Override key', default='component', description='Components with the same key override the prototype default')
    enabled: BoolProperty(name='Enabled', default=True)
    parameters: CollectionProperty(type=VECXY_Parameter)


class VECXY_Settings(bpy.types.PropertyGroup):
    identifier: StringProperty(options={'HIDDEN'})
    is_prototype: BoolProperty(name='Prototype', default=False)
    is_edit_proxy: BoolProperty(default=False,options={'HIDDEN'})
    asset_name: StringProperty(name='Asset name', default='Prototype')
    export_path: StringProperty(name='USD asset', subtype='FILE_PATH', options={'PATH_SUPPORTS_BLEND_RELATIVE'})
    override_collider: BoolProperty(name='Override collider', default=False)
    collider: EnumProperty(name='Collider', items=[('NONE','None',''),('BOX','Box',''),('SPHERE','Sphere',''),('CAPSULE','Capsule','')], default='NONE')
    collider_center: FloatVectorProperty(name='Center', size=3, subtype='TRANSLATION')
    collider_size: FloatVectorProperty(name='Size', size=3, default=(1,1,1), min=.001, subtype='XYZ')
    collider_radius: FloatProperty(name='Radius', default=.35, min=.001)
    collider_height: FloatProperty(name='Cylinder height', default=1.1, min=0)
    friction: FloatProperty(name='Friction', default=.5, min=0)
    restitution: FloatProperty(name='Restitution', default=0, min=0, max=1)
    trigger: BoolProperty(name='Trigger', default=False)
    role: StringProperty(name='Gameplay role', description='spawn, entrance, washer or another game marker')
    override_role: BoolProperty(name='Override role', default=False)
    override_tint: BoolProperty(name='Override color tint', default=False)
    tint: FloatVectorProperty(name='Tint', size=4, subtype='COLOR', default=(1,1,1,1), min=0,max=1)
    override_components: BoolProperty(name='Override components', default=False)
    components: CollectionProperty(type=VECXY_Component)
    show_collider: BoolProperty(name='Show collider', default=True)
    edit_defaults: BoolProperty(name='Common settings (all copies)', default=False, options={'SKIP_SAVE'})


class VECXY_SceneSettings(bpy.types.PropertyGroup):
    catalog_path: StringProperty(name='Component catalog',subtype='FILE_PATH', options={'PATH_SUPPORTS_BLEND_RELATIVE'},default='//vecxy.components.json')
    selected_component: EnumProperty(name='Component',items=schema.component_items)
    export_path: StringProperty(name='Scene USD', subtype='FILE_PATH', options={'PATH_SUPPORTS_BLEND_RELATIVE'}, default='//../Assets/Scenes/Laundromat.usda')
    python_path: StringProperty(name='OpenUSD Python', subtype='FILE_PATH', description='Python with usd-core and Pillow; auto-detects project .agents/local/usd-env')
    cook_after_export: BoolProperty(name='Prepare game cache', default=True)
    export_prototypes: BoolProperty(name='Export prototype files with scene', default=True)
    editing: PointerProperty(type=bpy.types.Collection)
    editing_proxy: PointerProperty(type=bpy.types.Collection)
    editing_instance: PointerProperty(type=bpy.types.Object)
    editing_hidden: BoolProperty(default=False)
    editing_matrix: StringProperty(default='')
    normalized_z_up: BoolProperty(default=True, options={'HIDDEN'})


def instance_collection(obj):
    col = obj.instance_collection if obj and obj.instance_type=='COLLECTION' else None
    return col if col and col.vecxy.is_prototype else None


def settings_owner(obj):
    if obj and not instance_collection(obj) and schema.collider_view(obj.vecxy).collider=='NONE' and obj.parent and schema.collider_view(obj.parent.vecxy).collider!='NONE':
        return obj.parent
    return obj


def current_settings(context):
    obj=settings_owner(context.object)
    if not obj:return None
    editing=context.scene.vecxy_usd.editing
    if editing and (obj.name in editing.all_objects or obj.get('vecxy_edit_original')):return editing.vecxy
    col=instance_collection(obj)
    return col.vecxy if col and obj.vecxy.edit_defaults else obj.vecxy


def effective_collider(obj):
    col=instance_collection(obj)
    return schema.collider_view(obj.vecxy,col.vecxy if col else None)


def ensure_id(settings):
    if not settings.identifier: settings.identifier=uuid.uuid4().hex
    return settings.identifier


def collection_bounds(col):
    points=[]
    for obj in col.all_objects:
        if obj.type=='MESH':
            points.extend(obj.matrix_world @ Vector(corner) for corner in obj.bound_box)
    return points


def local_bounds(obj):
    col=instance_collection(obj)
    if col:return collection_bounds(col)
    inv=obj.matrix_world.inverted_safe()
    return [inv @ child.matrix_world @ Vector(c) for child in [obj,*obj.children_recursive] if child.type=='MESH' for c in child.bound_box]


class VECXY_OT_fit_collider(bpy.types.Operator):
    bl_idname='vecxy.fit_collider';bl_label='Fit collider to geometry';bl_options={'REGISTER','UNDO'}
    def execute(self,context):
        obj=settings_owner(context.object);settings=current_settings(context)
        editing=context.scene.vecxy_usd.editing
        if editing and context.scene.vecxy_usd.editing_proxy:
            import json
            inverse=Matrix(json.loads(context.scene.vecxy_usd.editing_matrix)).inverted_safe()
            points=[inverse @ mesh.matrix_world @ Vector(c) for mesh in context.scene.vecxy_usd.editing_proxy.all_objects if mesh.type=='MESH' for c in mesh.bound_box]
        else:points=local_bounds(obj)
        if not points:self.report({'ERROR'},'No mesh geometry to fit');return {'CANCELLED'}
        lo=Vector(tuple(min(p[i] for p in points) for i in range(3)));hi=Vector(tuple(max(p[i] for p in points) for i in range(3)))
        component=next((c for c in settings.components if c.type_name in schema.COLLIDERS),None)
        if component is None:
            component=schema.add_component(settings,'Vecxy.Physics.BoxCollider')
        axis=schema.values(component).get('axis','Z');axis_index='XYZ'.index(axis)
        extents=hi-lo
        radial=[extents[i] for i in range(3) if i!=axis_index] if component.type_name.endswith('CapsuleCollider') else extents
        data={'center':tuple((lo+hi)*.5),'size':tuple(extents),'radius':max(radial)*.5}
        data['height']=max(0,extents[axis_index]-2*data['radius'])
        for field in schema.CATALOG[component.type_name]['fields']:
            if field['key'] in data:schema.set_param(component,field,data[field['key']])
        if instance_collection(obj) and settings==obj.vecxy:obj.vecxy.override_components=True
        return {'FINISHED'}


class VECXY_OT_mark_prototype(bpy.types.Operator):
    bl_idname='vecxy.mark_prototype';bl_label='Make shared prototype';bl_options={'REGISTER','UNDO'}
    def execute(self,context):
        obj=context.object
        if not obj:self.report({'ERROR'},'Select an object or hierarchy');return {'CANCELLED'}
        if obj.instance_type=='COLLECTION' and obj.instance_collection:
            col=obj.instance_collection
        else:
            members=set([obj,*obj.children_recursive,*context.selected_objects])
            col=bpy.data.collections.new(obj.name+'_Prototype')
            pivot=obj.matrix_world.copy();inv=pivot.inverted_safe()
            roots=[o for o in members if o.parent not in members]
            matrices={o:o.matrix_world.copy() for o in members}
            for member in members:
                for owner in list(member.users_collection):owner.objects.unlink(member)
                col.objects.link(member)
            for member in roots:
                member.parent=None;member.matrix_world=inv @ matrices[member]
            inst=bpy.data.objects.new(obj.name+'_Instance',None);context.collection.objects.link(inst)
            inst.instance_type='COLLECTION';inst.instance_collection=col;inst.matrix_world=pivot
            # The prototype owns original root defaults; the new instancer inherits them.
            copy_settings(obj.vecxy,col.vecxy)
            obj.vecxy.collider='NONE';obj.vecxy.role='';obj.vecxy.components.clear()
            context.view_layer.objects.active=inst;inst.select_set(True)
        col.vecxy.is_prototype=True;col.vecxy.asset_name=col.name.removesuffix('_Prototype')
        if not col.vecxy.export_path:col.vecxy.export_path='//../Assets/Scenes/Prototypes/'+safe_name(col.vecxy.asset_name)+'.usda'
        ensure_id(col.vecxy)
        return {'FINISHED'}


def copy_settings(source,target):
    for prop in source.bl_rna.properties:
        key=prop.identifier
        if key not in ('rna_type','components','identifier') and not prop.is_readonly:
            setattr(target,key,getattr(source,key))
    for comp in source.components:
        new=target.components.add();new.type_name=comp.type_name;new.key=comp.key;new.enabled=comp.enabled
        for param in comp.parameters:
            p=new.parameters.add()
            for key in ('key','kind','number','integer','boolean','text','vector','is_override','choices'):setattr(p,key,getattr(param,key))
            if param.kind=='ENUM':p.enum_value=param.enum_value


def safe_name(value):
    import re
    return re.sub(r'[^A-Za-z0-9_]+','_',value).strip('_') or 'Prototype'


class VECXY_OT_create_instance(bpy.types.Operator):
    bl_idname='vecxy.create_instance';bl_label='Create linked instance';bl_options={'REGISTER','UNDO'}
    def execute(self,context):
        col=instance_collection(context.object)
        if not col:self.report({'ERROR'},'Select a prototype instance first');return {'CANCELLED'}
        original=context.object
        obj=bpy.data.objects.new(col.vecxy.asset_name+'_Instance',None);context.collection.objects.link(obj)
        obj.instance_type='COLLECTION';obj.instance_collection=col;obj.matrix_world=original.matrix_world.copy();obj.location.x+=1.5
        for selected in context.selected_objects:selected.select_set(False)
        obj.select_set(True);context.view_layer.objects.active=obj
        return {'FINISHED'}


class VECXY_OT_edit_prototype(bpy.types.Operator):
    bl_idname='vecxy.edit_prototype';bl_label='Edit shared model here';bl_options={'REGISTER','UNDO'}
    def execute(self,context):
        import json
        settings=context.scene.vecxy_usd
        if settings.editing:self.report({'ERROR'},'Finish the current shared-model edit first');return {'CANCELLED'}
        instance=context.object;col=instance_collection(instance)
        if not col:self.report({'ERROR'},'Select a prototype instance first');return {'CANCELLED'}
        proxy=bpy.data.collections.new('Vecxy_Shared_Edit');proxy.vecxy.is_edit_proxy=True
        context.scene.collection.children.link(proxy)
        placement=instance.matrix_world @ Matrix.Translation(-col.instance_offset)
        settings.editing=col;settings.editing_proxy=proxy;settings.editing_instance=instance
        settings.editing_hidden=instance.hide_get();settings.editing_matrix=json.dumps([list(row) for row in placement])
        for selected in context.selected_objects:selected.select_set(False)
        instance.hide_set(True)
        # Keep placement on a parent: decomposing nonuniform scale and a rotated
        # child's world matrix into TRS would introduce a shape-changing shear loss.
        root=bpy.data.objects.new('Vecxy_Edit_Placement',None);proxy.objects.link(root)
        root.matrix_world=placement;root.hide_select=True
        copies={}
        for original in col.all_objects:
            copy=original.copy();proxy.objects.link(copy);copy['vecxy_edit_original']=original.name
            copy.hide_set(False);copies[original]=copy
        for original,copy in copies.items():
            if original.parent in copies:copy.parent=copies[original.parent]
            else:
                copy.parent=root;copy.matrix_parent_inverse=Matrix.Identity(4);copy.matrix_basis=original.matrix_world.copy()
            copy.select_set(copy.type=='MESH')
        context.view_layer.update()
        active=next((o for o in copies.values() if o.type=='MESH'),None)
        if active:context.view_layer.objects.active=active
        # Keep the existing viewpoint, position and instance scale. Mesh data is shared
        # so edits appear on all instances while the source transforms stay untouched.
        return {'FINISHED'}


class VECXY_OT_finish_edit(bpy.types.Operator):
    bl_idname='vecxy.finish_prototype';bl_label='Finish editing shared model';bl_options={'REGISTER','UNDO'}
    def execute(self,context):
        import json
        settings=context.scene.vecxy_usd;proxy=settings.editing_proxy
        if proxy:
            placement=Matrix(json.loads(settings.editing_matrix));inverse=placement.inverted_safe()
            pairs=[(o,bpy.data.objects.get(o.get('vecxy_edit_original',''))) for o in proxy.all_objects]
            def depth(obj):
                result=0
                while obj.parent:
                    result+=1;obj=obj.parent
                return result
            pairs.sort(key=lambda pair:depth(pair[0]))
            for copy,original in pairs:
                if original:original.matrix_world=inverse @ copy.matrix_world
            for copy,_ in pairs:bpy.data.objects.remove(copy,do_unlink=True)
            bpy.data.collections.remove(proxy)
        instance=settings.editing_instance
        if instance:
            instance.hide_set(settings.editing_hidden);instance.select_set(True);context.view_layer.objects.active=instance
        settings.editing=None;settings.editing_proxy=None;settings.editing_instance=None;settings.editing_matrix=''
        return {'FINISHED'}


class VECXY_OT_component(bpy.types.Operator):
    bl_idname='vecxy.component';bl_label='Component';bl_options={'REGISTER','UNDO'}
    index:IntProperty(default=-1)
    def execute(self,context):
        settings=current_settings(context)
        if self.index<0:
            try:schema.add_component(settings,context.scene.vecxy_usd.selected_component)
            except RuntimeError as error:self.report({'ERROR'},str(error));return {'CANCELLED'}
        else:settings.components.remove(self.index)
        if instance_collection(context.object) and settings==context.object.vecxy:settings.override_components=True
        return {'FINISHED'}


class VECXY_OT_override_component(bpy.types.Operator):
    bl_idname='vecxy.override_component';bl_label='Override component';bl_options={'REGISTER','UNDO'}
    index:IntProperty()
    def execute(self,context):
        obj=context.object;col=instance_collection(obj)
        if not col:return {'CANCELLED'}
        source=col.vecxy.components[self.index]
        if any(c.key==source.key for c in obj.vecxy.components):return {'FINISHED'}
        own=schema.add_component(obj.vecxy,source.type_name,override=True,key=source.key);own.enabled=source.enabled
        obj.vecxy.override_components=True
        return {'FINISHED'}


class VECXY_OT_reset(bpy.types.Operator):
    bl_idname='vecxy.reset_overrides';bl_label='Reset instance overrides';bl_options={'REGISTER','UNDO'}
    def execute(self,context):
        s=context.object.vecxy;s.override_collider=False;s.override_role=False;s.override_components=False;s.override_tint=False;s.components.clear()
        return {'FINISHED'}


class VECXY_OT_export_scene(bpy.types.Operator):
    bl_idname='vecxy.export_scene';bl_label='Export scene USD'
    def execute(self,context):
        from .exporter import export_scene
        try:export_scene(context)
        except Exception as error:self.report({'ERROR'},str(error));return {'CANCELLED'}
        self.report({'INFO'},'Vecxy scene exported');return {'FINISHED'}


class VECXY_OT_export_prototype(bpy.types.Operator):
    bl_idname='vecxy.export_prototype';bl_label='Export this prototype USD'
    def execute(self,context):
        from .exporter import export_prototype
        col=instance_collection(context.object) or context.scene.vecxy_usd.editing
        if not col:self.report({'ERROR'},'Select a prototype instance');return {'CANCELLED'}
        try:export_prototype(context,col)
        except Exception as error:self.report({'ERROR'},str(error));return {'CANCELLED'}
        self.report({'INFO'},'Only the prototype asset was updated');return {'FINISHED'}


class VECXY_OT_field_help(bpy.types.Operator):
    bl_idname='vecxy.field_help';bl_label='Component field information'
    type_name:StringProperty();key:StringProperty()
    def execute(self,context):
        definition=schema.CATALOG[self.type_name];field=next(f for f in definition['fields'] if f['key']==self.key)
        text=field.get('description') or field['label']
        if field.get('unit'):text+='; unit: '+field['unit']
        if field.get('min') is not None:text+='; min: '+str(field['min'])
        if field.get('max') is not None:text+='; max: '+str(field['max'])
        self.report({'INFO'},text);return {'FINISHED'}


class VECXY_OT_reload_catalog(bpy.types.Operator):
    bl_idname='vecxy.reload_catalog';bl_label='Reload project components'
    def execute(self,context):
        try:
            schema.reload_catalog(context,required=True)
            for obj in bpy.data.objects:
                for component in obj.vecxy.components:schema.ensure_fields(component,overrides=bool(instance_collection(obj)))
            for col in bpy.data.collections:
                for component in col.vecxy.components:schema.ensure_fields(component)
        except Exception as error:self.report({'ERROR'},str(error));return {'CANCELLED'}
        self.report({'INFO'},str(len(schema.CATALOG))+' project/engine components loaded')
        return {'FINISHED'}


class VECXY_PT_tools(bpy.types.Panel):
    bl_label='Vecxy USD';bl_idname='VECXY_PT_tools';bl_space_type='VIEW_3D';bl_region_type='UI';bl_category='Vecxy'
    def draw(self,context):
        layout=self.layout;scene=context.scene.vecxy_usd
        if scene.editing:
            layout.label(text='Editing: '+scene.editing.vecxy.asset_name)
            layout.operator('vecxy.finish_prototype',icon='LOOP_BACK')
            layout.operator('vecxy.export_prototype',icon='EXPORT')
        obj=settings_owner(context.object)
        if obj:
            if obj!=context.object:layout.label(text='Components on: '+obj.name,icon='OBJECT_DATA')
            col=instance_collection(obj)
            if col:
                layout.label(text='Shared model: '+col.vecxy.asset_name,icon='OUTLINER_COLLECTION')
                layout.label(text='Instance: '+obj.name)
                layout.operator('vecxy.create_instance',text='Create linked copy')
                layout.operator('vecxy.edit_prototype',text='Edit shared model here')
                layout.prop(obj.vecxy,'edit_defaults')
                if obj.vecxy.edit_defaults:
                    layout.prop(col.vecxy,'asset_name');layout.prop(col.vecxy,'export_path')
                else:
                    layout.operator('vecxy.reset_overrides')
                layout.operator('vecxy.export_prototype',icon='EXPORT')
            elif not scene.editing:layout.operator('vecxy.mark_prototype',icon='OUTLINER_COLLECTION')
            settings=current_settings(context)
            inherited=col and not obj.vecxy.edit_defaults
            layout.prop(obj.vecxy,'show_collider')
            if inherited:layout.prop(settings,'override_role')
            row=layout.row();row.enabled=not inherited or settings.override_role;row.prop(settings,'role')
            layout.prop(settings,'override_tint')
            if settings.override_tint:layout.prop(settings,'tint')
            box=layout.box();box.label(text='Components')
            if inherited:
                for i,default in enumerate(col.vecxy.components):
                    row=box.row();row.label(text='Inherits: '+default.key)
                    op=row.operator('vecxy.override_component',text='Override');op.index=i
                box.label(text='Local overrides below; unchecked fields inherit.')
            body=box.column()
            for i,comp in enumerate(settings.components):
                definition=schema.CATALOG.get(comp.type_name)
                sub=body.box();row=sub.row();row.prop(comp,'enabled',text='')
                row.label(text=definition['label'] if definition else comp.type_name,icon='MODIFIER')
                op=row.operator('vecxy.component',text='',icon='X');op.index=i
                if not definition:
                    sub.label(text='Not in project catalog. Build and reload.',icon='ERROR');continue
                for field in definition['fields']:
                    param=next((p for p in comp.parameters if p.key==field['key']),None)
                    if param is None:continue
                    row=sub.row(align=True)
                    if inherited:row.prop(param,'is_override',text='')
                    if inherited and not param.is_override:
                        base=next((c for c in col.vecxy.components if c.key==comp.key),None)
                        value=schema.values(base).get(field['key'],field['default']) if base else field['default']
                        if isinstance(value,dict):value=', '.join(f'{v:g}' for v in value.values())
                        row.label(text=field['label']+': '+str(value))
                    else:
                        controls=row.row()
                        controls.prop(param,{'FLOAT':'number','INT':'integer','BOOL':'boolean','STRING':'text','VECTOR3':'vector','ENUM':'enum_value'}[param.kind],text=field['label'])
                    if field.get('description') or field.get('unit'):
                        op=row.operator('vecxy.field_help',text='',icon='INFO');op.type_name=comp.type_name;op.key=field['key']
                if comp.type_name in schema.COLLIDERS:sub.operator('vecxy.fit_collider',text='Fit to model')
            body.prop(scene,'selected_component',text='')
            body.operator('vecxy.component',text='Add selected component',icon='ADD')
        layout.separator();layout.prop(scene,'catalog_path');layout.operator('vecxy.reload_catalog',icon='FILE_REFRESH')
        layout.separator();layout.prop(scene,'export_path');layout.prop(scene,'python_path');layout.prop(scene,'export_prototypes');layout.prop(scene,'cook_after_export')
        layout.operator('vecxy.export_scene',icon='EXPORT')


def draw_colliders():
    import gpu
    from gpu_extras.batch import batch_for_shader
    context=bpy.context
    if context.mode!='OBJECT':return
    shader=gpu.shader.from_builtin('UNIFORM_COLOR')
    gpu.state.blend_set('ALPHA');gpu.state.depth_test_set('LESS_EQUAL');gpu.state.line_width_set(2)
    try:
        drawn=set()
        for obj in context.selected_objects:
            obj=settings_owner(obj)
            if not obj.vecxy.show_collider:continue
            editing=context.scene.vecxy_usd.editing
            source_edit=editing and (obj.name in editing.all_objects or obj.get('vecxy_edit_original'))
            key=editing if source_edit else obj
            if key in drawn:continue
            drawn.add(key)
            settings=schema.collider_view(editing.vecxy) if source_edit else effective_collider(obj)
            if settings.collider=='NONE':continue
            if source_edit:
                import json
                matrix=Matrix(json.loads(context.scene.vecxy_usd.editing_matrix)) if context.scene.vecxy_usd.editing_matrix else Matrix.Identity(4)
            else:matrix=obj.matrix_world
            center=Vector(settings.collider_center);points=[]
            axis=getattr(settings,'collider_axis','Z')
            shape_rotation=Matrix.Rotation(-math.pi/2,3,'X') if settings.collider=='CAPSULE' and axis=='Y' else Matrix.Rotation(math.pi/2,3,'Y') if settings.collider=='CAPSULE' and axis=='X' else Matrix.Identity(3)
            def line(a,b):points.extend([matrix@(center+shape_rotation@Vector(a)),matrix@(center+shape_rotation@Vector(b))])
            if settings.collider=='BOX':
                half=Vector(settings.collider_size)*.5;corners=[Vector((half.x*x,half.y*y,half.z*z)) for x in (-1,1) for y in (-1,1) for z in (-1,1)]
                for i in range(8):
                    for bit in (1,2,4):
                        if i<(i^bit):line(corners[i],corners[i^bit])
            else:
                r=settings.collider_radius;h=settings.collider_height*.5 if settings.collider=='CAPSULE' else 0
                for axis in range(3):
                    for k in range(48):
                        def point(t):
                            a=math.tau*t/48;v=[0.,0.,0.];v[(axis+1)%3]=r*math.cos(a);v[(axis+2)%3]=r*math.sin(a)
                            if h:v[2]+=h if v[2]>=0 else -h
                            return v
                        line(point(k),point(k+1))
                if h:
                    for k in range(48):
                        a=math.tau*k/48;b=math.tau*(k+1)/48
                        line((r*math.cos(a),r*math.sin(a),-h),(r*math.cos(b),r*math.sin(b),-h))
            if points:
                batch=batch_for_shader(shader,'LINES',{'pos':points});shader.bind();shader.uniform_float('color',(.15,1,.45,.85));batch.draw(shader)
    finally:gpu.state.line_width_set(1);gpu.state.depth_test_set('NONE');gpu.state.blend_set('NONE')


CLASSES=(VECXY_Parameter,VECXY_Component,VECXY_Settings,VECXY_SceneSettings,VECXY_OT_fit_collider,VECXY_OT_mark_prototype,VECXY_OT_create_instance,VECXY_OT_edit_prototype,VECXY_OT_finish_edit,VECXY_OT_component,VECXY_OT_override_component,VECXY_OT_reset,VECXY_OT_field_help,VECXY_OT_reload_catalog,VECXY_OT_export_scene,VECXY_OT_export_prototype,VECXY_PT_tools)
_handle=None

@bpy.app.handlers.persistent
def load_project_catalog(dummy):
    if getattr(bpy.data,'filepath',''):
        try:
            schema.reload_catalog(bpy.context)
            for owner in [*bpy.data.objects,*bpy.data.collections]:
                for component in owner.vecxy.components:schema.ensure_fields(component,overrides=isinstance(owner,bpy.types.Object) and bool(instance_collection(owner)))
        except Exception as error:print('Vecxy catalog:',error)


def register():
    global _handle
    for cls in CLASSES:bpy.utils.register_class(cls)
    bpy.types.Object.vecxy=PointerProperty(type=VECXY_Settings)
    bpy.types.Collection.vecxy=PointerProperty(type=VECXY_Settings)
    bpy.types.Scene.vecxy_usd=PointerProperty(type=VECXY_SceneSettings)
    bpy.app.handlers.load_post.append(load_project_catalog)
    load_project_catalog(None)
    if not bpy.app.background:_handle=bpy.types.SpaceView3D.draw_handler_add(draw_colliders,(),'WINDOW','POST_VIEW')

def unregister():
    global _handle
    if load_project_catalog in bpy.app.handlers.load_post:bpy.app.handlers.load_post.remove(load_project_catalog)
    if _handle:bpy.types.SpaceView3D.draw_handler_remove(_handle,'WINDOW');_handle=None
    del bpy.types.Scene.vecxy_usd;del bpy.types.Collection.vecxy;del bpy.types.Object.vecxy
    for cls in reversed(CLASSES):bpy.utils.unregister_class(cls)
