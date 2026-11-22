"""Select pixel regions from a PNG atlas without splitting or rewriting the image."""
from pathlib import Path

import bpy
from bpy.props import BoolProperty, StringProperty
from bpy_extras.io_utils import ImportHelper
from mathutils import Vector

from .decals import asset_directory


_ACTIVE = None


def crop_valid(region, size):
    x, y, width, height = region
    return x >= 0 and y >= 0 and width > 0 and height > 0 and x + width <= size[0] and y + height <= size[1]


def load_atlas(context, value):
    from .face_sprites import image_data

    root = asset_directory(context)
    path = (root / value).resolve()
    if not path.is_relative_to(root) or not path.is_file() or path.suffix.lower() != '.png':
        raise ValueError('Choose an existing PNG atlas in the project Assets directory')
    image = image_data(path)
    if min(image.size) <= 0:
        raise ValueError(f'Could not read {path.name}')
    return image


class VECXY_OT_load_face_atlas(bpy.types.Operator, ImportHelper):
    bl_idname = 'vecxy.load_face_atlas'
    bl_label = 'Load PNG Atlas'
    bl_description = 'Choose a PNG image to reuse across face zones'
    bl_options = {'REGISTER', 'UNDO'}

    filter_glob: StringProperty(default='*.png', options={'HIDDEN'})

    def execute(self, context):
        from .face_sprites import active_mesh, local_image_asset
        from .face_library import remember_atlas

        obj = active_mesh(context)
        if obj is None:
            return {'CANCELLED'}
        try:
            root = asset_directory(context)
            source = Path(self.filepath).resolve()
            if source.suffix.lower() != '.png' or not source.is_file():
                raise ValueError('Choose a PNG atlas')
            relative = local_image_asset(source, root)
            load_atlas(context, relative)
            remember_atlas(context, relative)
            obj.vecxy_face_sprites.atlas_path = relative
        except (OSError, RuntimeError, ValueError) as error:
            self.report({'ERROR'}, str(error))
            return {'CANCELLED'}
        return {'FINISHED'}


