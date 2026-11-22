"""Project-wide atlas and sprite presets shared by character .blend files."""
import json
import os
from pathlib import Path
import tempfile

import bpy
from bpy.props import EnumProperty

from .decals import asset_directory

_cache = {}
_items = {}


def library_path(context):
    return asset_directory(context).parent / 'Authoring' / 'FaceSpriteLibrary.json'


def entries(context):
    path = library_path(context)
    if not path.is_file():
        return {'format': 1, 'atlases': [], 'sprites': []}
    stat = path.stat()
    stamp = stat.st_mtime_ns, stat.st_size, stat.st_ino
    cached = _cache.get(path)
    if cached is None or cached[0] != stamp:
        data = json.loads(path.read_text(encoding='utf-8'))
        if not isinstance(data, dict) or data.get('format') != 1 or not all(
                isinstance(data.get(section), list) for section in ('atlases', 'sprites')):
            raise ValueError('Unsupported FaceSpriteLibrary.json format')
        if (any(not isinstance(item, str) for item in data['atlases']) or
                any(not isinstance(item, dict) or not isinstance(item.get('name'), str) or
                    not isinstance(item.get('image'), str) or not isinstance(item.get('region'), list) or
                    len(item['region']) != 4 or any(not isinstance(n, int) or n < 0 for n in item['region'])
                    for item in data['sprites'])):
            raise ValueError('Invalid entry in FaceSpriteLibrary.json')
        _cache[path] = stamp, data
    return _cache[path][1]


def write(context, body):
    path = library_path(context)
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(mode='w', encoding='utf-8', dir=path.parent, delete=False) as handle:
            temporary = Path(handle.name)
            json.dump(body, handle, ensure_ascii=False, indent=2)
            handle.write('\n')
        os.replace(temporary, path)
        _cache.pop(path, None)
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)


def remember_atlas(context, relative):
    data = entries(context)
    if relative not in data['atlases']:
        write(context, {**data, 'atlases': [*data['atlases'], relative]})


def remember_sprite(context, sprite):
    data = entries(context)
    preset = {'name': sprite.identifier, 'image': sprite.texture, 'region': list(sprite.region)}
    if preset not in data['sprites']:
        write(context, {**data, 'sprites': [*data['sprites'], preset]})


def preset_items(operator, context):
    try:
        path = library_path(context)
        data = entries(context)
    except (OSError, ValueError, AttributeError):
        return []
    root = path.parent.parent / 'Assets'
    if operator.kind == 'ATLAS':
        choices = [(str(i), Path(item).name, item) for i, item in enumerate(data['atlases'])
                   if (root / item).is_file()]
    else:
        choices = [(str(i), item['name'], item['image']) for i, item in enumerate(data['sprites'])
                   if (root / item['image']).is_file()]
    # Blender holds references to the enum strings while the search popup is open.
    key = path, operator.kind
    if _items.get(key) != choices:
        _items[key] = choices
    return _items[key]


class VECXY_OT_face_library_pick(bpy.types.Operator):
    bl_idname = 'vecxy.face_library_pick'
    bl_label = 'Choose from Face Library'
    bl_property = 'entry'
    bl_options = {'REGISTER', 'UNDO'}

    kind: EnumProperty(items=(('ATLAS', 'Atlas', ''), ('SPRITE', 'Sprite', '')))
    entry: EnumProperty(items=preset_items)

    def invoke(self, context, _event):
        if not preset_items(self, context):
            self.report({'INFO'}, 'The project library has no available entries of this type')
            return {'CANCELLED'}
        context.window_manager.invoke_search_popup(self)
        return {'RUNNING_MODAL'}

    def execute(self, context):
        from .face_sprites import active_mesh, selected_zone, add_sprite
        from . import face_atlas

        obj = active_mesh(context)
        if not obj:
            return {'CANCELLED'}
        try:
            data = entries(context)
            index = int(self.entry)
            if self.kind == 'ATLAS':
                relative = data['atlases'][index]
                face_atlas.load_atlas(context, relative)
                obj.vecxy_face_sprites.atlas_path = relative
            else:
                zone = selected_zone(obj)
                if zone is None:
                    return {'CANCELLED'}
                preset = data['sprites'][index]
                root = asset_directory(context)
                image = (root / preset['image']).resolve()
                if not image.is_relative_to(root) or not image.is_file():
                    raise ValueError('The sprite image is missing from Assets')
                add_sprite(zone, preset['name'], preset['image'], preset['region'])
        except (OSError, RuntimeError, ValueError, IndexError, KeyError) as error:
            self.report({'ERROR'}, str(error))
            return {'CANCELLED'}
        return {'FINISHED'}


class VECXY_OT_face_library_save(bpy.types.Operator):
    bl_idname = 'vecxy.face_library_save'
    bl_label = 'Save Sprite to Library'
    bl_description = 'Reuse this image or atlas region in other characters in this project'

    def execute(self, context):
        from .face_sprites import active_mesh, selected_zone

        obj = active_mesh(context)
        zone = selected_zone(obj) if obj else None
        if zone is None or not 0 <= zone.sprite_index < len(zone.sprites):
            return {'CANCELLED'}
        sprite = zone.sprites[zone.sprite_index]
        try:
            root = asset_directory(context)
            image = (root / sprite.texture).resolve()
            if not sprite.identifier.strip() or not image.is_relative_to(root) or not image.is_file():
                raise ValueError('Provide a sprite ID and a valid image first')
            remember_sprite(context, sprite)
        except (OSError, ValueError) as error:
            self.report({'ERROR'}, str(error))
            return {'CANCELLED'}
        self.report({'INFO'}, f'Saved {sprite.identifier} to the project Face Library')
        return {'FINISHED'}


CLASSES = (VECXY_OT_face_library_pick, VECXY_OT_face_library_save)


def register():
    for cls in CLASSES:
        bpy.utils.register_class(cls)


def unregister():
    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)
    _cache.clear()
    _items.clear()
