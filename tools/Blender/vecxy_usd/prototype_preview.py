"""Inspect authored components and collider geometry in the viewport."""
from itertools import chain
from typing import NamedTuple

import bpy
from bpy_extras import view3d_utils
from mathutils import Matrix, Vector

from . import collider_edit, collider_mesh, schema
from .level_design import shared

_MOVABLE = (.64, 1, .24, .9)
_STATIC = (.12, .95, .66, .9)
_TRIGGER = (.88, .48, 1, .96)
_EDITING = (1, .7, .2, 1)
_mouse = {}
_keymap = None
_handles = []


class PreviewNode(NamedTuple):
    owner: object
    settings: object
    defaults: object
    matrix: Matrix
    path: tuple

    @property
    def static(self):
        return self.settings.is_static if self.defaults is None or self.settings.override_static else self.defaults.is_static

    @property
    def role(self):
        return self.settings.role if self.defaults is None or self.settings.override_role else self.defaults.role


def collection_nodes(collection, matrix, path, seen=()):
    if collection in seen:
        return
    transform = matrix @ Matrix.Translation(-collection.instance_offset)
    for member in collection.all_objects:
        if not member.hide_render:
            yield from object_nodes(member, transform @ member.matrix_world, path, (*seen, collection))


def object_nodes(obj, matrix=None, path=(), seen=(), preview_defaults=False):
    matrix = obj.matrix_world if matrix is None else matrix
    col = shared(obj)
    path = (*path, obj)
    defaults = col.vecxy if col else None
    settings = defaults if preview_defaults and defaults is not None else obj.vecxy
    yield PreviewNode(obj, settings, None if settings == defaults else defaults, matrix, path)
    if obj.instance_type == 'COLLECTION' and obj.instance_collection:
        yield from collection_nodes(obj.instance_collection, matrix, path, seen)


def visible_components(node):
    return [(comp, data) for comp, data in schema.effective_components(node.settings, node.defaults) if comp.enabled]


def node_description(node):
    labels = []
    for comp, data in visible_components(node):
        label = schema.CATALOG.get(comp.type_name, {}).get('label', comp.type_name.rsplit('.', 1)[-1])
        if data.get('isTrigger'):
            label += ' (trigger)'
        if comp.type_name == schema.MESH_COLLIDER and data.get('doubleSided'):
            label += ' (double sided)'
        labels.append(label)
    return labels


def path_label(path):
    def label(obj):
        col = shared(obj)
        return col.vecxy.asset_name if col else obj.name
    return ' / '.join(label(obj) for obj in path)


def collider_style(node, shape):
    if shape.is_trigger:
        return _TRIGGER, True
    if collider_edit.is_editing(node.owner, shape.key):
        return _EDITING, False
    return (_STATIC if node.static else _MOVABLE), False


def same_matrix(first, second):
    return all((a - b).length_squared < 1e-10 for a, b in zip(first, second))


def resolve_hit_owner(context, depsgraph, obj, matrix):
    obj = obj.original
    if context.view_layer.objects.get(obj.name) == obj and same_matrix(obj.matrix_world, matrix):
        return obj
    for instance in depsgraph.object_instances:
        if (instance.is_instance and instance.object.original == obj and instance.parent
                and same_matrix(instance.matrix_world, matrix)):
            return instance.parent.original
    return obj


def hit_root(context, mouse):
    direction = view3d_utils.region_2d_to_vector_3d(context.region, context.region_data, mouse)
    origin = view3d_utils.region_2d_to_origin_3d(context.region, context.region_data, mouse, clamp=10000)
    depsgraph = context.evaluated_depsgraph_get()
    hit, _, _, _, obj, matrix = context.scene.ray_cast(depsgraph, origin, direction)
    if hit:
        return resolve_hit_owner(context, depsgraph, obj, matrix), obj.original, matrix
    # A prototype can consist entirely of markers or empty collider objects.
    nearby = []
    for obj in context.visible_objects:
        if not shared(obj) and (obj.type != 'EMPTY' or not (obj.vecxy.role or obj.vecxy.components)):
            continue
        point = view3d_utils.location_3d_to_region_2d(context.region, context.region_data, obj.matrix_world.translation)
        if point is not None and (distance := (point - Vector(mouse)).length_squared) <= 100:
            nearby.append((distance, obj))
    if nearby:
        obj = min(nearby, key=lambda item: item[0])[1]
        return obj, obj, obj.matrix_world
    return None


