"""Collider-style handles and volume selection for navigation authoring."""
from types import SimpleNamespace

import bpy
from bpy.props import EnumProperty, StringProperty
from bpy_extras import view3d_utils
from mathutils import Matrix, Vector

from . import collider_frame, current_settings, instance_collection, prototype_preview, schema, settings_owner
from . import collider_edit, navigation

OBSTACLE = navigation.OBSTACLE
_keymap = None


def obstacle_settings(obj, settings=None):
    collection = instance_collection(obj)
    local = obj.vecxy if settings is None else settings
    defaults = collection.vecxy if collection and local == obj.vecxy else None
    return next(((comp, data) for comp, data in schema.effective_components(local, defaults)
                 if comp.enabled and comp.type_name == OBSTACLE), None)


def obstacle_box(matrix, data):
    center = navigation.vector(data, 'center', (0, 0, 0))
    size = navigation.vector(data, 'size', (1, 1, 1))
    return matrix @ Matrix.Translation(center) @ Matrix.Diagonal(Vector((*size, 1)))


def pickable(context, obj):
    return obj and not obj.hide_select and context.view_layer.objects.get(obj.name) == obj and obj.visible_get(
        view_layer=context.view_layer, viewport=context.space_data)


def custom_boxes(context):
    for obj in context.visible_objects:
        if navigation.is_authoring_volume(obj):
            continue
        matrix = collider_frame(context, obj)[1] if obj == context.object else obj.matrix_world
        current = current_settings(context) if obj == context.object and settings_owner(obj) == obj else None
        entry = obstacle_settings(obj, current)
        if entry and not entry[1].get('useColliders', True):
            yield obj, obstacle_box(matrix, entry[1])


def hit_box(origin, direction, matrix):
    """Distance along the world ray to the first face of a transformed unit box."""
    try:
        inverse = matrix.inverted()
    except ValueError:
        return None
    local = inverse @ origin
    ray = inverse.to_3x3() @ direction
    near, far = -float('inf'), float('inf')
    for position, speed in zip(local, ray):
        if abs(speed) < 1e-9:
            if abs(position) > .5:
                return None
            continue
        a, b = sorted(((-.5 - position) / speed, (.5 - position) / speed))
        near, far = max(near, a), min(far, b)
        if near > far:
            return None
    return near if near >= 0 else far if far >= 0 else None


class VECXY_OT_pick_navigation_volume(bpy.types.Operator):
    bl_idname = 'vecxy.pick_navigation_volume'
    bl_label = 'Select navigation volume'
    bl_options = {'INTERNAL'}

    @classmethod
    def poll(cls, context):
        return bool(context.mode == 'OBJECT' and context.region and context.region.type == 'WINDOW' and
                context.region_data and context.space_data.overlay.show_overlays and
                navigation.options(context).preview_blockers and not collider_edit.edit_active())

    def invoke(self, context, event):
        # Leave clicks for transform, measurement and other active tools alone.
        tool = context.workspace.tools.from_space_view3d_mode('OBJECT', create=False)
        if tool and tool.idname not in {'builtin.select', 'builtin.select_box'}:
            return {'PASS_THROUGH'}
        mouse = Vector((event.mouse_region_x, event.mouse_region_y))
        origin = view3d_utils.region_2d_to_origin_3d(context.region, context.region_data, mouse, clamp=10000)
        direction = view3d_utils.region_2d_to_vector_3d(context.region, context.region_data, mouse)
        graph = context.evaluated_depsgraph_get()
        hit, point, _, _, hit_object, matrix = context.scene.ray_cast(graph, origin, direction)
        limit = (point - origin).length + .001 if hit else float('inf')
        owner = None

        candidates = [(obj, obj.matrix_world) for obj in context.visible_objects if obj.get(navigation.BLOCKER)]
        candidates.extend(custom_boxes(context))
        for obj, transform in candidates:
            if pickable(context, obj) and (distance := hit_box(origin, direction, transform)) is not None and distance < limit:
                limit, owner = distance, obj

        # A collider-backed obstacle can live on an Empty above its visual mesh.
        if owner is None and hit:
            root = prototype_preview.resolve_hit_owner(context, graph, hit_object, matrix)
            target = root
            while target:
                if pickable(context, target) and obstacle_settings(target):
                    owner = target if target != hit_object.original else None
                    break
                target = target.parent
        if owner is None:
            return {'PASS_THROUGH'}
        if not event.shift:
            for obj in context.selected_objects:
                obj.select_set(False)
        owner.select_set(True)
        context.view_layer.objects.active = owner
        context.area.tag_redraw()
        return {'FINISHED'}


