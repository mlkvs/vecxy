"""Blender navigation authoring and preview. The Vecxy CLI performs the bake."""
import json
import math
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

import bpy
from bpy.props import BoolProperty, EnumProperty, FloatProperty, PointerProperty, StringProperty
from mathutils import Matrix, Quaternion, Vector

from . import characters, collider_mesh, instance_collection, schema
from .decals import asset_directory
from .exporter import finish_prototype_edits, resolved_path

OBSTACLE = 'Vecxy.AI.NavigationObstacle'
BLOCKER = '_vecxy_navigation_blocker'
BAKE_AREA = '_vecxy_navigation_bake_area'
BOX_TRIANGLES = ((0, 1, 2), (0, 2, 3), (4, 6, 5), (4, 7, 6),
                 (0, 4, 5), (0, 5, 1), (1, 5, 6), (1, 6, 2),
                 (2, 6, 7), (2, 7, 3), (3, 7, 4), (3, 4, 0))
BOX_EDGES = ((0, 1), (1, 2), (2, 3), (3, 0), (4, 5), (5, 6), (6, 7), (7, 4),
             (0, 4), (1, 5), (2, 6), (3, 7))
_handle = None
_preview_cache = {}
_gpu_cache = {}


def options(context):
    return context.scene.vecxy_navigation


def is_authoring_volume(obj):
    return obj.get(BLOCKER) or obj.get(BAKE_AREA)


def bake_area(context):
    area = options(context).area
    return area if area and area.name in context.scene.objects and area.get(BAKE_AREA) else None


def redraw(_self, context):
    if context.screen:
        for area in context.screen.areas:
            if area.type == 'VIEW_3D':
                area.tag_redraw()


def update_scope(settings, context):
    area = bake_area(context)
    if area and area.name in context.view_layer.objects:
        area.hide_set(settings.scope != 'BOX')
    redraw(settings, context)


class NavigationSettings(bpy.types.PropertyGroup):
    show_bake: BoolProperty(name='Show Baked Navigation', update=redraw)
    scope: EnumProperty(name='Bake area', items=[
        ('SCENE', 'Entire Scene', 'Fit the bake to all static colliders in the scene'),
        ('BOX', 'Area Box', 'Limit the bake to a box you can move and scale in the viewport')
    ], default='SCENE', update=update_scope)
    area: PointerProperty(type=bpy.types.Object)
    cell_size: FloatProperty(name='Cell Size', default=.2, min=.025, unit='LENGTH',
                             description='Smaller cells capture more detail and take longer to bake')
    radius: FloatProperty(name='Radius', default=.3, min=0, unit='LENGTH',
                          description='Distance to leave between the center of the bot and obstacles')
    height: FloatProperty(name='Height', default=1.8, min=.01, unit='LENGTH',
                          description='Free space the bot needs above the floor')
    max_slope: FloatProperty(name='Max Slope (°)', default=45, min=0, max=70,
                             description='Steepest ramp the bot can walk up')
    max_step: FloatProperty(name='Max Step Height', default=.3, min=0, unit='LENGTH',
                            description='Largest height change the bot can step over')
    cli_path: StringProperty(subtype='FILE_PATH', options={'HIDDEN'})
    preview_blockers: BoolProperty(name='Show navigation volumes', default=True, update=redraw)


def map_path(context):
    return resolved_path(context.scene.vecxy_usd.export_path).with_suffix('.navigation.json')


def engine(point):
    return dict(x=point[0], y=point[2], z=-point[1])


def blender(point):
    return Vector((point['x'], -point['z'], point['y']))


def vector(data, key, fallback):
    return Vector(tuple(data[key][axis] for axis in 'xyz') if key in data else fallback)


def box(matrix, size):
    linear = matrix.to_3x3()
    return dict(center=engine(matrix.translation),
                right=engine(linear @ Vector((size[0] * .5, 0, 0))),
                up=engine(linear @ Vector((0, size[1] * .5, 0))),
                forward=engine(linear @ Vector((0, 0, size[2] * .5))))


