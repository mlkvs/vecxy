"""Author sprite layers on a dedicated, per-corner face coordinate map."""
import filecmp
from pathlib import Path
import shutil

import bpy
from bpy.props import BoolProperty, CollectionProperty, EnumProperty, FloatProperty, IntProperty, IntVectorProperty, PointerProperty, StringProperty
from bpy_extras.io_utils import ImportHelper
from mathutils import Vector

from . import face_atlas, face_library, face_surface, face_zone_edit
from .decals import asset_directory
from .face_surface import ATTRIBUTE

TYPE = 'Vecxy.Rendering.FaceSprites'
IMAGE_TYPES = {'.png', '.jpg', '.jpeg', '.bmp', '.tga', '.ppm'}
_preview_handle = None
_preview_shader = None
_preview_textures = {}


def active_mesh(context):
    obj = context.object
    return obj if obj and obj.type == 'MESH' else None


def selected_zone(obj):
    settings = obj.vecxy_face_sprites
    return settings.zones[settings.zone_index] if 0 <= settings.zone_index < len(settings.zones) else None


def selected_zones(obj):
    settings = obj.vecxy_face_sprites
    group = [zone for zone in settings.zones if zone.selected]
    active = selected_zone(obj)
    if active is not None and active not in group:
        group.append(active)
    return group


def current_sprite(zone):
    return zone.preview_sprite if zone.preview_override else zone.default_sprite


def redraw(_self, context):
    if context and context.screen:
        for area in context.screen.areas:
            if area.type == 'VIEW_3D':
                area.tag_redraw()


def rename_sprite(sprite, context):
    old = sprite.previous_identifier
    if old:
        for zone in sprite.id_data.vecxy_face_sprites.zones:
            if any(item.as_pointer() == sprite.as_pointer() for item in zone.sprites):
                if zone.default_sprite == old:
                    zone.default_sprite = sprite.identifier
                if zone.preview_sprite == old:
                    zone.preview_sprite = sprite.identifier
                break
    sprite.previous_identifier = sprite.identifier
    redraw(sprite, context)


class VECXY_FaceSprite(bpy.types.PropertyGroup):
    identifier: StringProperty(name='Sprite ID', update=rename_sprite)
    previous_identifier: StringProperty(options={'HIDDEN'})
    texture: StringProperty(name='Texture', update=redraw)
    region: IntVectorProperty(name='Atlas region', size=4, min=0, update=redraw)


class VECXY_FaceZone(bpy.types.PropertyGroup):
    name: StringProperty(name='Zone ID', default='zone', update=redraw)
    selected: BoolProperty(name='Include in Group Selection', update=redraw)
    x: FloatProperty(name='Left', min=0, max=1, precision=3, update=redraw)
    y: FloatProperty(name='Top', min=0, max=1, precision=3, update=redraw)
    width: FloatProperty(name='Width', min=.001, max=1, default=.3, precision=3, update=redraw)
    height: FloatProperty(name='Height', min=.001, max=1, default=.2, precision=3, update=redraw)
    sprites: CollectionProperty(type=VECXY_FaceSprite)
    sprite_index: IntProperty(update=redraw)
    default_sprite: StringProperty(update=redraw)
    preview_sprite: StringProperty(update=redraw)
    preview_override: BoolProperty(update=redraw)


class VECXY_FaceSettings(bpy.types.PropertyGroup):
    zones: CollectionProperty(type=VECXY_FaceZone)
    zone_index: IntProperty(default=-1, min=-1, update=redraw)
    atlas_path: StringProperty(name='PNG Atlas', update=redraw)
    show_preview: BoolProperty(name='Preview sprites', default=True, update=redraw)
    show_contour: BoolProperty(name='Face boundary', default=True, update=redraw)
    show_regions: BoolProperty(name='Highlight selected zones', default=True, update=redraw)
    resolution: EnumProperty(name='Face texture size', items=[(str(n), str(n), '') for n in (64, 128, 256, 512, 1024)],
                             default='256')
    nearest: BoolProperty(name='Pixel filtering', default=True, update=redraw)
    projection: EnumProperty(name='Projection', items=[
        ('FRONT', 'Front (XZ)', 'Project onto local X and Z'),
        ('SIDE', 'Side (YZ)', 'Project onto local Y and Z'),
        ('TOP', 'Top (XY)', 'Project onto local X and Y'),
    ], default='FRONT')