class VECXY_OT_edit_navigation_box(collider_edit.FaceHandleEditor, bpy.types.Operator):
    bl_idname = 'vecxy.edit_navigation_box'
    bl_label = 'Edit navigation box in viewport'
    bl_options = {'REGISTER', 'UNDO'}

    kind: EnumProperty(items=[
        ('AREA', 'Bake Area', ''), ('BLOCKER', 'Blocker', ''), ('OBSTACLE', 'Obstacle', '')
    ], options={'SKIP_SAVE'})
    key: StringProperty(options={'SKIP_SAVE'})

    def invoke(self, context, event):
        self.component = None
        if self.kind == 'OBSTACLE':
            self.obj = settings_owner(context.object)
            if self.obj is None:
                return {'CANCELLED'}
            settings = current_settings(context)
            entry = obstacle_settings(self.obj, settings)
            self.component = next((comp for comp in settings.components if comp.key == self.key and comp.enabled), None)
            if self.component is None or entry is None or entry[1].get('useColliders', True):
                return {'CANCELLED'}
            self.owner = collider_frame(context, self.obj)[0]
            self.snapshot = collider_edit.snapshot_parameters(self.component, {'center', 'size'})
            self.fields = {field['key']: field for field in schema.CATALOG[OBSTACLE]['fields']}
        else:
            self.obj = navigation.bake_area(context) if self.kind == 'AREA' else context.object
            if not self.obj or (self.kind == 'BLOCKER' and not self.obj.get(navigation.BLOCKER)):
                return {'CANCELLED'}
            self.owner = self.obj
            self.original = self.obj.matrix_world.copy()
            size = Vector(tuple(self.original.to_3x3().col[i].length for i in range(3)))
            if min(size) < 1e-6:
                self.report({'ERROR'}, 'Give the box a nonzero scale before editing')
                return {'CANCELLED'}
            self.transform = self.original @ Matrix.Diagonal(Vector((1/size.x, 1/size.y, 1/size.z, 1)))
            self.center, self.size = Vector(), size
        return self.begin(context, 'box')

    def frame(self, context):
        return collider_frame(context, self.obj)[1] if self.component else self.transform

    def shape(self):
        if self.component:
            _, data = obstacle_settings(self.obj, current_settings(bpy.context))
            center = navigation.vector(data, 'center', (0, 0, 0))
            size = navigation.vector(data, 'size', (1, 1, 1))
        else:
            center, size = self.center, self.size
        return SimpleNamespace(collider='BOX', collider_center=center, collider_size=size, minimum_size=.01)

    def apply(self, values):
        if self.component:
            for key, value in values.items():
                schema.set_param(self.component, self.fields[key], value)
        else:
            self.center, self.size = values['center'], values['size']
            self.obj.matrix_world = self.transform @ Matrix.Translation(self.center) @ Matrix.Diagonal(
                Vector((*self.size, 1)))

    def restore(self):
        if self.component:
            collider_edit.restore_parameters(self.snapshot)
        else:
            self.obj.matrix_world = self.original


def register():
    global _keymap
    bpy.utils.register_class(VECXY_OT_edit_navigation_box)
    bpy.utils.register_class(VECXY_OT_pick_navigation_volume)
    configs = bpy.context.window_manager.keyconfigs
    if not bpy.app.background and configs.addon:
        keymap = configs.addon.keymaps.new(name='3D View', space_type='VIEW_3D')
        _keymap = (keymap, [keymap.keymap_items.new(VECXY_OT_pick_navigation_volume.bl_idname, 'LEFTMOUSE', 'CLICK',
                                                  shift=shift, head=True) for shift in (False, True)])


def unregister():
    global _keymap
    if _keymap:
        keymap, items = _keymap
        for item in items:
            keymap.keymap_items.remove(item)
        _keymap = None
    bpy.utils.unregister_class(VECXY_OT_pick_navigation_volume)
    bpy.utils.unregister_class(VECXY_OT_edit_navigation_box)