def box_corners(volume):
    at, x, y, z = (blender(volume[key]) for key in ('center', 'right', 'up', 'forward'))
    return (at-x-y-z, at+x-y-z, at+x-y+z, at-x-y+z, at-x+y-z, at+x+y-z, at+x+y+z, at-x+y+z)


def box_triangles(volume):
    points = box_corners(volume)
    mirrored = (points[1]-points[0]).cross(points[4]-points[0]).dot(points[3]-points[0]) < 0
    for a, b, c in BOX_TRIANGLES:
        if mirrored:
            b, c = c, b
        yield dict(a=engine(points[a]), b=engine(points[b]), c=engine(points[c]))


def collider_geometry(kind, data, matrix, geometry):
    center = vector(data, 'center', (0, 0, 0))
    if kind == 'MESH':
        vertices, indices = geometry()
        local = matrix @ Matrix.Translation(center)
        corners = [local @ vertex for vertex in vertices]
        if not corners:
            return [], None
        mirrored = local.to_3x3().determinant() < 0
        triangles = []
        for index in range(0, len(indices), 3):
            a, b, c = indices[index:index+3]
            if mirrored:
                b, c = c, b
            triangles.append(dict(a=engine(corners[a]), b=engine(corners[b]), c=engine(corners[c])))
        low = Vector(tuple(min(point[i] for point in vertices) for i in range(3)))
        high = Vector(tuple(max(point[i] for point in vertices) for i in range(3)))
        return triangles, box(local @ Matrix.Translation((low+high)*.5),
                              tuple(max(.001, high[i]-low[i]) for i in range(3)))

    position, rotation, signed_scale = matrix.decompose()
    scale = tuple(abs(value) for value in signed_scale)
    transform = Matrix.Translation(position) @ rotation.to_matrix().to_4x4() @ Matrix.Translation(
        Vector(tuple(center[i] * scale[i] for i in range(3))))
    if kind == 'BOX':
        size = vector(data, 'size', (1, 1, 1))
        dimensions = tuple(size[i] * scale[i] for i in range(3))
    elif kind == 'SPHERE':
        dimensions = (2 * data.get('radius', .5) * max(scale),) * 3
        transform = Matrix.Translation(transform.translation)
    else:
        axis = data.get('axis', 'Z')
        rotate = {'X': Quaternion((0, 0, 1), -1.5707963267948966),
                  'Z': Quaternion((1, 0, 0), 1.5707963267948966)}.get(axis)
        if rotate:
            transform @= rotate.to_matrix().to_4x4()
        diameter = 2 * data.get('radius', .35) * max(scale[0], scale[2])
        dimensions = (diameter, diameter + data.get('height', 1.1) * scale[1], diameter)
    volume = box(transform, dimensions)
    triangles = list(box_triangles(volume))
    if kind != 'BOX':
        for triangle in triangles:
            triangle['supportsWalking'] = False
    return triangles, volume