def hover_info(context):
    if context.mode != 'OBJECT' or not context.scene.vecxy_usd.preview_on_hover or not context.region_data:
        return None
    entry = _mouse.get(context.window.as_pointer())
    if entry is None or entry[0] != context.region.as_pointer():
        return None
    mouse, scene, result = entry[1:]
    if scene != context.scene.as_pointer():
        return None
    if result is None:
        result = hit_root(context, mouse)
        _mouse[context.window.as_pointer()] = entry[:3] + (result or False,)
    if result:
        try:
            result[0].name
        except ReferenceError:
            clear_hover(None)
            return None
    return (mouse, *result) if result else None


def draw_nested_info(layout, obj, settings):
    nodes = object_nodes(obj)
    next(nodes)
    rows = [(node, labels) for node in nodes
            if (labels := node_description(node)) or node.role or node.static or shared(node.owner)]
    if not rows:
        return
    box = layout.box()
    expanded = settings.show_nested_components
    box.prop(settings, 'show_nested_components', text=f'Nested components ({len(rows)})',
             icon='TRIA_DOWN' if expanded else 'TRIA_RIGHT', emboss=False)
    if not expanded:
        return
    for node, labels in rows:
        box.label(text=path_label(node.path[1:]))
        info = ', '.join(labels)
        flags = ', '.join(value for value in (node.role, 'Static' if node.static else 'Movable') if value)
        box.label(text=f'{info} · {flags}' if info else flags)


def mesh_edges(depsgraph, node):
    owner = node.owner
    is_collection = isinstance(owner, bpy.types.Collection)
    vertices, indices = collider_mesh.triangles(depsgraph, collection=owner if is_collection else None,
                                                obj=None if is_collection else owner)
    edges = {tuple(sorted((i,j))) for a,b,c in zip(indices[::3],indices[1::3],indices[2::3])
             for i,j in ((a,b),(b,c),(c,a))}
    return [vertices[i] for edge in edges for i in edge]


def dashed_lines(context, points):
    project = view3d_utils.location_3d_to_region_2d
    for a, b in zip(points[::2], points[1::2]):
        start = project(context.region, context.region_data, a)
        end = project(context.region, context.region_data, b)
        if start is None or end is None:
            yield a
            yield b
            continue
        pieces = min(24, max(1, round((end - start).length / 11)))
        for i in range(pieces):
            yield a.lerp(b, i / pieces)
            yield a.lerp(b, (i + .57) / pieces)


def draw_colliders():
    import gpu
    from gpu_extras.batch import batch_for_shader
    from . import collider_frame, settings_owner

    context = bpy.context
    if context.mode not in {'OBJECT', 'EDIT_MESH'} or not context.space_data.overlay.show_overlays:
        return
    hover = hover_info(context)
    targets = list(context.selected_objects)
    if hover and hover[1] not in targets:
        targets.append(hover[1])
    lines = {}
    roots = set()
    mesh_cache = {}
    depsgraph = context.evaluated_depsgraph_get()
    for selected in targets:
        obj = settings_owner(selected)
        if not obj.vecxy.show_collider:
            continue
        owner, matrix = collider_frame(context, obj)
        if owner in roots:
            continue
        roots.add(owner)
        if owner == obj:
            nodes = object_nodes(obj, preview_defaults=obj.vecxy.edit_defaults)
        else:
            nodes = chain([PreviewNode(owner, owner.vecxy, None, matrix, ())],
                          collection_nodes(owner, matrix, ()))
        for node in nodes:
            if isinstance(node.owner, bpy.types.Object) and not node.owner.vecxy.show_collider:
                continue
            for shape in schema.collider_views(node.settings, node.defaults):
                if shape.collider == 'MESH':
                    if node.owner not in mesh_cache:
                        mesh_cache[node.owner] = mesh_edges(depsgraph, node)
                    local = mesh_cache[node.owner]
                else:
                    local = collider_edit.outline(shape)
                center = Vector(shape.collider_center)
                points = [node.matrix @ (center + point) for point in local]
                color, dashed = collider_style(node, shape)
                lines.setdefault(color, []).extend(dashed_lines(context, points) if dashed else points)
    if not lines:
        return
    shader = gpu.shader.from_builtin('UNIFORM_COLOR')
    gpu.state.blend_set('ALPHA')
    gpu.state.depth_test_set('NONE' if context.scene.vecxy_usd.colliders_xray else 'LESS_EQUAL')
    gpu.state.line_width_set(2)
    try:
        shader.bind()
        for color in sorted(lines, key=lambda color: color == _TRIGGER):
            points = lines[color]
            if points:
                shader.uniform_float('color', color)
                batch_for_shader(shader, 'LINES', {'pos': points}).draw(shader)
    finally:
        gpu.state.line_width_set(1)
        gpu.state.depth_test_set('NONE')
        gpu.state.blend_set('NONE')