class VECXY_OT_pick_face_atlas_region(bpy.types.Operator):
    bl_idname = 'vecxy.pick_face_atlas_region'
    bl_label = 'Choose Atlas Region'
    bl_description = 'Drag over the image to select the pixels for this sprite'
    bl_options = {'REGISTER', 'UNDO'}

    update_sprite: BoolProperty(default=False, options={'SKIP_SAVE'})

    @classmethod
    def poll(cls, context):
        from .face_sprites import active_mesh, selected_zone
        return bool(_ACTIVE is None and context.area and context.area.type == 'VIEW_3D' and
                    (obj := active_mesh(context)) and selected_zone(obj) is not None)

    def invoke(self, context, _event):
        global _ACTIVE
        import gpu
        from .face_sprites import selected_zone

        self.obj = context.object
        self.zone = selected_zone(self.obj)
        self.sprite = None
        self.crop = (0, 0, 0, 0)
        self.pending = []
        settings = self.obj.vecxy_face_sprites
        if self.update_sprite:
            if not 0 <= self.zone.sprite_index < len(self.zone.sprites):
                return {'CANCELLED'}
            self.sprite = self.zone.sprites[self.zone.sprite_index]
            self.path = self.sprite.texture
            self.crop = tuple(self.sprite.region)
        else:
            self.path = settings.atlas_path
        try:
            image = load_atlas(context, self.path)
            self.size = tuple(image.size)
            self.texture = gpu.texture.from_image(image)
        except (ValueError, RuntimeError) as error:
            self.report({'ERROR'}, str(error))
            return {'CANCELLED'}

        self.area = context.area
        self.region = next(region for region in self.area.regions if region.type == 'WINDOW')
        self.start = None
        self.pan_anchor = None
        self.pan = Vector((0, 0))
        self.zoom = 1
        self.draw_handle = bpy.types.SpaceView3D.draw_handler_add(self.draw_atlas, (), 'WINDOW', 'POST_PIXEL')
        _ACTIVE = self
        self.area.header_text_set('Drag: crop  ·  Shift-drag: add crop  ·  Wheel: zoom  ·  Enter: confirm  ·  Esc: cancel')
        self.area.tag_redraw()
        context.window_manager.modal_handler_add(self)
        return {'RUNNING_MODAL'}

    def frame(self):
        width, height = self.size
        scale = min(max(1, self.region.width-70) / width, max(1, self.region.height-100) / height) * self.zoom
        center = Vector((self.region.width*.5, self.region.height*.5)) + self.pan
        return center.x-scale*width*.5, center.y+scale*height*.5, scale

    def screen(self, pixel):
        left, top, scale = self.frame()
        return Vector((left+pixel[0]*scale, top-pixel[1]*scale))

    def image_pixel(self, screen):
        left, top, scale = self.frame()
        return Vector(((screen.x-left)/scale, (top-screen.y)/scale))

    def update_crop(self, end):
        if self.start is None:
            return
        x0, x1 = sorted((self.start.x, end.x))
        y0, y1 = sorted((self.start.y, end.y))
        x, right = (round(min(max(edge, 0), self.size[0])) for edge in (x0, x1))
        y, bottom = (round(min(max(edge, 0), self.size[1])) for edge in (y0, y1))
        self.crop = x, y, right-x, bottom-y

    def draw_atlas(self):
        if bpy.context.area != self.area or bpy.context.region != self.region:
            return
        import blf
        import gpu
        from gpu_extras.batch import batch_for_shader

        depth, blend = gpu.state.depth_test_get(), gpu.state.blend_get()
        gpu.state.depth_test_set('NONE')
        gpu.state.blend_set('ALPHA')
        fill = gpu.shader.from_builtin('UNIFORM_COLOR')

        def box(points, color):
            fill.bind()
            fill.uniform_float('color', color)
            batch_for_shader(fill, 'TRI_FAN', {'pos': points}).draw(fill)

        try:
            width, height = self.region.width, self.region.height
            box([(0, 0), (width, 0), (width, height), (0, height)], (.035, .043, .052, .97))
            w, h = self.size
            points = [self.screen(p) for p in ((0, h), (w, h), (w, 0), (0, 0))]
            box(points, (.17, .19, .22, 1))
            shader = gpu.shader.from_builtin('IMAGE')
            shader.bind()
            shader.uniform_sampler('image', self.texture)
            batch_for_shader(shader, 'TRI_FAN', {'pos': points,
                             'texCoord': [(0, 0), (1, 0), (1, 1), (0, 1)]}).draw(shader)
            for x, y, w, h in [*self.pending, self.crop]:
                if not w or not h:
                    continue
                points = [self.screen(p) for p in ((x, y), (x+w, y), (x+w, y+h), (x, y+h))]
                box(points, (1, .65, .18, .16))
                fill.bind()
                fill.uniform_float('color', (1, .72, .26, 1))
                batch_for_shader(fill, 'LINE_STRIP', {'pos': [*points, points[0]]}).draw(fill)
            blf.size(0, 14)
            blf.color(0, .95, .96, .98, 1)
            blf.position(0, 18, height-27, 0)
            blf.draw(0, Path(self.path).name)
            blf.position(0, 18, 20, 0)
            _, _, w, h = self.crop
            count = len(self.pending) + bool(w and h)
            blf.draw(0, f'{count} selected  ·  {w} × {h} px' if count else 'Drag to select a sprite')
        finally:
            gpu.state.blend_set(blend)
            gpu.state.depth_test_set(depth)

    def modal(self, context, event):
        if _ACTIVE != self:
            return {'CANCELLED'}
        if context.object != self.obj:
            self.finish()
            return {'CANCELLED'}
        if event.value == 'PRESS':
            if event.type in {'ESC', 'RIGHTMOUSE'}:
                self.finish()
                return {'CANCELLED'}
            if event.type in {'RET', 'NUMPAD_ENTER'}:
                crops = [*self.pending, *([self.crop] if crop_valid(self.crop, self.size) else [])]
                if not crops:
                    self.report({'WARNING'}, 'Draw a rectangle to select some pixels')
                    return {'RUNNING_MODAL'}
                self.save_crops(crops)
                self.finish()
                return {'FINISHED'}
            if event.type == 'BACK_SPACE' and self.sprite is None:
                self.crop = self.pending.pop() if self.pending else (0, 0, 0, 0)
                self.area.tag_redraw()
                return {'RUNNING_MODAL'}

        mouse = Vector((event.mouse_x-self.region.x, event.mouse_y-self.region.y))
        pixel = self.image_pixel(mouse)
        if event.type == 'LEFTMOUSE':
            if event.value == 'PRESS' and 0 <= pixel.x <= self.size[0] and 0 <= pixel.y <= self.size[1]:
                if self.sprite is None and event.shift and crop_valid(self.crop, self.size):
                    self.pending.append(self.crop)
                self.start = pixel
                self.update_crop(pixel)
            elif event.value == 'RELEASE' and self.start is not None:
                self.update_crop(pixel)
                self.start = None
        elif event.type == 'MIDDLEMOUSE':
            self.pan_anchor = mouse if event.value == 'PRESS' else None
        elif event.type == 'MOUSEMOVE':
            if self.pan_anchor is not None:
                self.pan += mouse-self.pan_anchor
                self.pan_anchor = mouse
            elif self.start is not None:
                self.update_crop(pixel)
        elif event.type in {'WHEELUPMOUSE', 'WHEELDOWNMOUSE'} and self.start is None:
            self.zoom = max(.4, min(12, self.zoom*(1.25 if event.type == 'WHEELUPMOUSE' else .8)))
            self.pan += mouse-self.screen(pixel)
        self.area.tag_redraw()
        return {'RUNNING_MODAL'}

    def save_crops(self, crops):
        from .face_sprites import add_sprite

        if self.sprite is not None:
            self.sprite.region = crops[0]
            self.zone.preview_sprite, self.zone.preview_override = self.sprite.identifier, True
        else:
            for crop in crops:
                add_sprite(self.zone, self.zone.name, self.path, crop)

    def finish(self):
        global _ACTIVE
        if _ACTIVE != self:
            return
        bpy.types.SpaceView3D.draw_handler_remove(self.draw_handle, 'WINDOW')
        _ACTIVE = None
        try:
            self.area.header_text_set(None)
            self.area.tag_redraw()
        except ReferenceError:
            pass

    def cancel(self, _context):
        self.finish()


@bpy.app.handlers.persistent
def close_picker(_dummy):
    if _ACTIVE:
        _ACTIVE.finish()


CLASSES = (VECXY_OT_load_face_atlas, VECXY_OT_pick_face_atlas_region)


def register():
    for cls in CLASSES:
        bpy.utils.register_class(cls)
    bpy.app.handlers.load_pre.append(close_picker)
    bpy.app.handlers.save_pre.append(close_picker)


def unregister():
    close_picker(None)
    bpy.app.handlers.load_pre.remove(close_picker)
    bpy.app.handlers.save_pre.remove(close_picker)
    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)