def capture(context, use_area=True):
    settings = options(context)
    depsgraph = context.evaluated_depsgraph_get()
    triangles, blockers, obstacles, solids = [], [], [], []
    scene_objects = context.scene.objects
    source = {member for col in bpy.data.collections if col.vecxy.is_prototype or col.vecxy.is_edit_proxy
              for member in col.all_objects}
    excluded = characters.character_objects()

    def visit_group(objects, transform, inherited_obstacle, trail):
        members = set(objects)

        def visit(obj, under_obstacle):
            matrix = transform(obj)
            if obj.get(BAKE_AREA):
                return []
            if obj.get(BLOCKER):
                if not obj.hide_render:
                    blockers.append(box(matrix, (1, 1, 1)))
                return []

            col = instance_collection(obj)
            defaults = col.vecxy if col else None
            exported = not obj.hide_render and not obj.get('_vecxy_decal_preview') and characters.scene_object(obj, excluded)
            entries = list(schema.effective_components(obj.vecxy, defaults)) if exported else []
            obstacle = next((data for comp, data in entries if comp.enabled and comp.type_name == OBSTACLE), None)
            dynamic = under_obstacle or obstacle is not None
            is_static = (defaults.is_static if defaults and not obj.vecxy.override_static else obj.vecxy.is_static)
            boxes = []
            geometry = None

            for comp, data in entries:
                if not comp.enabled or data.get('isTrigger') or comp.type_name not in schema.COLLIDERS:
                    continue
                if not is_static and not dynamic:
                    continue

                def get_geometry():
                    nonlocal geometry
                    if geometry is None:
                        if col and any(c.key == comp.key and c.enabled for c in defaults.components):
                            geometry = collider_mesh.triangles(depsgraph, collection=col)
                        else:
                            geometry = collider_mesh.triangles(depsgraph, obj=obj)
                    return geometry

                faces, volume = collider_geometry(schema.COLLIDERS[comp.type_name], data, matrix, get_geometry)
                if volume is not None:
                    boxes.append(volume)
                if not dynamic:
                    triangles.extend(faces)
                    if volume is not None and comp.type_name != schema.MESH_COLLIDER:
                        solids.append(volume)

            for child in obj.children:
                if child in members:
                    boxes.extend(visit(child, dynamic))
            if exported and col and col not in trail:
                offset = matrix @ Matrix.Translation(-col.instance_offset)
                boxes.extend(visit_group(col.all_objects, lambda member: offset @ member.matrix_world,
                                         dynamic, (*trail, col)))

            if obstacle is not None:
                if obstacle.get('useColliders', True) and boxes:
                    obstacles.extend(boxes)
                else:
                    local = Matrix.Translation(vector(obstacle, 'center', (0, 0, 0)))
                    obstacles.append(box(matrix @ local, vector(obstacle, 'size', (1, 1, 1))))
            return boxes

        result = []
        for obj in objects:
            if obj.parent not in members:
                result.extend(visit(obj, inherited_obstacle))
        return result

    visit_group([obj for obj in scene_objects if obj not in source],
                lambda obj: obj.evaluated_get(depsgraph).matrix_world, False, ())
    if not triangles:
        raise ValueError('No static solid colliders were found to bake')
    if use_area and settings.scope == 'BOX':
        area = bake_area(context)
        if area is None:
            raise ValueError('Create an Area Box or choose Entire Scene')
        points = [engine(point) for point in box_corners(box(area.matrix_world, (1, 1, 1)))]
        low = {axis: min(point[axis] for point in points) for axis in 'xyz'}
        high = {axis: max(point[axis] for point in points) for axis in 'xyz'}
        if high['y'] <= low['y'] or any(high[axis] - low[axis] < settings.cell_size for axis in 'xz'):
            raise ValueError('Scale up the Area Box so it has room for at least one navigation cell')
    else:
        low, high = scene_bounds(triangles, settings.cell_size)
    return dict(settings=dict(min=low, max=high,
                              cellSize=settings.cell_size, radius=settings.radius, height=settings.height,
                              maxSlope=settings.max_slope, maxStep=settings.max_step),
                triangles=triangles, blockers=blockers, obstacles=obstacles, solidVolumes=solids)


def scene_bounds(triangles, cell):
    minimum = dict.fromkeys('xyz', math.inf)
    maximum = dict.fromkeys('xyz', -math.inf)
    for triangle in triangles:
        for vertex in ('a', 'b', 'c'):
            point = triangle[vertex]
            for axis in 'xyz':
                minimum[axis] = min(minimum[axis], point[axis])
                maximum[axis] = max(maximum[axis], point[axis])
    # Snap the automatic bounds to cell lines and leave a cell around the geometry.
    return ({axis: (math.floor(minimum[axis] / cell) - 1) * cell for axis in 'xyz'},
            {axis: (math.ceil(maximum[axis] / cell) + 1) * cell for axis in 'xyz'})


