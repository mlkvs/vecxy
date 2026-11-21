"""Author projected decals using the same component data as the runtime."""
from hashlib import sha256
import math
from pathlib import Path
import shutil

import bpy
from bpy.props import BoolProperty, FloatProperty, FloatVectorProperty, IntProperty, StringProperty
from bpy_extras import view3d_utils
from mathutils import Matrix, Quaternion, Vector

from . import schema
from .level_design import select_only

TYPE = 'Vecxy.Rendering.Decal'
_EXTENSIONS = {'.png', '.jpg', '.jpeg', '.bmp', '.tga', '.ppm'}
_ACTIVE = None


def asset_directory(context):
    candidates = (Path(bpy.path.abspath(context.scene.vecxy_usd.export_path)).resolve(),
                  schema.catalog_path(context).parent.parent / 'Assets')
    for candidate in candidates:
        for directory in (candidate, *candidate.parents):
            if directory.name == 'Assets' and directory.is_dir():
                return directory
    raise ValueError('Set the USD export path inside the project Assets directory')


def asset_file(context, value):
    root = asset_directory(context)
    definition = schema.CATALOG.get(TYPE, {})
    for field in definition.get('fields', []):
        if field['key'] == 'texture':
            value = next((asset['path'] for asset in field.get('assets', []) if asset['id'] == value), value)
            break
    path = (root / value).resolve()
    if not path.is_relative_to(root) or path.suffix.lower() not in _EXTENSIONS or not path.is_file():
        return None
    return path


def decal_component(obj):
    return next((component for component in obj.vecxy.components if component.type_name == TYPE), None) if obj else None


def set_value(component, name, value):
    field = next(f for f in schema.CATALOG[TYPE]['fields'] if f['key'] == name)
    schema.set_param(component, field, value)


class VECXY_DecalSettings(bpy.types.PropertyGroup):
    texture: StringProperty(name='Texture')
    size: FloatVectorProperty(name='Size (X, Y, depth)', size=3, default=(1, 1, .2), min=.001, subtype='XYZ')
    opacity: FloatProperty(name='Opacity', default=1, min=0, max=1)
    angle: FloatProperty(name='Surface angle', default=75, min=0, max=90)
    layer: IntProperty(name='Layer order')
    attach: BoolProperty(name='Attach to surface', description='Parent an applied decal to the hit object')
    preview: BoolProperty(name='Show projected textures', default=True)


class VECXY_OT_decal_texture(bpy.types.Operator):
    bl_idname = 'vecxy.decal_texture'
    bl_label = 'Choose decal texture'
    bl_description = 'Select an image in Assets or import a copy into Assets/Decals'
    bl_options = {'REGISTER', 'UNDO'}
    filepath: StringProperty(subtype='FILE_PATH')
    filter_glob: StringProperty(default='*.png;*.jpg;*.jpeg;*.bmp;*.tga;*.ppm', options={'HIDDEN'})
    replace: BoolProperty(options={'SKIP_SAVE'})

    def invoke(self, context, event):
        try:
            self.filepath = str(asset_directory(context)) + '/'
        except ValueError as error:
            self.report({'ERROR'}, str(error))
            return {'CANCELLED'}
        context.window_manager.fileselect_add(self)
        return {'RUNNING_MODAL'}

    def execute(self, context):
        comp = decal_component(context.object) if self.replace else None
        if self.replace and (comp is None or TYPE not in schema.CATALOG):
            self.report({'ERROR'}, 'Select a decal with a loaded project catalog')
            return {'CANCELLED'}
        try:
            root = asset_directory(context)
            path = Path(bpy.path.abspath(self.filepath)).resolve()
            if path.suffix.lower() not in _EXTENSIONS or not path.is_file():
                raise ValueError('Select a supported image file')
            if not path.is_relative_to(root):
                source = path
                folder = root / 'Decals'
                folder.mkdir(exist_ok=True)
                path = folder / source.name
                content = source.read_bytes()
                if path.exists() and path.read_bytes() != content:
                    path = folder / f'{source.stem}_{sha256(content).hexdigest()[:12]}{source.suffix.lower()}'
                if not path.exists():
                    shutil.copyfile(source, path)
            relative = path.relative_to(root).as_posix()
            if self.replace:
                set_value(comp, 'texture', relative)
            else:
                context.scene.vecxy_decals.texture = relative
        except (ValueError, OSError, RuntimeError) as error:
            self.report({'ERROR'}, str(error))
            return {'CANCELLED'}
        return {'FINISHED'}


