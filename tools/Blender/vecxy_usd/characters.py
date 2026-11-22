"""Character authoring and standalone GLB export."""
from collections import defaultdict
from contextlib import contextmanager
import json
from pathlib import Path
import struct
import tempfile

import bpy
from bpy.props import BoolProperty, CollectionProperty, EnumProperty, IntProperty, PointerProperty, StringProperty
from bpy_extras.io_utils import ExportHelper

from . import face_sprites
from .decals import asset_directory

HEADER = struct.Struct('<4sII')
CHUNK = struct.Struct('<I4s')
NODE_MARKER = 'vecxyCharacterNode'


def active_collection(context):
    return context.scene.vecxy_character_collection


def is_character(collection):
    return collection is not None and collection.vecxy_character.enabled


def character_objects():
    return {obj for collection in bpy.data.collections if is_character(collection)
            for obj in collection.all_objects}


def scene_object(obj, excluded):
    return obj not in excluded and not (obj.instance_type == 'COLLECTION' and is_character(obj.instance_collection))


def character_members(collection):
    meshes = [obj for obj in collection.all_objects if obj.type == 'MESH'
              and not obj.get('_vecxy_decal_preview')]
    rigs = {obj for obj in collection.all_objects if obj.type == 'ARMATURE'}
    rigs.update(rig for obj in meshes if (rig := obj.find_armature()))
    return sorted(meshes, key=lambda obj: obj.name), sorted(rigs, key=lambda obj: obj.name)


def is_attachment(obj):
    return obj.type == 'EMPTY' and obj.instance_type != 'COLLECTION' and not obj.get('_vecxy_decal_preview')


def export_members(collection, meshes, rigs):
    return [*meshes, *rigs, *(obj for obj in collection.all_objects if is_attachment(obj))]


def face_mesh(collection):
    settings = collection.vecxy_character
    configured = settings.face
    if configured:
        return configured if configured in collection.all_objects[:] else None
    marked = [obj for obj in collection.all_objects if obj.type == 'MESH' and obj.vecxy_face_sprites.zones]
    return marked[0] if len(marked) == 1 else None


def face_in_collection(settings, obj):
    return obj.type == 'MESH' and obj in settings.id_data.all_objects[:]


class VECXY_CharacterClip(bpy.types.PropertyGroup):
    action: PointerProperty(name='Blender Action', type=bpy.types.Action)
    export_name: StringProperty(name='Name in Game', description='Leave blank to use the Blender Action name')
    enabled: BoolProperty(name='Export Clip', default=True)


class VECXY_CharacterSettings(bpy.types.PropertyGroup):
    enabled: BoolProperty(name='Export as Character', description='Keep this collection out of the USD level')
    face: PointerProperty(name='Face Mesh', type=bpy.types.Object, poll=face_in_collection)
    clip_mode: EnumProperty(name='Include', items=[
        ('ALL', 'All Actions', 'Export the available actions with Blender names'),
        ('LIST', 'Clip List', 'Export checked actions with the names below'),
        ('NONE', 'No Animation', 'Export the character at rest')], default='ALL')
    clips: CollectionProperty(type=VECXY_CharacterClip)
    clip_index: IntProperty()
    default_animation: StringProperty(name='Start Animation', description='Name in Game to play when the character spawns')