def bake(context):
    if not bpy.data.filepath:
        raise ValueError('Save the Blender file before baking navigation')
    root = asset_directory(context)
    output = map_path(context)
    if not output.is_relative_to(root):
        raise ValueError('Set the USD export path inside Assets before baking navigation')
    configured = options(context).cli_path
    executable = bpy.path.abspath(configured) if configured else shutil.which('vecxy')
    if not executable:
        fallback = Path.home() / '.local/bin/vecxy'
        if fallback.is_file():
            executable = str(fallback)
    if not executable:
        raise ValueError('Vecxy CLI was not found. Install the vecxy npm CLI and reopen Blender')
    if os.name == 'nt' and Path(executable).suffix.lower() == '.cmd':
        command = [os.environ.get('COMSPEC', 'cmd.exe'), '/d', '/c', executable]
    else:
        command = [executable]
    finish_prototype_edits(context)
    payload = capture(context)
    with tempfile.TemporaryDirectory(prefix='vecxy-navigation-') as work:
        path = Path(work) / 'navigation-input.json'
        path.write_text(json.dumps(payload, separators=(',', ':'), allow_nan=False), encoding='utf-8')
        result = subprocess.run([*command, 'navigation', 'bake', str(path), '--output', str(output)],
                                cwd=root.parent, capture_output=True, text=True,
                                env={**os.environ, 'PATH': str(Path(executable).parent) + os.pathsep + os.environ.get('PATH', '')})
    if result.returncode:
        raise RuntimeError((result.stderr or result.stdout).strip()[-1800:])
    _preview_cache.clear()
    _gpu_cache.clear()
    return output


class VECXY_OT_bake_navigation(bpy.types.Operator):
    bl_idname = 'vecxy.bake_navigation'
    bl_label = 'Bake / Rebuild Navigation'
    bl_description = 'Bake static colliders and navigation blockers with the Vecxy CLI'

    def execute(self, context):
        try:
            output = bake(context)
            document = json.loads(output.read_text(encoding='utf-8'))
            count = sum(height is not None for height in document['heights'])
            options(context).show_bake = True
            if count == 0:
                self.report({'WARNING'}, 'No walkable cells. Check bounds, colliders and agent dimensions')
            else:
                self.report({'INFO'}, f'Baked {count} walkable cells to {output.name}')
            return {'FINISHED'}
        except (OSError, ValueError, RuntimeError) as error:
            self.report({'ERROR'}, str(error))
            return {'CANCELLED'}


class VECXY_OT_navigation_blocker(bpy.types.Operator):
    bl_idname = 'vecxy.add_navigation_blocker'
    bl_label = 'Add Navigation Blocker'
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        if context.mode != 'OBJECT':
            self.report({'ERROR'}, 'Switch to Object Mode before placing a blocker')
            return {'CANCELLED'}
        blocker = bpy.data.objects.new('Navigation Blocker', None)
        blocker[BLOCKER] = True
        blocker.empty_display_type = 'CUBE'
        blocker.empty_display_size = .5
        blocker.location = context.scene.cursor.location + Vector((0, 0, .5))
        context.collection.objects.link(blocker)
        for obj in context.selected_objects:
            obj.select_set(False)
        blocker.select_set(True)
        context.view_layer.objects.active = blocker
        redraw(None, context)
        return {'FINISHED'}