def face_definition(obj, root):
    settings = obj.vecxy_face_sprites
    attribute = obj.data.attributes.get(ATTRIBUTE)
    if not settings.zones:
        return None
    if not attribute or not any(0 <= value.vector[0] <= 1 for value in attribute.data):
        raise ValueError(f'{obj.name}: assign face polygons before export')
    zones = []
    for zone in settings.zones:
        identifier = zone.name.strip()
        if not identifier or any(entry['id'] == identifier for entry in zones):
            raise ValueError(f'{obj.name}: zone IDs must be nonempty and unique')
        if zone.x + zone.width > 1.000001 or zone.y + zone.height > 1.000001:
            raise ValueError(f'{obj.name}: zone {identifier} extends beyond the face')
        sprites, regions = {}, {}
        for sprite in zone.sprites:
            sprite_id = sprite.identifier.strip()
            texture = (root / sprite.texture).resolve()
            if not sprite_id or sprite_id in sprites:
                raise ValueError(f'{obj.name} / {identifier}: sprite IDs must be nonempty and unique')
            if not texture.is_relative_to(root) or not texture.is_file() or texture.suffix.lower() not in IMAGE_TYPES:
                raise ValueError(f'{obj.name} / {identifier}: missing sprite texture {sprite.texture}')
            sprites[sprite_id] = texture.relative_to(root).as_posix()
            region = tuple(sprite.region)
            if any(region):
                if texture.suffix.lower() != '.png' or not face_atlas.crop_valid(region, image_data(texture).size):
                    raise ValueError(f'{obj.name} / {identifier}: invalid PNG atlas region for {sprite_id}')
                regions[sprite_id] = dict(zip(('x', 'y', 'width', 'height'), region))
        if zone.default_sprite and zone.default_sprite not in sprites:
            raise ValueError(f'{obj.name} / {identifier}: select a valid default sprite')
        entry = {'id': identifier, 'x': zone.x, 'y': zone.y, 'width': zone.width, 'height': zone.height,
                 'defaultSprite': zone.default_sprite, 'sprites': sprites}
        if regions:
            entry['spriteRegions'] = regions
        zones.append(entry)
    return {'resolution': int(settings.resolution), 'nearest': settings.nearest, 'zones': zones}


def component(obj, context):
    if obj.type != 'MESH' or not obj.vecxy_face_sprites.zones:
        return None
    return {'key': 'faceSprites', 'type': TYPE, 'enabled': True,
            'data': face_definition(obj, asset_directory(context))}


class VECXY_OT_mark_face(bpy.types.Operator):
    bl_idname = 'vecxy.mark_sprite_face'
    bl_label = 'Assign Selected Polygons'
    bl_description = 'Replace the face region and project a dedicated coordinate map'
    bl_options = {'REGISTER', 'UNDO'}

    @classmethod
    def poll(cls, context):
        return active_mesh(context) is not None

    def execute(self, context):
        obj = active_mesh(context)
        editing = obj.mode == 'EDIT'
        if editing:
            bpy.ops.object.mode_set(mode='OBJECT')
        try:
            mesh = obj.data
            loops = [index for poly in mesh.polygons if poly.select for index in poly.loop_indices]
            if not loops:
                raise ValueError('Select the polygons of the face in Edit Mode')
            axis_x, axis_y = {'FRONT': (0, 2), 'SIDE': (1, 2), 'TOP': (0, 1)}[obj.vecxy_face_sprites.projection]
            coords = [mesh.vertices[mesh.loops[index].vertex_index].co for index in loops]
            left, right = min(v[axis_x] for v in coords), max(v[axis_x] for v in coords)
            bottom, top = min(v[axis_y] for v in coords), max(v[axis_y] for v in coords)
            if right - left < 1e-6 or top - bottom < 1e-6:
                raise ValueError('The selection has no area in this projection. Choose a different axis')
            attribute = mesh.attributes.get(ATTRIBUTE)
            if not attribute:
                # Blender's USD exporter renames the primary UV layer to st.
                if not mesh.uv_layers:
                    mesh.uv_layers.new(name='UVMap')
                active = mesh.uv_layers.active
                attribute = mesh.attributes.new(ATTRIBUTE, 'FLOAT2', 'CORNER')
                if active:
                    mesh.uv_layers.active = active
            for value in attribute.data:
                value.vector = (-1, -1)
            for index, coord in zip(loops, coords):
                attribute.data[index].vector = ((coord[axis_x] - left) / (right - left),
                                                (top - coord[axis_y]) / (top - bottom))
            mesh.update()
        except (RuntimeError, ValueError) as error:
            self.report({'ERROR'}, str(error))
            return {'CANCELLED'}
        finally:
            if editing:
                bpy.ops.object.mode_set(mode='EDIT')
        redraw(self, context)
        return {'FINISHED'}


