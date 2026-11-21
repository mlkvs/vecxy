"""Viewport handles for primitive colliders."""
import math

import bpy
from bpy.props import StringProperty
from bpy_extras import view3d_utils
from mathutils import Vector
from mathutils.geometry import intersect_line_line

_ACTIVE = None
_AXES = tuple(Vector(axis) for axis in ((1, 0, 0), (0, 1, 0), (0, 0, 1)))


def is_editing(owner, key=None):
    return _ACTIVE is not None and _ACTIVE.owner == owner and (key is None or _ACTIVE.key == key)


def outline(shape):
    if shape.collider == 'BOX':
        half = Vector(shape.collider_size) * .5
        corners = [Vector((x * half.x, y * half.y, z * half.z))
                   for x in (-1, 1) for y in (-1, 1) for z in (-1, 1)]
        return [point for i in range(8) for bit in (1, 2, 4) if i < i ^ bit
                for point in (corners[i], corners[i ^ bit])]

    radius = shape.collider_radius
    length_axis = 'XYZ'.index(shape.collider_axis)
    axis = _AXES[length_axis]
    a, b = (_AXES[i] for i in range(3) if i != length_axis)
    half_height = shape.collider_height * .5 if shape.collider == 'CAPSULE' else 0

    def arc(u, v, center, span, segments=24):
        points = [center + radius * (math.cos(t) * u + math.sin(t) * v)
                  for t in (span * i / segments for i in range(segments + 1))]
        return [point for segment in zip(points, points[1:]) for point in segment]

    points = arc(a, b, axis * half_height, math.tau)
    if half_height:
        points += arc(a, b, -axis * half_height, math.tau)
    for radial in (a, b):
        for sign in (-1, 1):
            points += arc(radial, axis * sign, axis * half_height * sign, math.pi)
            if half_height:
                points += [radial * radius * sign - axis * half_height,
                           radial * radius * sign + axis * half_height]
    return points


def handles(shape):
    center = Vector(shape.collider_center)
    extents = Vector(shape.collider_size) * .5 if shape.collider == 'BOX' else Vector((shape.collider_radius,) * 3)
    if shape.collider == 'CAPSULE':
        extents['XYZ'.index(shape.collider_axis)] += shape.collider_height * .5
    return [(index, sign, center + direction * extents[index] * sign)
            for index, direction in enumerate(_AXES) for sign in (-1, 1)]


def resized(shape, axis, sign, distance):
    center = Vector(shape.collider_center)
    if shape.collider == 'BOX':
        size = Vector(shape.collider_size)
        new = max(.001, size[axis] + distance)
        center[axis] += sign * (new - size[axis]) * .5
        size[axis] = new
        return {'size': size, 'center': center}
    if shape.collider == 'CAPSULE' and axis == 'XYZ'.index(shape.collider_axis):
        height = max(0, shape.collider_height + distance)
        center[axis] += sign * (height - shape.collider_height) * .5
        return {'height': height, 'center': center}
    return {'radius': max(.001, shape.collider_radius + distance)}