class VECXY_OT_character_from_selection(bpy.types.Operator):
    bl_idname = 'vecxy.character_from_selection'
    bl_label = 'Create from Selection'
    bl_description = 'Group the selection and meshes bound to the same rig as a separate character asset'
    bl_options = {'REGISTER', 'UNDO'}

    @classmethod
    def poll(cls, context):
        return context.mode == 'OBJECT' and any(obj.type in {'ARMATURE', 'MESH'} for obj in context.selected_objects)

    def execute(self, context):
        selected = {obj for obj in context.selected_objects if obj.type in {'ARMATURE', 'MESH'} or is_attachment(obj)}
        rigs = {obj for obj in selected if obj.type == 'ARMATURE'}
        rigs.update(rig for obj in selected if obj.type == 'MESH' and (rig := obj.find_armature()))
        if len(rigs) > 1:
            self.report({'ERROR'}, 'Select one character rig at a time')
            return {'CANCELLED'}
        if selected.intersection(character_objects()):
            self.report({'ERROR'}, 'Selection already belongs to a character. Choose its collection')
            return {'CANCELLED'}
        rig_children = {obj for rig in rigs for obj in rig.children_recursive}
        for obj in context.scene.objects:
            if rigs and (obj.type == 'MESH' or is_attachment(obj)):
                if obj.find_armature() in rigs or obj in rig_children:
                    selected.add(obj)
        if not any(obj.type == 'MESH' for obj in selected):
            self.report({'ERROR'}, 'Select a rig with at least one character mesh')
            return {'CANCELLED'}
        if selected.intersection(character_objects()):
            self.report({'ERROR'}, 'A mesh on this rig already belongs to another character collection')
            return {'CANCELLED'}
        owner = next(iter(rigs), context.object)
        collection = bpy.data.collections.new(f'{owner.name if owner else "New"} Character')
        context.scene.collection.children.link(collection)
        for obj in sorted(selected | rigs, key=lambda obj: obj.name):
            collection.objects.link(obj)
        collection.vecxy_character.enabled = True
        context.scene.vecxy_character_collection = collection
        if face := face_mesh(collection):
            collection.vecxy_character.face = face
        elif context.object and context.object.type == 'MESH' and context.object in selected:
            collection.vecxy_character.face = context.object
        self.report({'INFO'}, f'Created {collection.name}')
        return {'FINISHED'}


def available_actions(rigs, meshes):
    from bpy_extras.anim_utils import action_get_channelbag_for_slot

    actions = set()
    for obj in [*rigs, *meshes]:
        animation = obj.animation_data
        if animation:
            if animation.action:
                actions.add(animation.action)
            actions.update(strip.action for track in animation.nla_tracks for strip in track.strips
                           if strip.action and not strip.mute)
    if rigs:
        rig = rigs[0]
        for action in bpy.data.actions:
            for slot in action.slots:
                bag = action_get_channelbag_for_slot(action, slot)
                if bag and any(curve_bone_on_rig(curve.data_path, rig) for curve in bag.fcurves):
                    actions.add(action)
                    break
    return sorted(actions, key=lambda action: action.name.casefold())


def curve_bone_on_rig(path, rig):
    if not path.startswith('pose.bones['):
        return False
    try:
        rig.path_resolve(path)
    except ValueError:
        return False
    return True


class VECXY_OT_character_clip_list(bpy.types.Operator):
    bl_idname = 'vecxy.character_clip_list'
    bl_label = 'Edit Character Clips'
    bl_options = {'REGISTER', 'UNDO'}
    action: EnumProperty(items=[
        ('COLLECT', 'Find Actions', 'Add candidate actions without changing saved names'),
        ('ADD', 'Add Action', 'Pick an action manually'),
        ('REMOVE', 'Remove Action', 'Remove this row'),
    ])

    def execute(self, context):
        collection = active_collection(context)
        if not is_character(collection):
            return {'CANCELLED'}
        settings = collection.vecxy_character
        if self.action == 'COLLECT':
            meshes, rigs = character_members(collection)
            existing = {clip.action for clip in settings.clips}
            for action in available_actions(rigs, meshes):
                if action not in existing:
                    settings.clips.add().action = action
            if not settings.clips:
                self.report({'WARNING'}, 'No compatible actions found for this character')
                return {'CANCELLED'}
        elif self.action == 'ADD':
            settings.clips.add()
            settings.clip_index = len(settings.clips) - 1
        elif 0 <= settings.clip_index < len(settings.clips):
            settings.clips.remove(settings.clip_index)
            settings.clip_index = max(0, settings.clip_index - 1)
        settings.clip_mode = 'LIST'
        return {'FINISHED'}


class VECXY_OT_select_face_mesh(bpy.types.Operator):
    bl_idname = 'vecxy.select_character_face'
    bl_label = 'Select Face Mesh'
    bl_options = {'UNDO'}

    def execute(self, context):
        collection = active_collection(context)
        obj = face_mesh(collection) if collection else None
        if obj is None or obj.name not in context.view_layer.objects:
            self.report({'ERROR'}, 'Choose a face mesh visible in the current view layer')
            return {'CANCELLED'}
        if context.mode != 'OBJECT':
            bpy.ops.object.mode_set(mode='OBJECT')
        bpy.ops.object.select_all(action='DESELECT')
        obj.hide_set(False)
        obj.select_set(True)
        context.view_layer.objects.active = obj
        return {'FINISHED'}