class VECXY_OT_place_decal(bpy.types.Operator):
    bl_idname = 'vecxy.place_decal'
    bl_label = 'Place decal on surface'
    bl_options = {'REGISTER', 'UNDO'}
    move: BoolProperty(options={'SKIP_SAVE'})

    @classmethod
    def poll(cls, context):
        return context.mode == 'OBJECT' and context.area and context.area.type == 'VIEW_3D'

    def invoke(self, context, event):
        global _ACTIVE
        if _ACTIVE is not None:
            return {'CANCELLED'}
        if TYPE not in schema.CATALOG:
            self.report({'ERROR'}, 'Generate and reload the project component catalog')
            return {'CANCELLED'}
        defaults = context.scene.vecxy_decals
        comp = decal_component(context.object) if self.move else None
        if self.move and comp is None:
            self.report({'ERROR'}, 'Select a decal first')
            return {'CANCELLED'}
        try:
            texture = schema.values(comp)['texture'] if comp else defaults.texture
            if not asset_file(context, texture):
                raise ValueError('Choose an existing decal texture')
        except (ValueError, OSError, RuntimeError) as error:
            self.report({'ERROR'}, str(error))
            return {'CANCELLED'}
        self.area = context.area
        self.region = next(region for region in self.area.regions if region.type == 'WINDOW')
        self.view = context.space_data.region_3d
        self.obj = context.object if self.move else bpy.data.objects.new(Path(texture).stem + ' Decal', None)
        if self.move:
            self.original = self.obj.matrix_world.copy()
        else:
            collection = context.scene.vecxy_usd.editing or context.collection
            collection.objects.link(self.obj)
            self.obj.empty_display_type = 'ARROWS'
            self.obj.empty_display_size = .2
            self.obj['_vecxy_decal_preview'] = True
            comp = schema.add_component(self.obj.vecxy, TYPE)
            for name, value in (('texture', texture), ('size', defaults.size), ('opacity', defaults.opacity),
                                ('maxSurfaceAngle', defaults.angle), ('order', defaults.layer)):
                set_value(comp, name, value)
        self.angle = 0.0
        self.valid = False
        self.receiver = None
        self.attach = defaults.attach
        _ACTIVE = self
        context.window_manager.modal_handler_add(self)
        self.area.header_text_set('LMB: place decal   R / Shift+R: rotate   Esc: cancel')
        self.update(context, event)
        return {'RUNNING_MODAL'}

    def update(self, context, event):
        from .prototype_preview import resolve_hit_owner

        mouse = event.mouse_x - self.region.x, event.mouse_y - self.region.y
        self.valid = False
        if 0 <= mouse[0] < self.region.width and 0 <= mouse[1] < self.region.height:
            origin = view3d_utils.region_2d_to_origin_3d(self.region, self.view, mouse, clamp=10000)
            direction = view3d_utils.region_2d_to_vector_3d(self.region, self.view, mouse)
            graph = context.evaluated_depsgraph_get()
            hit, location, normal, _, receiver, matrix = context.scene.ray_cast(graph, origin, direction)
            if hit:
                if normal.dot(direction) > 0:
                    normal = -normal
                rotation = normal.to_track_quat('Z', 'Y') @ Quaternion((0, 0, 1), self.angle)
                scale = self.original.to_scale() if self.move else Vector((1, 1, 1))
                self.obj.matrix_world = Matrix.LocRotScale(location, rotation, scale)
                self.receiver = resolve_hit_owner(context, graph, receiver, matrix)
                self.valid = True
        self.area.tag_redraw()

    def modal(self, context, event):
        if _ACTIVE is not self:
            return {'CANCELLED'}
        if event.value == 'PRESS':
            if event.type in {'ESC', 'RIGHTMOUSE'}:
                self.finish(context, cancel=True)
                return {'CANCELLED'}
            if event.type == 'R':
                self.angle += math.pi / 12 * (-1 if event.shift else 1)
            if event.type == 'LEFTMOUSE' and self.valid:
                self.finish(context)
                return {'FINISHED'}
        if event.type in {'MIDDLEMOUSE', 'WHEELUPMOUSE', 'WHEELDOWNMOUSE', 'NDOF_MOTION'} or event.type.startswith('NUMPAD'):
            return {'PASS_THROUGH'}
        self.update(context, event)
        return {'RUNNING_MODAL'}

    def finish(self, context, cancel=False):
        global _ACTIVE
        _ACTIVE = None
        if cancel:
            if self.move:
                self.obj.matrix_world = self.original
            else:
                bpy.data.objects.remove(self.obj, do_unlink=True)
        else:
            if self.attach and self.receiver and context.view_layer.objects.get(self.receiver.name) == self.receiver:
                world = self.obj.matrix_world.copy()
                self.obj.parent = self.receiver
                self.obj.matrix_parent_inverse = self.receiver.matrix_world.inverted_safe()
                self.obj.matrix_world = world
            self.obj.pop('_vecxy_decal_preview', None)
            select_only(context, [self.obj])
        self.area.header_text_set(None)
        self.area.tag_redraw()

    def cancel(self, context):
        if _ACTIVE is self:
            self.finish(context, cancel=True)