def draw_hover():
    import blf

    context = bpy.context
    if not context.space_data.overlay.show_overlays:
        return
    hover = hover_info(context)
    if not hover:
        return
    mouse, root, hit, matrix = hover
    nodes = list(object_nodes(root))
    matching = next((node for node in nodes if node.owner == hit and same_matrix(node.matrix, matrix)), nodes[0])
    trail = [node for node in nodes if node.path == matching.path[:len(node.path)]]
    display = next((node for node in reversed(trail) if node_description(node) or node.role or node.static), matching)
    if not shared(root) and not node_description(display) and not display.role and not display.static:
        return

    lines = [path_label(display.path)]
    details = node_description(display)
    if display.role:
        details.append(display.role)
    details.append('Static' if display.static else 'Movable')
    lines.append(' · '.join(details))
    if len(nodes) > 1:
        count = sum(len(list(schema.collider_views(node.settings, node.defaults))) for node in nodes)
        lines.append(f'Colliders in prototype: {count}')

    blf.size(0, 13)
    x = max(8, min(mouse[0] + 18, context.region.width - max(blf.dimensions(0, line)[0] for line in lines) - 8))
    y = min(context.region.height - 20, max(18 * len(lines), mouse[1] - 14))
    blf.color(0, 1, 1, 1, 1)
    blf.enable(0, blf.SHADOW)
    blf.shadow(0, 5, 0, 0, 0, 1)
    try:
        for line in lines:
            blf.position(0, x, y, 0)
            blf.draw(0, line)
            y -= 18
    finally:
        blf.disable(0, blf.SHADOW)


class VECXY_OT_track_hover(bpy.types.Operator):
    bl_idname = 'vecxy.track_hover'
    bl_label = 'Track prototype hover'
    bl_options = {'INTERNAL'}

    def invoke(self, context, event):
        window = context.window
        key = window.as_pointer()
        old = _mouse.get(key)
        area = context.area
        region = context.region
        if area and area.type == 'VIEW_3D' and region and region.type == 'WINDOW' and context.scene.vecxy_usd.preview_on_hover:
            _mouse[key] = (region.as_pointer(), (event.mouse_x - region.x, event.mouse_y - region.y),
                           context.scene.as_pointer(), None)
            area.tag_redraw()
        elif old:
            del _mouse[key]
        if old and (key not in _mouse or old[0] != _mouse[key][0]):
            for other in window.screen.areas:
                if other.type == 'VIEW_3D':
                    other.tag_redraw()
        return {'PASS_THROUGH'}


@bpy.app.handlers.persistent
def clear_hover(_dummy):
    _mouse.clear()


def register():
    global _keymap
    bpy.utils.register_class(VECXY_OT_track_hover)
    if bpy.app.background:
        return
    configs = bpy.context.window_manager.keyconfigs
    if configs.addon:
        keymap = configs.addon.keymaps.new(name='Window')
        item = keymap.keymap_items.new(VECXY_OT_track_hover.bl_idname, 'MOUSEMOVE', 'ANY', any=True)
        _keymap = keymap, item
    for callback, phase in ((draw_colliders, 'POST_VIEW'), (draw_hover, 'POST_PIXEL')):
        _handles.append(bpy.types.SpaceView3D.draw_handler_add(callback, (), 'WINDOW', phase))
    for handlers in (bpy.app.handlers.load_pre, bpy.app.handlers.undo_pre, bpy.app.handlers.redo_pre):
        handlers.append(clear_hover)


def unregister():
    global _keymap
    if _keymap:
        keymap, item = _keymap
        keymap.keymap_items.remove(item)
        _keymap = None
    for handle in _handles:
        bpy.types.SpaceView3D.draw_handler_remove(handle, 'WINDOW')
    _handles.clear()
    for handlers in (bpy.app.handlers.load_pre, bpy.app.handlers.undo_pre, bpy.app.handlers.redo_pre):
        if clear_hover in handlers:
            handlers.remove(clear_hover)
    clear_hover(None)
    bpy.utils.unregister_class(VECXY_OT_track_hover)