class VECXY_OT_use_active_face_mesh(bpy.types.Operator):
    bl_idname = 'vecxy.use_active_character_face'
    bl_label = 'Use Active Mesh as Face'
    bl_description = 'Set the active mesh as the face and add it to the character collection if needed'
    bl_options = {'REGISTER', 'UNDO'}

    @classmethod
    def poll(cls, context):
        return is_character(active_collection(context)) and face_sprites.active_mesh(context) is not None

    def execute(self, context):
        collection = active_collection(context)
        obj = face_sprites.active_mesh(context)
        for other in bpy.data.collections:
            if other != collection and is_character(other) and obj.name in other.all_objects:
                self.report({'ERROR'}, f'{obj.name} already belongs to {other.name}')
                return {'CANCELLED'}
        if obj.name not in collection.all_objects:
            collection.objects.link(obj)
        collection.vecxy_character.face = obj
        self.report({'INFO'}, f'{obj.name} is the face mesh for {collection.name}')
        return {'FINISHED'}


def clip_mapping(settings):
    if settings.clip_mode != 'LIST':
        return None
    mapping = {}
    aliases = set()
    for clip in settings.clips:
        if not clip.enabled:
            continue
        if not clip.action:
            raise ValueError('Choose a Blender Action for each checked clip')
        action = clip.action.name
        name = clip.export_name.strip() or action
        if action in mapping:
            raise ValueError(f'Action {action!r} is listed twice')
        if name in aliases:
            raise ValueError(f'Export name {name!r} is used more than once')
        mapping[action] = name
        aliases.add(name)
    if not mapping:
        raise ValueError('Check a clip to export, or choose No Animation')
    return mapping


def character_check(context, collection):
    if not is_character(collection):
        raise ValueError('Choose or create a Character collection')
    if collection.vecxy.is_prototype or collection.vecxy.is_edit_proxy:
        raise ValueError('Choose a dedicated character collection outside the Shared Prototype library')
    meshes, rigs = character_members(collection)
    if not meshes:
        raise ValueError('Add at least one mesh to the character collection')
    if len(rigs) > 1:
        raise ValueError('Export one armature per character')
    for obj in export_members(collection, meshes, rigs):
        if obj.name not in context.view_layer.objects or obj.hide_viewport:
            raise ValueError(f'{obj.name}: enable its collection and viewport visibility before exporting')
    face = face_mesh(collection)
    if collection.vecxy_character.face and face is None:
        raise ValueError('Choose a face mesh in this character collection')
    other_faces = [obj.name for obj in meshes if obj != face and obj.vecxy_face_sprites.zones]
    if other_faces:
        raise ValueError('Assign one face mesh per character. Other configured meshes: ' + ', '.join(other_faces))
    root = asset_directory(context)
    options = face_sprites.face_definition(face, root) if face else None
    mapping = clip_mapping(collection.vecxy_character)
    start = collection.vecxy_character.default_animation.strip()
    if mapping is not None and start and start not in mapping.values():
        raise ValueError(f'Start animation {start!r} must be checked in the clip list')
    warnings = []
    for obj in [*meshes, *rigs]:
        if any(abs(scale - 1) > .001 for scale in obj.scale):
            warnings.append(f'{obj.name}: object scale differs from 1')
    return root, meshes, rigs, options, mapping, warnings


class VECXY_OT_check_character(bpy.types.Operator):
    bl_idname = 'vecxy.check_character'
    bl_label = 'Check Before Export'

    def execute(self, context):
        try:
            collection = active_collection(context)
            _, meshes, rigs, options, mapping, warnings = character_check(context, collection)
            nodes = authored_nodes(context, export_members(collection, meshes, rigs))
        except (RuntimeError, OSError, ValueError) as error:
            self.report({'ERROR'}, str(error))
            return {'CANCELLED'}
        for warning in warnings:
            self.report({'WARNING'}, warning)
        self.report({'INFO'}, f'{len(meshes)} meshes · {len(rigs)} rigs · {len(nodes)} configured nodes · '
                    f'{len(options["zones"]) if options else 0} face zones'
                    + (f' · {len(mapping)} clips' if mapping is not None else ''))
        return {'FINISHED'}