class VECXY_OT_fit_face_zone(bpy.types.Operator):
    bl_idname = 'vecxy.fit_face_sprite_zone'
    bl_label = 'Fit Zone to Selected Polygons'
    bl_options = {'REGISTER', 'UNDO'}

    @classmethod
    def poll(cls, context):
        obj = active_mesh(context)
        return obj is not None and obj.mode == 'EDIT' and selected_zone(obj) is not None

    def execute(self, context):
        obj = active_mesh(context)
        zone = selected_zone(obj)
        if obj.mode == 'EDIT':
            obj.update_from_editmode()
        attribute = obj.data.attributes.get(ATTRIBUTE)
        coords = [attribute.data[i].vector[:] for poly in obj.data.polygons if poly.select
                  for i in poly.loop_indices if attribute and attribute.data[i].vector[0] >= 0]
        if not coords:
            self.report({'ERROR'}, 'Select polygons within the assigned face')
            return {'CANCELLED'}
        x, y = zip(*coords)
        if max(x) - min(x) < .001 or max(y) - min(y) < .001:
            self.report({'ERROR'}, 'The selected region is too small')
            return {'CANCELLED'}
        zone.x, zone.y, zone.width, zone.height = min(x), min(y), max(x)-min(x), max(y)-min(y)
        return {'FINISHED'}


def unique_id(name, existing):
    original = name
    suffix = 2
    while name in existing:
        name = f'{original}_{suffix}'
        suffix += 1
    return name


def add_sprite(zone, name, texture, region=(0, 0, 0, 0)):
    for index, sprite in enumerate(zone.sprites):
        if (sprite.identifier, sprite.texture, tuple(sprite.region)) == (name, texture, tuple(region)):
            zone.sprite_index = index
            break
    else:
        identifier = unique_id(name, {item.identifier for item in zone.sprites})
        sprite = zone.sprites.add()
        sprite.identifier, sprite.texture, sprite.region = identifier, texture, region
        zone.sprite_index = len(zone.sprites) - 1
    if not zone.default_sprite:
        zone.default_sprite = sprite.identifier
    zone.preview_sprite, zone.preview_override = sprite.identifier, True
    return sprite


def local_image_asset(source, root):
    if source.is_relative_to(root):
        return source.relative_to(root).as_posix()
    folder = root / 'FaceSprites'
    folder.mkdir(exist_ok=True)
    existing = list(folder.iterdir())
    for file in existing:
        if (file.is_file() and file.suffix.lower() == source.suffix.lower() and
                (file.stem == source.stem or file.stem.startswith(source.stem + '_')) and
                filecmp.cmp(source, file, shallow=False)):
            return file.relative_to(root).as_posix()
    name = unique_id(source.stem, {file.stem for file in existing})
    copied = folder / (name + source.suffix.lower())
    shutil.copyfile(source, copied)
    return copied.relative_to(root).as_posix()