class VECXY_OT_navigation_bake_area(bpy.types.Operator):
    bl_idname = 'vecxy.navigation_bake_area'
    bl_label = 'Select Area Box'
    bl_options = {'REGISTER', 'UNDO'}

    fit: BoolProperty(name='Fit to Scene', options={'SKIP_SAVE'})

    def execute(self, context):
        if context.mode != 'OBJECT':
            self.report({'ERROR'}, 'Switch to Object Mode to edit the Area Box')
            return {'CANCELLED'}
        area = bake_area(context)
        if self.fit or area is None:
            try:
                finish_prototype_edits(context)
                bounds = capture(context, use_area=False)['settings']
            except (ValueError, RuntimeError) as error:
                self.report({'ERROR'}, str(error))
                return {'CANCELLED'}
            if area is None:
                area = bpy.data.objects.new('Navigation Area', None)
                area[BAKE_AREA] = True
                area.empty_display_type = 'CUBE'
                area.empty_display_size = .5
                area.lock_rotation = (True, True, True)
                context.scene.collection.objects.link(area)
                options(context).area = area
            a, b = blender(bounds['min']), blender(bounds['max'])
            size = b - a
            area.matrix_world = Matrix.Translation((a + b) * .5) @ Matrix.Diagonal(
                Vector((abs(size.x), abs(size.y), abs(size.z), 1)))
            context.view_layer.update()
        if area.name not in context.view_layer.objects:
            self.report({'ERROR'}, 'Show the Area Box collection in the active view layer')
            return {'CANCELLED'}
        area.hide_set(False)
        for obj in context.selected_objects:
            obj.select_set(False)
        area.select_set(True)
        context.view_layer.objects.active = area
        redraw(None, context)
        return {'FINISHED'}


class VECXY_PT_ai(bpy.types.Panel):
    bl_label = 'AI'
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = 'Vecxy'
    bl_order = 24

    def draw(self, context):
        pass


class VECXY_PT_navigation(bpy.types.Panel):
    bl_label = 'Navigation'
    bl_parent_id = 'VECXY_PT_ai'
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'

    def draw(self, context):
        layout = self.layout
        settings = options(context)
        layout.prop(settings, 'show_bake', text='Show Baked Navigation')
        layout.prop(settings, 'preview_blockers', text='Show Navigation Volumes')
        layout.prop(settings, 'scope', expand=True)
        if settings.scope == 'BOX':
            row = layout.row(align=True)
            area = bake_area(context)
            row.operator('vecxy.navigation_bake_area', text='Select Box' if area else 'Create Box',
                         icon='CUBE').fit = False
            if area:
                row.operator('vecxy.navigation_bake_area', text='Fit to Scene', icon='FULLSCREEN_ENTER').fit = True
                layout.operator('vecxy.edit_navigation_box', text='Edit Area in Viewport', icon='GIZMO').kind = 'AREA'
        layout.operator('vecxy.bake_navigation', icon='FILE_REFRESH')
        layout.operator('vecxy.add_navigation_blocker', text='Add Blocker', icon='CUBE')
        agent = layout.box()
        agent.label(text='Bot Size')
        agent.prop(settings, 'radius')
        agent.prop(settings, 'height')
        if context.object and context.object.get(BLOCKER):
            layout.operator('vecxy.edit_navigation_box', text='Edit Blocker in Viewport', icon='GIZMO').kind = 'BLOCKER'


class VECXY_PT_navigation_advanced(bpy.types.Panel):
    bl_label = 'Bake Settings'
    bl_parent_id = 'VECXY_PT_navigation'
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_options = {'DEFAULT_CLOSED'}

    def draw(self, context):
        settings = options(context)
        for prop in ('cell_size', 'max_slope', 'max_step'):
            self.layout.prop(settings, prop)


def preview_vertices(path):
    stamp = path.stat().st_mtime_ns
    if path in _preview_cache and _preview_cache[path][0] == stamp:
        return _preview_cache[path][1:]
    document = json.loads(path.read_text(encoding='utf-8'))
    settings = document['settings']
    size = settings['cellSize']
    origin = settings['min']
    columns = document['columns']
    gradients = document.get('gradients') or {}
    blocked = set(document.get('previewBlocked') or ())
    green, red = [], []
    for index, height in enumerate(document['heights']):
        if height is None:
            continue
        z, x = divmod(index, columns)
        x = origin['x'] + x * size
        y = -origin['z'] - z * size
        gradient = gradients.get(str(index), {'x': 0, 'y': 0})
        points = tuple((x+dx*size, y-dz*size,
                        height+.027 + ((dx-.5)*gradient['x'] + (dz-.5)*gradient['y'])*size)
                       for dx, dz in ((0, 0), (1, 0), (1, 1), (0, 1)))
        (red if index in blocked else green).extend(points[i] for i in (0, 2, 1, 0, 3, 2))
    _preview_cache[path] = stamp, green, red
    return green, red


