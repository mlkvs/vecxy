"""Interactive translation and resizing in the face's projected coordinates."""
from dataclasses import dataclass

import bpy
from bpy_extras import view3d_utils
from mathutils import Vector

from . import face_surface

_ACTIVE = None
MIN_SIZE = .001
SCREEN_HANDLES = ((-1, -1), (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (0, 0))


@dataclass
class FacePlane:
    center: Vector
    uv_center: Vector
    u_axis: Vector
    v_axis: Vector

    @classmethod
    def fit(cls, points, coordinates):
        if not points:
            return None
        count = len(points)
        center = sum(points, Vector((0, 0, 0))) / count
        uv_center = sum((Vector(uv) for uv in coordinates), Vector((0, 0))) / count
        uu = uv = vv = 0.0
        up = Vector((0, 0, 0))
        vp = Vector((0, 0, 0))
        for point, (u, v) in zip(points, coordinates):
            u -= uv_center.x
            v -= uv_center.y
            uu += u*u
            uv += u*v
            vv += v*v
            up += u * (point - center)
            vp += v * (point - center)
        determinant = uu*vv - uv*uv
        if determinant <= 1e-12:
            return None
        u_axis = (vv*up - uv*vp) / determinant
        v_axis = (uu*vp - uv*up) / determinant
        if u_axis.cross(v_axis).length_squared <= (u_axis.length_squared * v_axis.length_squared) * 1e-8:
            return None
        return cls(center, uv_center, u_axis, v_axis)

    def world(self, uv):
        return self.center + self.u_axis*(uv.x-self.uv_center.x) + self.v_axis*(uv.y-self.uv_center.y)

    def mouse_uv(self, region, view, mouse):
        normal = self.u_axis.cross(self.v_axis).normalized()
        ray = view3d_utils.region_2d_to_vector_3d(region, view, mouse)
        alignment = ray.dot(normal)
        if abs(alignment) < .03:
            return None
        origin = view3d_utils.region_2d_to_origin_3d(region, view, mouse, clamp=1000)
        offset = origin + ray * ((self.center-origin).dot(normal) / alignment) - self.center
        uu, uv, vv = self.u_axis.length_squared, self.u_axis.dot(self.v_axis), self.v_axis.length_squared
        up, vp = offset.dot(self.u_axis), offset.dot(self.v_axis)
        return self.uv_center + Vector((vv*up - uv*vp, uu*vp - uv*up)) / (uu*vv - uv*uv)


def zone_rect(zone):
    return zone.x, zone.y, zone.width, zone.height


def drag_rect(rect, handle, offset, minimum=(MIN_SIZE, MIN_SIZE)):
    left, top, width, height = rect
    width, height = min(width, 1-left), min(height, 1-top)
    width, height = max(MIN_SIZE, width), max(MIN_SIZE, height)
    left, top = min(left, 1-width), min(top, 1-height)
    right, bottom = left+width, top+height
    min_width, min_height = minimum
    horizontal, vertical = handle
    if handle == (0, 0):
        return (min(max(left + offset.x, 0), 1-width),
                min(max(top + offset.y, 0), 1-height), width, height)
    if horizontal < 0:
        left = min(max(left+offset.x, 0), right-min_width)
    elif horizontal > 0:
        right = max(min(right+offset.x, 1), left+min_width)
    if vertical < 0:
        top = min(max(top+offset.y, 0), bottom-min_height)
    elif vertical > 0:
        bottom = max(min(bottom+offset.y, 1), top+min_height)
    return left, top, right-left, bottom-top


def group_bounds(rects):
    x = min(rect[0] for rect in rects)
    y = min(rect[1] for rect in rects)
    right = max(left+width for left, _, width, _ in rects)
    bottom = max(top+height for _, top, _, height in rects)
    return x, y, right-x, bottom-y


def transform_group(rects, bounds, handle, offset):
    x, y, width, height = bounds
    min_size = (width * max(MIN_SIZE/rect[2] for rect in rects),
                height * max(MIN_SIZE/rect[3] for rect in rects))
    left, top, new_width, new_height = drag_rect(bounds, handle, offset, min_size)
    scale_x, scale_y = new_width/width, new_height/height
    return [(left + (a-x)*scale_x, top + (b-y)*scale_y, w*scale_x, h*scale_y) for a, b, w, h in rects]


class VECXY_OT_edit_face_zone(bpy.types.Operator):
    bl_idname = 'vecxy.edit_face_zone'
    bl_label = 'Edit Zones in Viewport'
    bl_description = 'Click a zone, Shift-click to extend selection, and drag to move or resize'
    bl_options = {'REGISTER', 'UNDO'}

    @classmethod
    def poll(cls, context):
        from .face_sprites import active_mesh
        obj = active_mesh(context)
        if context.mode != 'OBJECT':
            cls.poll_message_set('Switch to Object Mode to drag zone handles')
            return False
        return (_ACTIVE is None and context.area and context.area.type == 'VIEW_3D' and obj and
                obj.data.attributes.get(face_surface.ATTRIBUTE) and bool(obj.vecxy_face_sprites.zones))

    def invoke(self, context, _event):
        global _ACTIVE
        self.obj = context.object
        self.settings = self.obj.vecxy_face_sprites
        self.original = [(zone, zone_rect(zone), zone.selected) for zone in self.settings.zones]
        self.original_active = self.settings.zone_index
        points, coordinates, _ = face_surface.sample(context, self.obj)
        self.plane = FacePlane.fit(points, coordinates)
        if self.plane is None:
            self.report({'WARNING'}, 'Assign a face region with usable coordinates first')
            return {'CANCELLED'}
        self.area = context.area
        self.region = next(region for region in self.area.regions if region.type == 'WINDOW')
        self.view = context.space_data.region_3d
        self.frame = context.scene.frame_current_final
        self.drag = None
        self.hover = None
        self.draw_handle = bpy.types.SpaceView3D.draw_handler_add(self.draw_handles, (), 'WINDOW', 'POST_PIXEL')
        _ACTIVE = self
        self.area.header_text_set('Click: select or move  ·  Shift-click: multi-select  ·  Handles: resize  ·  H: highlights  ·  Enter: done')
        self.area.tag_redraw()
        context.window_manager.modal_handler_add(self)
        return {'RUNNING_MODAL'}

    def screen_handles(self):
        from .face_sprites import selected_zones
        rects = [zone_rect(zone) for zone in selected_zones(self.obj)]
        if not rects:
            return {}
        x, y, width, height = group_bounds(rects)
        points = {}
        for h, v in SCREEN_HANDLES:
            uv = Vector((x + (h+1)*width/2, y + (v+1)*height/2))
            point = view3d_utils.location_3d_to_region_2d(self.region, self.view, self.plane.world(uv))
            if point is not None:
                points[h, v] = point
        return points

    def zone_at(self, point):
        for index in reversed(range(len(self.settings.zones))):
            x, y, width, height = zone_rect(self.settings.zones[index])
            if x <= point.x <= x+width and y <= point.y <= y+height:
                return index
        return None

    def select(self, index, extend):
        from .face_sprites import selected_zones
        if not extend:
            for zone in self.settings.zones:
                zone.selected = False
            self.settings.zone_index = index if index is not None else -1
            return
        if index is None:
            return
        for zone in selected_zones(self.obj):
            zone.selected = True
        zone = self.settings.zones[index]
        zone.selected = not zone.selected
        self.settings.zone_index = index if zone.selected else next(
            (i for i, item in enumerate(self.settings.zones) if item.selected), -1)

    def follow_pose(self, context):
        frame = context.scene.frame_current_final
        if not self.drag and frame != self.frame:
            points, coordinates, _ = face_surface.sample(context, self.obj)
            plane = FacePlane.fit(points, coordinates)
            if plane is not None:
                self.plane = plane
            self.frame = frame

    def draw_handles(self):
        if bpy.context.area != self.area or bpy.context.region != self.region:
            return
        import gpu
        from gpu_extras.batch import batch_for_shader

        try:
            self.follow_pose(bpy.context)
            points = self.screen_handles()
        except ReferenceError:
            return
        if not points:
            return

        shader = gpu.shader.from_builtin('UNIFORM_COLOR')
        shader.bind()
        corners = ((-1, -1), (1, -1), (1, 1), (-1, 1), (-1, -1))
        if all(key in points for key in corners):
            shader.uniform_float('color', (1, .62, .18, 1))
            batch_for_shader(shader, 'LINE_STRIP', {'pos': [points[key] for key in corners]}).draw(shader)
        try:
            for handle, center in points.items():
                active = handle == self.hover or (self.drag and handle == self.drag[0])
                color = (1, .94, .65, 1) if active else (1, .62, .18, 1)
                shader.uniform_float('color', (.07, .04, .02, 1))
                gpu.state.point_size_set(15 if handle == (0, 0) else 11)
                point = batch_for_shader(shader, 'POINTS', {'pos': [center]})
                point.draw(shader)
                shader.uniform_float('color', color)
                gpu.state.point_size_set(11 if handle == (0, 0) else 7)
                point.draw(shader)
        finally:
            gpu.state.point_size_set(1)

    def modal(self, context, event):
        if _ACTIVE != self:
            return {'CANCELLED'}
        if context.object != self.obj or context.mode != 'OBJECT':
            self.finish(cancel=True)
            return {'CANCELLED'}
        if event.value == 'PRESS':
            if event.type in {'ESC', 'RIGHTMOUSE', 'RET', 'NUMPAD_ENTER'}:
                cancel = event.type in {'ESC', 'RIGHTMOUSE'}
                self.finish(cancel=cancel)
                return {'CANCELLED'} if cancel else {'FINISHED'}
            if event.type == 'H':
                self.settings.show_regions = not self.settings.show_regions
                return {'RUNNING_MODAL'}
        if event.type == 'LEFTMOUSE' and event.alt and not self.drag:
            return {'PASS_THROUGH'}

        self.follow_pose(context)
        mouse = Vector((event.mouse_x-self.region.x, event.mouse_y-self.region.y))
        points = self.screen_handles()
        nearest = min(points, key=lambda key: (points[key]-mouse).length_squared, default=None)
        self.hover = nearest if nearest is not None and (points[nearest]-mouse).length <= 12 else None
        if event.type == 'LEFTMOUSE':
            if event.value == 'RELEASE':
                self.drag = None
            elif event.value == 'PRESS' and 0 <= mouse.x < self.region.width and 0 <= mouse.y < self.region.height:
                from .face_sprites import selected_zones
                anchor = self.plane.mouse_uv(self.region, self.view, mouse)
                if anchor is not None:
                    index = self.zone_at(anchor)
                    if event.shift:
                        self.select(index, extend=True)
                    else:
                        handle = self.hover
                        if handle is None:
                            group = selected_zones(self.obj)
                            if index is None or self.settings.zones[index] not in group:
                                self.select(index, extend=False)
                            elif index != self.settings.zone_index:
                                for zone in group:
                                    zone.selected = True
                                self.settings.zone_index = index
                            if index is not None:
                                handle = (0, 0)
                        if handle is not None:
                            zones = selected_zones(self.obj)
                            rects = [zone_rect(zone) for zone in zones]
                            self.drag = handle, zones, rects, group_bounds(rects), anchor
            self.area.tag_redraw()
            return {'RUNNING_MODAL'}

        if event.type == 'MOUSEMOVE':
            if self.drag:
                handle, zones, rects, bounds, anchor = self.drag
                target = self.plane.mouse_uv(self.region, self.view, mouse)
                if target is not None:
                    for zone, rect in zip(zones, transform_group(rects, bounds, handle, target-anchor)):
                        zone.x, zone.y, zone.width, zone.height = rect
            self.area.tag_redraw()
            return {'RUNNING_MODAL'} if self.drag else {'PASS_THROUGH'}

        # Keep viewport navigation available while the editor is active.
        if event.type in {'MIDDLEMOUSE', 'WHEELUPMOUSE', 'WHEELDOWNMOUSE', 'NDOF_MOTION'} or event.type.startswith('NUMPAD'):
            return {'PASS_THROUGH'}
        return {'RUNNING_MODAL'}

    def finish(self, cancel=False):
        global _ACTIVE
        if _ACTIVE != self:
            return
        if cancel:
            try:
                for zone, rect, selected in self.original:
                    zone.x, zone.y, zone.width, zone.height = rect
                    zone.selected = selected
                self.settings.zone_index = self.original_active
            except ReferenceError:
                pass
        bpy.types.SpaceView3D.draw_handler_remove(self.draw_handle, 'WINDOW')
        _ACTIVE = None
        try:
            self.area.header_text_set(None)
            self.area.tag_redraw()
        except ReferenceError:
            pass

    def cancel(self, _context):
        self.finish(cancel=True)


@bpy.app.handlers.persistent
def cancel_edit(_dummy):
    if _ACTIVE:
        _ACTIVE.finish(cancel=True)


def register():
    bpy.utils.register_class(VECXY_OT_edit_face_zone)
    bpy.app.handlers.load_pre.append(cancel_edit)
    bpy.app.handlers.save_pre.append(cancel_edit)


def unregister():
    cancel_edit(None)
    bpy.app.handlers.load_pre.remove(cancel_edit)
    bpy.app.handlers.save_pre.remove(cancel_edit)
    bpy.utils.unregister_class(VECXY_OT_edit_face_zone)