class VECXY_OT_face_list_action(bpy.types.Operator):
    bl_idname = 'vecxy.face_sprite_action'
    bl_label = 'Edit Face Sprites'
    bl_options = {'REGISTER', 'UNDO'}

    action: EnumProperty(items=[(name, name.title(), '') for name in
                                ('ADD_ZONE', 'REMOVE_ZONE', 'CLEAR_SELECTION', 'REMOVE_SPRITE', 'PREVIEW',
                                 'DEFAULT', 'CLEAR', 'RESET_PREVIEW')])

    def execute(self, context):
        obj = active_mesh(context)
        if obj is None:
            return {'CANCELLED'}
        settings = obj.vecxy_face_sprites
        zone = selected_zone(obj)
        if self.action == 'ADD_ZONE':
            name = unique_id('zone', {item.name for item in settings.zones})
            zone = settings.zones.add()
            zone.name = name
            zone.x, zone.y = (1 - zone.width) / 2, (1 - zone.height) / 2
            for other in settings.zones:
                other.selected = False
            settings.zone_index = len(settings.zones) - 1
        elif self.action == 'CLEAR_SELECTION':
            for item in settings.zones:
                item.selected = False
            settings.zone_index = -1
        elif zone is None:
            return {'CANCELLED'}
        elif self.action == 'REMOVE_ZONE':
            settings.zones.remove(settings.zone_index)
            settings.zone_index = max(0, settings.zone_index - 1) if settings.zones else -1
        elif self.action == 'CLEAR':
            zone.preview_override = True
            zone.preview_sprite = ''
        elif self.action == 'RESET_PREVIEW':
            zone.preview_override = False
        elif 0 <= zone.sprite_index < len(zone.sprites):
            sprite = zone.sprites[zone.sprite_index]
            identifier = sprite.identifier
            if self.action == 'PREVIEW':
                zone.preview_sprite = identifier
                zone.preview_override = True
            elif self.action == 'DEFAULT':
                zone.default_sprite = '' if zone.default_sprite == identifier else identifier
                zone.preview_override = False
            elif self.action == 'REMOVE_SPRITE':
                if zone.default_sprite == identifier:
                    zone.default_sprite = ''
                if zone.preview_sprite == identifier:
                    zone.preview_override = False
                    zone.preview_sprite = ''
                zone.sprites.remove(zone.sprite_index)
                zone.sprite_index = max(0, zone.sprite_index - 1)
        redraw(self, context)
        return {'FINISHED'}


class VECXY_OT_import_face_sprite(bpy.types.Operator, ImportHelper):
    bl_idname = 'vecxy.import_face_sprite'
    bl_label = 'Add Sprite Image'
    bl_options = {'REGISTER', 'UNDO'}
    filter_glob: StringProperty(default='*.png;*.jpg;*.jpeg;*.bmp;*.tga;*.ppm', options={'HIDDEN'})

    def execute(self, context):
        obj = active_mesh(context)
        zone = selected_zone(obj) if obj else None
        if zone is None:
            return {'CANCELLED'}
        try:
            source = Path(self.filepath).resolve()
            if not source.is_file() or source.suffix.lower() not in IMAGE_TYPES:
                raise ValueError('Choose a supported image file')
            root = asset_directory(context)
            relative = local_image_asset(source, root)
            add_sprite(zone, source.stem, relative)
        except (OSError, ValueError) as error:
            self.report({'ERROR'}, str(error))
            return {'CANCELLED'}
        return {'FINISHED'}


class VECXY_UL_face_zones(bpy.types.UIList):
    def draw_item(self, _context, layout, _data, item, _icon, _active_data, _active_prop, _index):
        row = layout.row(align=True)
        row.prop(item, 'selected', text='')
        row.label(text=item.name, icon='UV')
        if current_sprite(item):
            row.label(text=current_sprite(item))


class VECXY_UL_face_sprites(bpy.types.UIList):
    def draw_item(self, _context, layout, zone, sprite, _icon, _active_data, _active_prop, _index):
        row = layout.row(align=True)
        row.label(text=sprite.identifier, icon='HIDE_OFF' if sprite.identifier == current_sprite(zone) else 'IMAGE_DATA')
        if sprite.identifier == zone.default_sprite:
            row.label(text='', icon='RADIOBUT_ON')