class VECXY_PT_decals(bpy.types.Panel):
    bl_label = 'Decals'
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = 'Vecxy'
    bl_order = 20

    def draw(self, context):
        layout = self.layout
        options = context.scene.vecxy_decals
        layout.prop(options, 'preview')
        section = layout.box()
        section.label(text=options.texture or 'Select a texture', icon='IMAGE_DATA')
        section.operator('vecxy.decal_texture', icon='FILE_FOLDER').replace = False
        section.prop(options, 'size')
        section.prop(options, 'opacity')
        section.prop(options, 'angle')
        section.prop(options, 'layer')
        section.prop(options, 'attach')
        section.operator('vecxy.place_decal', icon='CURSOR').move = False
        component = decal_component(context.object)
        if component:
            box = layout.box()
            box.label(text=context.object.name, icon='OBJECT_DATA')
            box.prop(component, 'enabled')
            texture = next((p.text for p in component.parameters if p.key == 'texture'), '')
            box.label(text=texture or 'Select a texture', icon='IMAGE_DATA')
            box.operator('vecxy.decal_texture', text='Replace texture', icon='FILE_FOLDER').replace = True
            for field in schema.CATALOG.get(TYPE, {}).get('fields', []):
                parameter = next((p for p in component.parameters if p.key == field['key']), None)
                if parameter and parameter.kind != 'STRING':
                    box.prop(parameter, {'VECTOR3': 'vector', 'FLOAT': 'number', 'INT': 'integer'}[parameter.kind],
                             text=field['label'])
            box.operator('vecxy.place_decal', text='Reposition decal', icon='ORIENTATION_GLOBAL').move = True


@bpy.app.handlers.persistent
def cancel_placement(_):
    if _ACTIVE:
        _ACTIVE.finish(bpy.context, cancel=True)


CLASSES = (VECXY_DecalSettings, VECXY_OT_decal_texture, VECXY_OT_place_decal, VECXY_PT_decals)


def register():
    from . import decal_preview
    for cls in CLASSES:
        bpy.utils.register_class(cls)
    bpy.types.Scene.vecxy_decals = bpy.props.PointerProperty(type=VECXY_DecalSettings)
    bpy.app.handlers.save_pre.append(cancel_placement)
    bpy.app.handlers.load_pre.append(cancel_placement)
    decal_preview.register()


def unregister():
    from . import decal_preview
    decal_preview.unregister()
    cancel_placement(None)
    bpy.app.handlers.save_pre.remove(cancel_placement)
    bpy.app.handlers.load_pre.remove(cancel_placement)
    del bpy.types.Scene.vecxy_decals
    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)