def to_engine_collider(component):
    if not component['type'].endswith('Collider') or not component['type'].startswith('Vecxy.Physics.'):
        return component
    data = component['data']

    def y_up(vector):
        return {'x': vector['x'], 'y': vector['z'], 'z': -vector['y']}

    if 'center' in data:
        data['center'] = y_up(data['center'])
    if 'vertices' in data:
        data['vertices'] = [y_up(vertex) for vertex in data['vertices']]
    if component['type'] == 'Vecxy.Physics.BoxCollider' and 'size' in data:
        size = data['size']
        data['size'] = {'x': size['x'], 'y': size['z'], 'z': size['y']}
    if component['type'] == 'Vecxy.Physics.CapsuleCollider':
        axis = data.get('axis', 'Z')
        data['axis'] = {'X': 'X', 'Y': 'Z', 'Z': 'Y'}[axis]
    return component


def authored_nodes(context, members):
    from . import exporter

    result = {}
    depsgraph = context.evaluated_depsgraph_get()
    for obj in members:
        settings = obj.vecxy
        if not settings.components and not settings.is_static:
            continue
        components = exporter.components(settings, {'depsgraph': depsgraph, 'obj': obj})
        result[obj] = {'isStatic': settings.is_static, 'components': [
            {'type': item['type'], 'data': json.dumps(to_engine_collider(item)['data'], separators=(',', ':'))}
            for item in components if item['enabled']]}
    return result


@contextmanager
def action_filter(mapping):
    # Blender reads this filter from the first scene during action gathering.
    scene = bpy.data.scenes[0]
    created = not hasattr(scene, 'gltf_action_filter')
    if created and mapping is None:
        yield
        return
    if created:
        from io_scene_gltf2 import GLTF2_filter_action
        bpy.types.Scene.gltf_action_filter = CollectionProperty(type=GLTF2_filter_action)
    entries = scene.gltf_action_filter
    previous = [(item.action, item.keep) for item in entries]
    try:
        entries.clear()
        if mapping is not None:
            for action in bpy.data.actions:
                if action.name not in mapping:
                    item = entries.add()
                    item.action, item.keep = action, False
        yield
    finally:
        if created:
            entries.clear()
            del bpy.types.Scene.gltf_action_filter
        else:
            entries.clear()
            for action, keep in previous:
                item = entries.add()
                item.action, item.keep = action, keep


def finish_glb(path, mapping, rig_required, bindings, start_animation):
    contents = path.read_bytes()
    magic, version, total = HEADER.unpack_from(contents)
    length, kind = CHUNK.unpack_from(contents, HEADER.size)
    start = HEADER.size + CHUNK.size
    if magic != b'glTF' or version != 2 or total != len(contents) or kind != b'JSON' or start + length > total:
        raise ValueError('Blender produced an invalid GLB file')
    document = json.loads(contents[start:start + length])
    tail = contents[start + length:]
    skins = document.get('skins', [])
    if rig_required and not skins:
        raise ValueError('The exported model has no skin. Check the Armature modifier and vertex groups')
    if any(len(skin['joints']) > 64 for skin in skins):
        raise ValueError('A Vecxy skin supports 64 exported joints. Limit the deform bones and try again')
    animations = document.get('animations', [])
    if mapping is not None:
        by_name = defaultdict(list)
        for clip in animations:
            by_name[clip.get('name')].append(clip)
        chosen = []
        for source, target in mapping.items():
            candidates = by_name[source]
            if len(candidates) != 1:
                raise ValueError(f'Expected one animation for {source!r}, found {len(candidates)}. '
                                 'Check that the Action has keys for this rig')
            chosen.append({**candidates[0], 'name': target})
        animations = document['animations'] = chosen
    names = [clip.get('name') for clip in animations]
    if len(names) != len(set(names)) or any(not name for name in names):
        raise ValueError('Exported animation names must be nonempty and unique')
    if start_animation and start_animation not in names:
        raise ValueError(f'Start animation {start_animation!r} was not exported')
    nodes = []
    remaining = dict(bindings)
    for index, node in enumerate(document.get('nodes', [])):
        extras = node.get('extras', {})
        identifier = extras.pop(NODE_MARKER, None)
        if identifier is None:
            continue
        if not extras:
            node.pop('extras')
        if identifier not in remaining:
            raise ValueError('A character component owner was exported more than once')
        nodes.append({'node': index, **remaining.pop(identifier)})
    if remaining:
        raise ValueError('Blender did not export nodes for: ' + ', '.join(sorted(remaining)))
    if mapping is not None or bindings:
        payload = json.dumps(document, separators=(',', ':'), ensure_ascii=False).encode('utf-8')
        payload += b' ' * (-len(payload) % 4)
        path.write_bytes(HEADER.pack(magic, version, start + len(payload) + len(tail))
                         + CHUNK.pack(len(payload), kind) + payload + tail)
    return len(animations), nodes