class VECXY_PT_face_sprites(bpy.types.Panel):
    bl_idname = 'VECXY_PT_face_sprites'
    bl_label = 'Face Sprites'
    bl_parent_id = 'VECXY_PT_character'
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = 'Vecxy'
    bl_options = {'DEFAULT_CLOSED'}

    @classmethod
    def poll(cls, context):
        from . import characters
        return characters.is_character(characters.active_collection(context))

    def draw(self, context):
        from . import characters

        layout = self.layout
        collection = characters.active_collection(context)
        layout.prop(collection.vecxy_character, 'face')
        obj = characters.face_mesh(collection)
        if context.object != obj or obj is None:
            layout.operator(characters.VECXY_OT_use_active_face_mesh.bl_idname, icon='FACESEL')
        if obj is None:
            layout.label(text='Select the head mesh in the viewport')
            return
        if context.object != obj:
            layout.operator(characters.VECXY_OT_select_face_mesh.bl_idname, icon='RESTRICT_SELECT_OFF')
            return

        settings = obj.vecxy_face_sprites
        surface = layout.box()
        surface.label(text='1. Face Surface')
        surface.prop(settings, 'projection')
        surface.operator(VECXY_OT_mark_face.bl_idname, icon='FACESEL')
        overlays = surface.row(align=True)
        overlays.prop(settings, 'show_contour', text='Boundary')
        overlays.prop(settings, 'show_regions', text='Zones')
        overlays.prop(settings, 'show_preview', text='Sprites')
        if not obj.data.attributes.get(ATTRIBUTE):
            surface.label(text='Select face polygons in Edit Mode')
            return

        zones = layout.box()
        zones.label(text='2. Zones')
        row = zones.row()
        row.template_list('VECXY_UL_face_zones', '', settings, 'zones', settings, 'zone_index', rows=3)
        buttons = row.column(align=True)
        buttons.operator(VECXY_OT_face_list_action.bl_idname, text='', icon='ADD').action = 'ADD_ZONE'
        buttons.operator(VECXY_OT_face_list_action.bl_idname, text='', icon='REMOVE').action = 'REMOVE_ZONE'
        buttons.operator(VECXY_OT_face_list_action.bl_idname, text='', icon='X').action = 'CLEAR_SELECTION'
        zones.operator(face_zone_edit.VECXY_OT_edit_face_zone.bl_idname, icon='GIZMO')
        if len(selected_zones(obj)) > 1:
            zones.label(text=f'{len(selected_zones(obj))} zones selected for viewport editing')
        zone = selected_zone(obj)
        if zone is None:
            if not settings.zones:
                zones.operator(VECXY_OT_face_list_action.bl_idname, text='Add Zone').action = 'ADD_ZONE'
            return
        zones.prop(zone, 'name')
        zones.operator(VECXY_OT_fit_face_zone.bl_idname, icon='FULLSCREEN_ENTER')
        header, fields = layout.panel('face_zone_coordinates', default_closed=True)
        header.label(text='Fine Adjustment')
        if fields:
            position = fields.row(align=True)
            position.prop(zone, 'x')
            position.prop(zone, 'y')
            size = fields.row(align=True)
            size.prop(zone, 'width')
            size.prop(zone, 'height')

        sprites = layout.box()
        sprites.label(text=f'3. Sprites for {zone.name}')
        sprites.label(text=f'Current · {current_sprite(zone) or "Empty"}', icon='HIDE_OFF')
        sprites.label(text=f'Default · {zone.default_sprite or "Empty"}', icon='RADIOBUT_ON')
        row = sprites.row()
        row.template_list('VECXY_UL_face_sprites', '', zone, 'sprites', zone, 'sprite_index', rows=3)
        buttons = row.column(align=True)
        buttons.operator(VECXY_OT_import_face_sprite.bl_idname, text='', icon='ADD')
        buttons.operator(VECXY_OT_face_list_action.bl_idname, text='', icon='REMOVE').action = 'REMOVE_SPRITE'
        if not zone.sprites:
            sprites.operator(VECXY_OT_import_face_sprite.bl_idname, icon='IMAGE_DATA')
        elif 0 <= zone.sprite_index < len(zone.sprites):
            sprite = zone.sprites[zone.sprite_index]
            sprites.prop(sprite, 'identifier')
            sprites.label(text=Path(sprite.texture).name or 'No image')
            if sprite.region[2] and sprite.region[3]:
                sprites.label(text=f'Atlas crop · {sprite.region[2]} × {sprite.region[3]} px')
                sprites.operator(face_atlas.VECXY_OT_pick_face_atlas_region.bl_idname,
                                 text='Edit Atlas Region').update_sprite = True
            row = sprites.row(align=True)
            row.operator(VECXY_OT_face_list_action.bl_idname, text='Show in Preview').action = 'PREVIEW'
            text = 'Clear Default' if sprite.identifier == zone.default_sprite else 'Set as Default'
            row.operator(VECXY_OT_face_list_action.bl_idname, text=text).action = 'DEFAULT'
        if zone.sprites:
            row = sprites.row(align=True)
            row.operator(VECXY_OT_face_list_action.bl_idname, text='Clear Preview').action = 'CLEAR'
            row.operator(VECXY_OT_face_list_action.bl_idname, text='Use Default').action = 'RESET_PREVIEW'
        library = layout.box()
        library.label(text='Project Face Library', icon='ASSET_MANAGER')
        row = library.row(align=True)
        row.operator(face_atlas.VECXY_OT_load_face_atlas.bl_idname, text='Import Atlas')
        row.operator(face_library.VECXY_OT_face_library_pick.bl_idname, text='Choose Atlas').kind = 'ATLAS'
        if settings.atlas_path:
            library.label(text=Path(settings.atlas_path).name)
            library.operator(face_atlas.VECXY_OT_pick_face_atlas_region.bl_idname, text='Add from Atlas', icon='ADD')
        row = library.row(align=True)
        row.operator(face_library.VECXY_OT_face_library_pick.bl_idname, text='Use Saved Sprite').kind = 'SPRITE'
        save = row.row()
        save.enabled = 0 <= zone.sprite_index < len(zone.sprites)
        save.operator(face_library.VECXY_OT_face_library_save.bl_idname, text='Save Sprite')
        row = layout.row(align=True)
        row.prop(settings, 'resolution')
        row.prop(settings, 'nearest')