def draw_preview():
    import gpu
    from gpu_extras.batch import batch_for_shader

    context = bpy.context
    if context.scene is None or context.space_data is None or not context.space_data.overlay.show_overlays:
        return
    settings = options(context)
    if not settings.show_bake and not settings.preview_blockers:
        return
    shader = gpu.shader.from_builtin('UNIFORM_COLOR')

    def create_batch(vertices, primitive='TRIS'):
        return batch_for_shader(shader, primitive, {'pos': vertices}) if vertices else None

    def draw(batch, color):
        if batch is not None:
            shader.bind()
            shader.uniform_float('color', color)
            batch.draw(shader)

    gpu.state.blend_set('ALPHA')
    gpu.state.depth_test_set('LESS_EQUAL')
    gpu.state.depth_mask_set(False)
    try:
        if settings.show_bake:
            try:
                path = map_path(context)
                stamp = path.stat().st_mtime_ns
                cached = _gpu_cache.get(path)
                if cached is None or cached[0] != stamp:
                    green, red = preview_vertices(path)
                    cached = stamp, create_batch(green), create_batch(red)
                    _gpu_cache[path] = cached
                _, green, red = cached
            except (OSError, RuntimeError, ValueError, KeyError):
                green, red = None, None
            draw(green, (.10, .82, .45, .38))
            draw(red, (.95, .19, .13, .55))
        if settings.preview_blockers:
            vertices = []
            for obj in context.scene.objects:
                if obj.get(BLOCKER) and obj.visible_get():
                    points = box_corners(box(obj.matrix_world, (1, 1, 1)))
                    vertices.extend(points[i] for triangle in BOX_TRIANGLES for i in triangle)
            draw(create_batch(vertices), (.92, .21, .55, .26))
            from . import navigation_edit
            outlines, selected_faces = [], []
            for obj, matrix in navigation_edit.custom_boxes(context):
                points = box_corners(box(matrix, (1, 1, 1)))
                outlines.extend(points[i] for edge in BOX_EDGES for i in edge)
                if obj.select_get():
                    selected_faces.extend(points[i] for triangle in BOX_TRIANGLES for i in triangle)
            draw(create_batch(selected_faces), (1, .43, .16, .16))
            draw(create_batch(outlines, 'LINES'), (1, .43, .16, 1))
            area = bake_area(context) if settings.scope == 'BOX' else None
            if area and area.visible_get():
                points = box_corners(box(area.matrix_world, (1, 1, 1)))
                edges = [points[i] for edge in BOX_EDGES for i in edge]
                draw(create_batch(edges, 'LINES'), (.25, .72, 1, 1))
    finally:
        gpu.state.depth_mask_set(True)
        gpu.state.depth_test_set('NONE')
        gpu.state.blend_set('NONE')


_classes = (NavigationSettings, VECXY_OT_bake_navigation, VECXY_OT_navigation_blocker,
            VECXY_OT_navigation_bake_area, VECXY_PT_ai,
            VECXY_PT_navigation, VECXY_PT_navigation_advanced)


def register():
    from . import navigation_edit
    global _handle
    navigation_edit.register()
    for cls in _classes:
        bpy.utils.register_class(cls)
    bpy.types.Scene.vecxy_navigation = PointerProperty(type=NavigationSettings)
    if not bpy.app.background:
        _handle = bpy.types.SpaceView3D.draw_handler_add(draw_preview, (), 'WINDOW', 'POST_VIEW')


def unregister():
    from . import navigation_edit
    global _handle
    if _handle is not None:
        bpy.types.SpaceView3D.draw_handler_remove(_handle, 'WINDOW')
        _handle = None
    del bpy.types.Scene.vecxy_navigation
    for cls in reversed(_classes):
        bpy.utils.unregister_class(cls)
    navigation_edit.unregister()
    _preview_cache.clear()
    _gpu_cache.clear()