class VECXY_OT_edit_collider(bpy.types.Operator):
    bl_idname = 'vecxy.edit_collider'
    bl_label = 'Edit collider in viewport'
    bl_options = {'REGISTER', 'UNDO'}
    key: StringProperty(options={'SKIP_SAVE'})

    @classmethod
    def poll(cls, context):
        return context.mode == 'OBJECT' and context.object and context.area and context.area.type == 'VIEW_3D'

    def invoke(self, context, event):
        global _ACTIVE
        from . import collider_frame, current_settings, settings_owner, schema

        if _ACTIVE:
            return {'CANCELLED'}
        obj = settings_owner(context.object)
        self.owner, _ = collider_frame(context, obj)
        settings = current_settings(context)
        self.component = next((c for c in settings.components if c.key == self.key and c.enabled), None)
        if self.component is None or self.component.type_name not in schema.COLLIDERS or self.component.type_name == schema.MESH_COLLIDER:
            return {'CANCELLED'}
        self.obj = obj
        self.area = context.area
        self.region = next(r for r in self.area.regions if r.type == 'WINDOW')
        self.view = context.space_data.region_3d
        self.drag = None
        self.snapshot = [(p, p.is_override, tuple(p.vector) if p.kind == 'VECTOR3' else p.number)
                         for p in self.component.parameters if p.key in {'center', 'size', 'radius', 'height'}]
        self.fields = {field['key']: field for field in schema.CATALOG[self.component.type_name]['fields']}
        self.draw_handle = bpy.types.SpaceView3D.draw_handler_add(self.draw_handles, (), 'WINDOW', 'POST_PIXEL')
        _ACTIVE = self
        self.area.header_text_set('Drag a collider handle   ·   Enter: finish   ·   Esc: cancel')
        self.area.tag_redraw()
        context.window_manager.modal_handler_add(self)
        return {'RUNNING_MODAL'}

    def shape(self):
        from . import instance_collection, schema
        if self.owner != self.obj:
            return next(s for s in schema.collider_views(self.owner.vecxy) if s.key == self.key)
        col = instance_collection(self.obj)
        if col and self.obj.vecxy.edit_defaults:
            return next(s for s in schema.collider_views(col.vecxy) if s.key == self.key)
        return next(s for s in schema.collider_views(self.obj.vecxy, col.vecxy if col else None) if s.key == self.key)

    def screen_handles(self):
        from . import collider_frame
        _, matrix = collider_frame(bpy.context, self.obj)
        for axis, sign, anchor in handles(self.shape()):
            position = view3d_utils.location_3d_to_region_2d(self.region, self.view, matrix @ anchor)
            if position is not None:
                yield axis, sign, anchor, position

    def draw_handles(self):
        if bpy.context.area != self.area or bpy.context.region != self.region:
            return
        import gpu
        from gpu_extras.batch import batch_for_shader

        points = [position for _, _, _, position in self.screen_handles()]
        if not points:
            return
        shader = gpu.shader.from_builtin('UNIFORM_COLOR')
        batch = batch_for_shader(shader, 'POINTS', {'pos': points})
        shader.bind()
        try:
            for size, color in ((13, (.08, .08, .08, 1)), (9, (1, .7, .2, 1))):
                gpu.state.point_size_set(size)
                shader.uniform_float('color', color)
                batch.draw(shader)
        finally:
            gpu.state.point_size_set(1)

    def axis_distance(self, mouse, anchor, direction):
        origin = view3d_utils.region_2d_to_origin_3d(self.region, self.view, mouse)
        ray = view3d_utils.region_2d_to_vector_3d(self.region, self.view, mouse)
        if direction.length_squared < 1e-12 or abs(ray.dot(direction.normalized())) > .9995:
            return None
        closest = intersect_line_line(anchor, anchor + direction, origin, origin + ray)
        return (closest[0] - anchor).dot(direction) / direction.length_squared if closest else None

    def modal(self, context, event):
        from . import collider_frame, schema

        if _ACTIVE != self:
            return {'CANCELLED'}
        if event.value == 'PRESS':
            if event.type in {'ESC', 'RIGHTMOUSE'}:
                self.finish(cancel=True)
                return {'CANCELLED'}
            if event.type in {'RET', 'NUMPAD_ENTER'}:
                self.finish()
                return {'FINISHED'}
        mouse = Vector((event.mouse_x - self.region.x, event.mouse_y - self.region.y))
        if event.type == 'LEFTMOUSE':
            if event.value == 'RELEASE':
                self.drag = None
            elif event.value == 'PRESS' and 0 <= mouse.x < self.region.width and 0 <= mouse.y < self.region.height:
                for axis, sign, anchor, position in sorted(self.screen_handles(), key=lambda h: (h[3] - mouse).length):
                    if (position - mouse).length > 14:
                        break
                    _, matrix = collider_frame(context, self.obj)
                    world_anchor = matrix @ anchor
                    direction = matrix.to_3x3() @ (_AXES[axis] * sign)
                    initial = self.axis_distance(mouse, world_anchor, direction)
                    if initial is not None:
                        self.drag = axis, sign, world_anchor, direction, initial, self.shape()
                        break
            return {'RUNNING_MODAL'}
        if event.type == 'MOUSEMOVE' and self.drag:
            axis, sign, anchor, direction, initial, shape = self.drag
            distance = self.axis_distance(mouse, anchor, direction)
            if distance is not None:
                for key, value in resized(shape, axis, sign, distance - initial).items():
                    schema.set_param(self.component, self.fields[key], value)
                self.area.tag_redraw()
            return {'RUNNING_MODAL'}
        if event.type in {'MIDDLEMOUSE', 'WHEELUPMOUSE', 'WHEELDOWNMOUSE', 'NDOF_MOTION'} or event.type.startswith('NUMPAD'):
            return {'PASS_THROUGH'}
        return {'RUNNING_MODAL'}

    def finish(self, cancel=False):
        global _ACTIVE
        if _ACTIVE != self:
            return
        if cancel:
            for param, overridden, value in self.snapshot:
                param.is_override = overridden
                if param.kind == 'VECTOR3':
                    param.vector = value
                else:
                    param.number = value
        bpy.types.SpaceView3D.draw_handler_remove(self.draw_handle, 'WINDOW')
        self.area.header_text_set(None)
        self.area.tag_redraw()
        _ACTIVE = None

    def cancel(self, context):
        self.finish(cancel=True)


@bpy.app.handlers.persistent
def cancel_edit(_dummy):
    if _ACTIVE:
        _ACTIVE.finish(cancel=True)