def preview_shader():
    import gpu
    global _preview_shader
    if _preview_shader is not None:
        return _preview_shader
    interface = gpu.types.GPUStageInterfaceInfo('vecxy_face_preview')
    interface.smooth('VEC2', 'uv')
    info = gpu.types.GPUShaderCreateInfo()
    info.vertex_in(0, 'VEC3', 'position')
    info.vertex_in(1, 'VEC2', 'coordinate')
    info.vertex_out(interface)
    info.fragment_out(0, 'VEC4', 'color')
    info.push_constant('MAT4', 'viewProjection')
    info.push_constant('VEC4', 'zone')
    info.push_constant('VEC4', 'regionPixels')
    info.push_constant('INT', 'nearest')
    info.push_constant('VEC4', 'overlay')
    info.sampler(0, 'FLOAT_2D', 'sprite')
    info.vertex_source("""
        void main() {
            uv = coordinate;
            gl_Position = viewProjection * vec4(position, 1);
            gl_Position.z -= 0.00008 * gl_Position.w;
        }
    """)
    info.fragment_source("""
        void main() {
            vec2 p = (uv - zone.xy) / zone.zw;
            if (any(lessThan(p, vec2(0))) || any(greaterThan(p, vec2(1)))) discard;
            if (overlay.a > 0) {
                color = overlay;
                return;
            }
            vec2 size = vec2(textureSize(sprite, 0));
            bool cropped = all(greaterThan(regionPixels.zw, vec2(0)));
            vec2 extent = cropped ? regionPixels.zw : size;
            vec2 origin = cropped ? regionPixels.xy : vec2(0);
            vec2 pixel = clamp(p * extent, vec2(.5), extent - .5);
            if (nearest != 0) {
                pixel = clamp(floor(p * extent), vec2(0), extent - 1) + .5;
            }
            vec2 coord = (origin + pixel) / size;
            coord.y = 1 - coord.y;
            color = cropped || nearest != 0 ? textureLod(sprite, coord, 0) : texture(sprite, coord);
        }
    """)
    _preview_shader = gpu.shader.create_from_info(info)
    return _preview_shader


def image_data(path):
    image = _preview_textures.get(path)
    if not image:
        image = bpy.data.images.load(str(path), check_existing=True)
        _preview_textures[path] = image
    return image