class VECXY_OT_export_character(bpy.types.Operator, ExportHelper):
    bl_idname = 'vecxy.export_character'
    bl_label = 'Export Character'
    bl_description = 'Export the character collection as GLB with optional face data'
    filename_ext = '.glb'
    filter_glob: StringProperty(default='*.glb', options={'HIDDEN'})

    @classmethod
    def poll(cls, context):
        return context.mode == 'OBJECT' and is_character(active_collection(context))

    def invoke(self, context, event):
        try:
            root = asset_directory(context)
            collection = active_collection(context)
            name = bpy.path.clean_name(collection.name)
            self.filepath = str(root / 'Characters' / f'{name}.glb')
        except ValueError:
            pass
        return ExportHelper.invoke(self, context, event)

    def execute(self, context):
        collection = active_collection(context)
        try:
            root, meshes, rigs, options, mapping, warnings = character_check(context, collection)
            output = Path(self.filepath).with_suffix('.glb').resolve()
            if not output.is_relative_to(root):
                raise ValueError('Save the character inside the project Assets directory')
            prototype = output.with_suffix('.face.prototype')
            character = output.with_suffix('.character.prototype')
            if not options and prototype.exists():
                raise ValueError(f'{prototype.name} already exists. Choose another filename or move the old face file')
            for file in (output, character, prototype):
                if file.exists() and not file.is_file():
                    raise ValueError(f'{file.name} must be a file')
            output.parent.mkdir(parents=True, exist_ok=True)
            members = export_members(collection, meshes, rigs)
            bindings = authored_nodes(context, members)
            require_skin = any(modifier.type == 'ARMATURE' and modifier.object
                               for mesh in meshes for modifier in mesh.modifiers)
            attachment_bones = [obj.parent.data.bones.get(obj.parent_bone) for obj in members
                                if obj.parent_type == 'BONE' and obj.parent and obj.parent.type == 'ARMATURE']
            deform_only = all(bone and bone.use_deform for bone in attachment_bones)
            start = collection.vecxy_character.default_animation.strip() if collection.vecxy_character.clip_mode != 'NONE' else ''
            selected = list(context.selected_objects)
            hidden = {obj: obj.hide_get() for obj in members}
            missing = object()
            markers = {obj: obj.get(NODE_MARKER, missing) for obj in bindings}
            try:
                bpy.ops.object.select_all(action='DESELECT')
                for obj in members:
                    obj.hide_set(False)
                    obj.select_set(True)
                for obj in bindings:
                    obj[NODE_MARKER] = obj.name
                with tempfile.TemporaryDirectory(dir=output.parent) as temporary:
                    glb = Path(temporary) / output.name
                    face = Path(temporary) / prototype.name
                    if options:
                        face.write_text(json.dumps({'format': 1, 'type': face_sprites.TYPE, 'data': options},
                                                   indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
                    with action_filter(mapping):
                        result = bpy.ops.export_scene.gltf(
                            filepath=str(glb), export_format='GLB', use_selection=True, export_attributes=True,
                            export_animations=collection.vecxy_character.clip_mode != 'NONE',
                            export_skins=True, export_animation_mode='ACTIONS', export_merge_animation='ACTION',
                            export_anim_single_armature=True, export_def_bones=deform_only, export_extras=bool(bindings),
                            export_morph=False)
                    if 'FINISHED' not in result or not glb.is_file():
                        raise RuntimeError('Blender could not export this character')
                    count, nodes = finish_glb(glb, mapping, require_skin,
                                              {obj.name: binding for obj, binding in bindings.items()}, start)
                    descriptor = Path(temporary) / character.name
                    descriptor.write_text(json.dumps({'format': 1, 'type': 'Vecxy.Animations.Character', 'data': {
                        'name': collection.name, 'model': output.relative_to(root).as_posix(),
                        'face': prototype.relative_to(root).as_posix() if options else '',
                        'defaultAnimation': start, 'nodes': nodes}}, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
                    glb.replace(output)
                    if options:
                        face.replace(prototype)
                    descriptor.replace(character)
            finally:
                for obj, marker in markers.items():
                    if marker is missing:
                        if NODE_MARKER in obj:
                            del obj[NODE_MARKER]
                    else:
                        obj[NODE_MARKER] = marker
                bpy.ops.object.select_all(action='DESELECT')
                for obj, state in hidden.items():
                    obj.hide_set(state)
                for obj in selected:
                    obj.select_set(True)
        except (OSError, RuntimeError, ValueError, struct.error) as error:
            self.report({'ERROR'}, str(error))
            return {'CANCELLED'}
        for warning in warnings:
            self.report({'WARNING'}, warning)
        self.report({'INFO'}, f'Exported {character.name} · {count} clips · {len(nodes)} configured nodes')
        return {'FINISHED'}


class VECXY_UL_character_clips(bpy.types.UIList):
    def draw_item(self, _context, layout, _data, clip, _icon, _active_data, _active_prop, _index):
        row = layout.row(align=True)
        row.prop(clip, 'enabled', text='')
        row.label(text=clip.export_name or (clip.action.name if clip.action else 'Choose an Action'))


class VECXY_PT_character(bpy.types.Panel):
    bl_idname = 'VECXY_PT_character'
    bl_label = 'Character'
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = 'Vecxy'
    bl_options = {'DEFAULT_CLOSED'}

    def draw(self, context):
        layout = self.layout
        layout.prop(context.scene, 'vecxy_character_collection', text='Collection')
        collection = active_collection(context)
        if not is_character(collection):
            layout.operator(VECXY_OT_character_from_selection.bl_idname, icon='OUTLINER_OB_ARMATURE')
            if collection:
                layout.prop(collection.vecxy_character, 'enabled')
            return
        meshes, rigs = character_members(collection)
        layout.label(text=f'{len(meshes)} meshes · {len(rigs)} rigs')
        layout.prop(collection.vecxy_character, 'enabled')
        row = layout.row(align=True)
        row.operator(VECXY_OT_check_character.bl_idname, icon='CHECKMARK')
        row.operator(VECXY_OT_export_character.bl_idname, icon='EXPORT')


class VECXY_PT_character_animation(bpy.types.Panel):
    bl_label = 'Animations'
    bl_parent_id = 'VECXY_PT_character'
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = 'Vecxy'

    @classmethod
    def poll(cls, context):
        return is_character(active_collection(context))

    def draw(self, context):
        layout = self.layout
        settings = active_collection(context).vecxy_character
        layout.prop(settings, 'clip_mode')
        if settings.clip_mode == 'NONE':
            return
        layout.prop(settings, 'default_animation')
        layout.operator(VECXY_OT_character_clip_list.bl_idname, text='Find Actions', icon='ACTION').action = 'COLLECT'
        if settings.clip_mode != 'LIST':
            return
        row = layout.row()
        row.template_list('VECXY_UL_character_clips', '', settings, 'clips', settings, 'clip_index', rows=4)
        controls = row.column(align=True)
        controls.operator(VECXY_OT_character_clip_list.bl_idname, text='', icon='ADD').action = 'ADD'
        controls.operator(VECXY_OT_character_clip_list.bl_idname, text='', icon='REMOVE').action = 'REMOVE'
        if 0 <= settings.clip_index < len(settings.clips):
            clip = settings.clips[settings.clip_index]
            layout.prop(clip, 'action')
            layout.prop(clip, 'export_name')


CLASSES = (VECXY_CharacterClip, VECXY_CharacterSettings, VECXY_OT_character_from_selection,
           VECXY_OT_character_clip_list, VECXY_OT_select_face_mesh, VECXY_OT_use_active_face_mesh, VECXY_OT_check_character,
           VECXY_OT_export_character, VECXY_UL_character_clips, VECXY_PT_character,
           VECXY_PT_character_animation)


def register():
    for cls in CLASSES:
        bpy.utils.register_class(cls)
    bpy.types.Collection.vecxy_character = PointerProperty(type=VECXY_CharacterSettings)
    bpy.types.Scene.vecxy_character_collection = PointerProperty(type=bpy.types.Collection)


def unregister():
    del bpy.types.Scene.vecxy_character_collection
    del bpy.types.Collection.vecxy_character
    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)