def draw_preview():
    import gpu
    from gpu_extras.batch import batch_for_shader
    context = bpy.context
    obj = active_mesh(context)
    if not obj or not context.region_data or not obj.data.attributes.get(ATTRIBUTE):
        return
    settings = obj.vecxy_face_sprites
    if not (settings.show_preview or settings.show_contour or settings.show_regions):
        return
    positions, coordinates, boundary = face_surface.sample(context, obj, settings.show_contour)
    if not positions:
        return

    depth, blend = gpu.state.depth_test_get(), gpu.state.blend_get()
    gpu.state.depth_test_set('LESS_EQUAL')
    gpu.state.blend_set('ALPHA')
    try:
        if settings.zones and (settings.show_preview or settings.show_regions):
            try:
                root = asset_directory(context)
            except ValueError:
                root = None
            shader = preview_shader()
            batch = batch_for_shader(shader, 'TRIS', {'position': positions, 'coordinate': coordinates})
            shader.bind()
            shader.uniform_float('viewProjection', context.region_data.perspective_matrix)
            shader.uniform_int('nearest', int(settings.nearest))
            shader.uniform_float('overlay', (0, 0, 0, 0))
            if root and settings.show_preview:
                for zone in settings.zones:
                    identifier = current_sprite(zone)
                    sprite = next((item for item in zone.sprites if identifier and item.identifier == identifier), None)
                    if sprite:
                        path = (root / sprite.texture).resolve()
                        if path.is_relative_to(root) and path.is_file():
                            shader.uniform_float('zone', (zone.x, zone.y, zone.width, zone.height))
                            shader.uniform_float('regionPixels', tuple(sprite.region))
                            shader.uniform_sampler('sprite', gpu.texture.from_image(image_data(path)))
                            batch.draw(shader)
            if settings.show_regions:
                active = selected_zone(obj)
                for zone in selected_zones(obj):
                    shader.uniform_float('zone', (zone.x, zone.y, zone.width, zone.height))
                    shader.uniform_float('overlay', (1, .58, .14, .28) if zone == active else (.24, .64, 1, .22))
                    batch.draw(shader)
        if boundary:
            shift = context.region_data.view_rotation @ Vector((0, 0, obj.dimensions.length * .0002))
            shader = gpu.shader.from_builtin('POLYLINE_UNIFORM_COLOR')
            batch = batch_for_shader(shader, 'LINES', {'pos': [point + shift for point in boundary]})
            shader.bind()
            shader.uniform_float('viewportSize', gpu.state.viewport_get()[2:])
            shader.uniform_float('lineWidth', 1.7)
            shader.uniform_float('color', (.25, 1, .7, .95))
            batch.draw(shader)
    finally:
        gpu.state.blend_set(blend)
        gpu.state.depth_test_set(depth)


@bpy.app.handlers.persistent
def clear_preview_cache(_dummy):
    _preview_textures.clear()


CLASSES = (VECXY_FaceSprite, VECXY_FaceZone, VECXY_FaceSettings, VECXY_OT_mark_face, VECXY_OT_fit_face_zone,
           VECXY_OT_face_list_action, VECXY_OT_import_face_sprite,
           VECXY_UL_face_zones, VECXY_UL_face_sprites, VECXY_PT_face_sprites)


def register():
    global _preview_handle
    for cls in CLASSES:
        bpy.utils.register_class(cls)
    bpy.types.Object.vecxy_face_sprites = PointerProperty(type=VECXY_FaceSettings)
    face_zone_edit.register()
    face_atlas.register()
    face_library.register()
    bpy.app.handlers.load_pre.append(clear_preview_cache)
    if not bpy.app.background:
        _preview_handle = bpy.types.SpaceView3D.draw_handler_add(draw_preview, (), 'WINDOW', 'POST_VIEW')


def unregister():
    global _preview_handle, _preview_shader
    if _preview_handle is not None:
        bpy.types.SpaceView3D.draw_handler_remove(_preview_handle, 'WINDOW')
        _preview_handle = None
    _preview_shader = None
    face_library.unregister()
    face_atlas.unregister()
    face_zone_edit.unregister()
    bpy.app.handlers.load_pre.remove(clear_preview_cache)
    clear_preview_cache(None)
    del bpy.types.Object.vecxy_face_sprites
    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)
