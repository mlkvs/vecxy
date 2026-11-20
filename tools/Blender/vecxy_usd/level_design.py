"""Shared-prototype layout tools. Placement uses geometry, never the empty's origin."""
import math
import uuid

import bpy
from bpy.props import BoolProperty, EnumProperty, FloatProperty, FloatVectorProperty, IntProperty, PointerProperty, StringProperty
from mathutils import Matrix, Quaternion, Vector
from bpy_extras import view3d_utils

_ACTIVE = set()
_AXES = [('X', 'X', ''), ('Y', 'Y', ''), ('Z', 'Z', '')]


def shared(obj):
    col = obj.instance_collection if obj and obj.instance_type == 'COLLECTION' else None
    return col if col and col.vecxy.is_prototype else None


def library_collection(context):
    index = context.scene.vecxy_layout.library_index
    return bpy.data.collections[index] if 0 <= index < len(bpy.data.collections) and bpy.data.collections[index].vecxy.is_prototype else None


def geometry_parts(obj, matrix=None, seen=None):
    """Mesh originals and world matrices, including nested collection offsets."""
    matrix = obj.matrix_world if matrix is None else matrix
    col = obj.instance_collection if obj.instance_type == 'COLLECTION' else None
    if col:
        seen = set() if seen is None else seen
        if col.as_pointer() in seen:
            return
        seen = seen | {col.as_pointer()}
        transform = matrix @ Matrix.Translation(-col.instance_offset)
        for child in col.all_objects:
            yield from geometry_parts(child, transform @ child.matrix_world, seen)
    elif obj.type == 'MESH':
        yield obj, matrix


def geometry_points(obj, depsgraph, matrix=None):
    return [transform @ Vector(corner)
            for mesh, transform in geometry_parts(obj, matrix)
            for corner in mesh.evaluated_get(depsgraph).bound_box]


def bounds(points):
    if not points:
        raise ValueError('В прототипе нет геометрии')
    return (Vector(tuple(min(p[i] for p in points) for i in range(3))),
            Vector(tuple(max(p[i] for p in points) for i in range(3))))


def box_points(low, high):
    return [Vector((x, y, z)) for x in (low.x, high.x) for y in (low.y, high.y) for z in (low.z, high.z)]


def support_location(points, rotation_scale, hit, normal):
    """Put the geometric support face on hit, preserving the authored pivot."""
    transformed = [rotation_scale @ point for point in points]
    low, high = bounds(transformed)
    center = (low + high) * .5
    minimum = min(p.dot(normal) for p in transformed)
    anchor = center + normal * (minimum - center.dot(normal))
    return hit - anchor


def magnet_offset(points, targets, distance):
    low, high = bounds(points)
    offset = Vector((0, 0, 0))
    for axis in range(3):
        candidates = []
        for other_low, other_high in targets:
            # Only adjacent bounds are eligible. Distant objects cannot pull a piece.
            if any(high[j] < other_low[j] - distance or low[j] > other_high[j] + distance
                   for j in range(3) if j != axis):
                continue
            candidates.extend((other_low[axis] - high[axis], other_high[axis] - low[axis],
                               other_low[axis] - low[axis], other_high[axis] - high[axis]))
        candidates = [delta for delta in candidates if abs(delta) <= distance]
        if candidates:
            offset[axis] = min(candidates, key=abs)
    return offset


def placement_collection(context):
    editing = context.scene.vecxy_usd.editing
    col = context.collection
    if editing or (col and col.vecxy.is_prototype):
        raise ValueError('Сначала вернитесь из редактирования прототипа в сцену')
    if col is None or col.library or col.is_library_indirect:
        col = context.scene.collection
    return col


def create_shared(context, col, source=None):
    target = placement_collection(context)
    obj = source.copy() if source else bpy.data.objects.new(col.vecxy.asset_name + '_Instance', None)
    obj.name = col.vecxy.asset_name + '_Instance'
    obj.instance_type = 'COLLECTION'
    obj.instance_collection = col
    obj.vecxy.identifier = uuid.uuid4().hex
    obj.vecxy.edit_defaults = False
    obj.pop('_vecxy_layout_preview', None)
    target.objects.link(obj)
    if source:
        obj.matrix_world = source.matrix_world.copy()
    else:
        scale = col.get('_vecxy_layout_scale')
        if scale is None:
            # Older .blend files predate saved library defaults. Infer size from
            # an existing placed instance without rewriting the source asset.
            example = next((o for o in context.scene.objects if o != obj and shared(o) == col), None)
            scale = example.matrix_world.decompose()[2] if example else (1, 1, 1)
        obj.scale = scale
    return obj


def select_only(context, objects):
    for obj in context.selected_objects:
        obj.select_set(False)
    for obj in objects:
        obj.select_set(True)
    if objects:
        context.view_layer.objects.active = objects[-1]


class VECXY_LayoutSettings(bpy.types.PropertyGroup):
    library_index: IntProperty(default=0)
    library_search: StringProperty(name='Поиск прототипа', default='')
    grid: FloatProperty(name='Шаг сетки', default=.25, min=.001, subtype='DISTANCE')
    use_grid: BoolProperty(name='Сетка', default=True)
    use_magnet: BoolProperty(name='Магнит по геометрии', default=True)
    magnet_distance: FloatProperty(name='Дистанция магнита', default=.15, min=.001, subtype='DISTANCE')
    align_surface: BoolProperty(name='Ориентировать по поверхности', default=False)
    rotation_step: FloatProperty(name='Шаг вращения', default=math.pi / 2, min=.001, subtype='ANGLE')
    surface_offset: FloatProperty(name='Отступ от поверхности', default=0, subtype='DISTANCE')
    count_x: IntProperty(name='X', default=4, min=1, max=100)
    count_y: IntProperty(name='Y', default=1, min=1, max=100)
    count_z: IntProperty(name='Z', default=1, min=1, max=100)
    use_dimensions: BoolProperty(name='Шаг по размеру геометрии', default=True)
    array_gap: FloatVectorProperty(name='Зазор', size=3, default=(0, 0, 0), subtype='TRANSLATION')
    array_step: FloatVectorProperty(name='Шаг', size=3, default=(2, 2, 2), subtype='TRANSLATION')
    local_axes: BoolProperty(name='Локальные оси активного', default=True)
    snap_axis: EnumProperty(name='Ось стыковки', items=_AXES, default='X')
    snap_side: EnumProperty(name='Сторона', items=[('POSITIVE', '+', ''), ('NEGATIVE', '−', '')])
    snap_gap: FloatProperty(name='Зазор стыковки', default=0, subtype='DISTANCE')


class VECXY_UL_shared_library(bpy.types.UIList):
    def filter_items(self, context, data, propname):
        items = getattr(data, propname)
        needle = context.scene.vecxy_layout.library_search.casefold().strip()
        flags = [self.bitflag_filter_item if col.vecxy.is_prototype and
                 (not needle or needle in (col.vecxy.asset_name + ' ' + col.name).casefold()) else 0 for col in items]
        return flags, []

    def draw_filter(self, context, layout):
        pass  # The always-visible search field belongs to the library panel.

    def draw_item(self, context, layout, data, item, icon, active_data, active_propname, index):
        row = layout.row(align=True)
        row.label(text=item.vecxy.asset_name or item.name, icon='OUTLINER_COLLECTION')
        op = row.operator('vecxy.place_shared', text='', icon='ADD', emboss=False)
        op.collection_name = item.name


class VECXY_OT_library_from_selection(bpy.types.Operator):
    bl_idname = 'vecxy.library_from_selection'
    bl_label = 'Найти выбранный в библиотеке'
    bl_options = {'REGISTER'}

    @classmethod
    def poll(cls, context):
        return bool(shared(context.object))

    def execute(self, context):
        context.scene.vecxy_layout.library_index = list(bpy.data.collections).index(shared(context.object))
        return {'FINISHED'}


class VECXY_OT_add_shared_at_cursor(bpy.types.Operator):
    bl_idname = 'vecxy.add_shared_at_cursor'
    bl_label = 'Добавить у 3D-курсора'
    bl_description = 'Низ геометрии ставится на 3D-курсор; пивот прототипа не меняется'
    bl_options = {'REGISTER', 'UNDO'}

    @classmethod
    def poll(cls, context):
        return context.mode == 'OBJECT' and bool(library_collection(context)) and not context.scene.vecxy_usd.editing

    def execute(self, context):
        try:
            obj = create_shared(context, library_collection(context))
            context.view_layer.update()
            points = geometry_points(obj, context.evaluated_depsgraph_get(), Matrix.Identity(4))
            obj.location = support_location(points, obj.matrix_world.to_3x3(), context.scene.cursor.location, Vector((0, 0, 1)))
            select_only(context, [obj])
        except ValueError as error:
            if 'obj' in locals():
                bpy.data.objects.remove(obj, do_unlink=True)
            self.report({'ERROR'}, str(error))
            return {'CANCELLED'}
        return {'FINISHED'}


class VECXY_OT_place_shared(bpy.types.Operator):
    bl_idname = 'vecxy.place_shared'
    bl_label = 'Расставить мышью'
    bl_description = 'ЛКМ — поставить копию; R — поворот; Shift — без привязки; Enter/Esc — закончить'
    bl_options = {'REGISTER', 'UNDO', 'BLOCKING'}
    collection_name: StringProperty(options={'HIDDEN'})
    from_selection: BoolProperty(default=False, options={'HIDDEN'})
    move_existing: BoolProperty(default=False, options={'HIDDEN'})

    @classmethod
    def poll(cls, context):
        return context.mode == 'OBJECT' and context.area and context.area.type == 'VIEW_3D' and not context.scene.vecxy_usd.editing

    def invoke(self, context, event):
        if _ACTIVE:
            self.report({'WARNING'}, 'Размещение уже запущено')
            return {'CANCELLED'}
        source = context.object if self.from_selection or self.move_existing else None
        col = shared(source) if source else bpy.data.collections.get(self.collection_name) if self.collection_name else library_collection(context)
        if not col or not col.vecxy.is_prototype:
            self.report({'ERROR'}, 'Выберите Shared Prototype в библиотеке или в сцене')
            return {'CANCELLED'}
        self._area = context.area
        self._region = next(r for r in self._area.regions if r.type == 'WINDOW')
        self._view = context.space_data.region_3d
        self._selection = list(context.selected_objects)
        self._active_before = context.object
        self._placed = []
        self._preview = None
        self._handle = None
        self._valid = False
        self._original_matrix = source.matrix_world.copy() if source else None
        try:
            placement_collection(context)
            depsgraph = context.evaluated_depsgraph_get()
            excluded = {source} if self.move_existing else set()
            self._surfaces = [(inst.object.original, inst.matrix_world.copy()) for inst in depsgraph.object_instances
                              if inst.object.type == 'MESH' and inst.show_self and inst.object.original not in excluded
                              and (inst.parent is None or inst.parent.original not in excluded)]
            self._targets = [bounds(points) for obj in context.visible_objects if obj not in excluded and
                             (shared(obj) or obj.type == 'MESH')
                             if (points := geometry_points(obj, depsgraph))]
            self._preview = source if self.move_existing else create_shared(context, col, source)
            if not self.move_existing:
                self._preview['_vecxy_layout_preview'] = True
            context.view_layer.update()
            self._points = geometry_points(self._preview, context.evaluated_depsgraph_get(), Matrix.Identity(4))
            bounds(self._points)  # Reject an empty prototype before entering a modal tool.
            _, self._base_rotation, self._scale = self._preview.matrix_world.decompose()
            self._angle = 0.0
            select_only(context, [self._preview])
            self._handle = bpy.types.SpaceView3D.draw_handler_add(self.draw_preview, (), 'WINDOW', 'POST_VIEW')
            _ACTIVE.add(self)
            context.window_manager.modal_handler_add(self)
            context.window.cursor_set('CROSSHAIR')
            self.update_preview(context, event)
        except Exception as error:
            self.finish(context, cancel=True)
            self.report({'ERROR'}, str(error))
            return {'CANCELLED'}
        return {'RUNNING_MODAL'}

    def ray_hit(self, context, origin, direction):
        closest = None
        depsgraph = context.evaluated_depsgraph_get()
        for mesh, matrix in self._surfaces:
            inv = matrix.inverted_safe()
            local_direction = inv.to_3x3() @ direction
            if local_direction.length_squared < 1e-12:
                continue
            hit, location, normal, _ = mesh.evaluated_get(depsgraph).ray_cast(inv @ origin, local_direction.normalized())
            if not hit:
                continue
            point = matrix @ location
            distance = (point - origin).dot(direction)
            if distance < 0 or (closest and distance >= closest[0]):
                continue
            world_normal = inv.transposed().to_3x3() @ normal
            world_normal.normalize()
            if world_normal.dot(direction) > 0:
                world_normal.negate()
            closest = distance, point, world_normal
        return (closest[1], closest[2]) if closest else None

    def update_preview(self, context, event):
        x, y = event.mouse_x - self._region.x, event.mouse_y - self._region.y
        self._valid = 0 <= x < self._region.width and 0 <= y < self._region.height
        if not self._valid:
            return
        settings = context.scene.vecxy_layout
        origin = view3d_utils.region_2d_to_origin_3d(self._region, self._view, (x, y))
        direction = view3d_utils.region_2d_to_vector_3d(self._region, self._view, (x, y))
        hit = self.ray_hit(context, origin, direction)
        if hit:
            point, normal = hit
        elif abs(direction.z) > 1e-6:
            distance = (context.scene.cursor.location.z - origin.z) / direction.z
            if distance < 0:
                self._valid = False
                return
            point, normal = origin + direction * distance, Vector((0, 0, 1))
        else:
            self._valid = False
            return
        if settings.use_grid and not event.shift:
            # Quantize the tangent coordinates; do not pull a hit off its surface.
            axis = max(range(3), key=lambda i: abs(normal[i]))
            snapped = point.copy()
            for i in range(3):
                if i != axis:
                    snapped[i] = round(point[i] / settings.grid) * settings.grid
            snapped[axis] -= (snapped - point).dot(normal) / normal[axis]
            point = snapped
        if settings.align_surface:
            base_up = self._base_rotation @ Vector((0, 0, 1))
            rotation = Quaternion(normal, self._angle) @ base_up.rotation_difference(normal) @ self._base_rotation
        else:
            rotation = Quaternion(Vector((0, 0, 1)), self._angle) @ self._base_rotation
        basis = Matrix.LocRotScale(Vector(), rotation, self._scale)
        location = support_location(self._points, basis.to_3x3(), point, normal)
        location += normal * settings.surface_offset
        matrix = Matrix.Translation(location) @ basis
        if settings.use_magnet and not event.shift:
            delta = magnet_offset([matrix @ p for p in self._points], self._targets, settings.magnet_distance)
            # Keep the contact plane and surface offset intact while snapping edges.
            delta -= normal * delta.dot(normal)
            matrix.translation += delta
        self._preview.matrix_world = matrix
        self._area.header_text_set('Vecxy: ЛКМ — поставить | R / Shift+R — поворот | G — сетка | M — магнит | A — поверхность | Shift — без привязки | Enter / Esc — готово')
        self._area.tag_redraw()

    def modal(self, context, event):
        if self not in _ACTIVE or self._preview is None:
            return {'CANCELLED'}
        if event.type in {'ESC', 'RIGHTMOUSE', 'RET', 'NUMPAD_ENTER'} and event.value == 'PRESS':
            cancel = event.type in {'ESC', 'RIGHTMOUSE'}
            changed = bool(self._placed) or (self.move_existing and not cancel)
            self.finish(context, cancel=cancel)
            return {'FINISHED'} if changed else {'CANCELLED'}
        if event.type == 'LEFTMOUSE' and event.value == 'PRESS' and self._valid:
            if self.move_existing:
                self.finish(context, cancel=False)
                return {'FINISHED'}
            copy = create_shared(context, shared(self._preview), self._preview)
            self._placed.append(copy)
            self._targets.append(bounds([copy.matrix_world @ p for p in self._points]))
            self._surfaces.extend(geometry_parts(copy))
            return {'RUNNING_MODAL'}
        if event.value == 'PRESS':
            settings = context.scene.vecxy_layout
            if event.type == 'R':
                self._angle += settings.rotation_step * (-1 if event.shift else 1)
            elif event.type == 'G':
                settings.use_grid = not settings.use_grid
            elif event.type == 'M':
                settings.use_magnet = not settings.use_magnet
            elif event.type == 'A':
                settings.align_surface = not settings.align_surface
        if event.type in {'MIDDLEMOUSE', 'WHEELUPMOUSE', 'WHEELDOWNMOUSE', 'NDOF_MOTION'} or event.type.startswith('NUMPAD'):
            return {'PASS_THROUGH'}
        # Camera navigation changes the ray even without mouse motion.
        self.update_preview(context, event)
        return {'RUNNING_MODAL'}

    def draw_preview(self):
        if not self._valid or self._preview is None:
            return
        import gpu
        from gpu_extras.batch import batch_for_shader
        low, high = bounds([self._preview.matrix_world @ p for p in self._points])
        corners = box_points(low, high)
        points = [p for i in range(8) for bit in (1, 2, 4) if i < (i ^ bit) for p in (corners[i], corners[i ^ bit])]
        shader = gpu.shader.from_builtin('UNIFORM_COLOR')
        gpu.state.blend_set('ALPHA')
        gpu.state.depth_test_set('LESS_EQUAL')
        try:
            shader.bind()
            shader.uniform_float('color', (.2, 1, .45, .9))
            batch_for_shader(shader, 'LINES', {'pos': points}).draw(shader)
        finally:
            gpu.state.depth_test_set('NONE')
            gpu.state.blend_set('NONE')

    def finish(self, context, cancel=False):
        _ACTIVE.discard(self)
        if self._handle is not None:
            bpy.types.SpaceView3D.draw_handler_remove(self._handle, 'WINDOW')
            self._handle = None
        if self._preview is not None:
            if self.move_existing:
                if cancel:
                    self._preview.matrix_world = self._original_matrix
            else:
                bpy.data.objects.remove(self._preview, do_unlink=True)
            self._preview = None
        objects = self._placed if self._placed else [o for o in self._selection if o.name in context.view_layer.objects]
        select_only(context, objects)
        if not self._placed and self._active_before and self._active_before.name in context.view_layer.objects:
            context.view_layer.objects.active = self._active_before
        self._area.header_text_set(None)
        self._area.tag_redraw()
        context.window.cursor_set('DEFAULT')

    def cancel(self, context):
        self.finish(context, cancel=True)


class VECXY_OT_duplicate_grid(bpy.types.Operator):
    bl_idname = 'vecxy.duplicate_grid'
    bl_label = 'Создать ряд / сетку копий'
    bl_description = 'Связанные копии без размножения мешей. Число по каждой оси включает оригинал; одна операция Undo'
    bl_options = {'REGISTER', 'UNDO'}

    @classmethod
    def poll(cls, context):
        return context.mode == 'OBJECT' and bool(context.selected_objects) and all(shared(o) for o in context.selected_objects) and not context.scene.vecxy_usd.editing

    def execute(self, context):
        settings = context.scene.vecxy_layout
        sources = list(context.selected_objects)
        counts = settings.count_x, settings.count_y, settings.count_z
        total = (math.prod(counts) - 1) * len(sources)
        if total < 1 or total > 1000:
            self.report({'ERROR'}, 'Нужно от 1 до 1000 новых экземпляров')
            return {'CANCELLED'}
        active = context.object if context.object in sources else sources[0]
        frame = active.matrix_world.to_quaternion().to_matrix().to_4x4() if settings.local_axes else Matrix.Identity(4)
        inverse = frame.inverted()
        depsgraph = context.evaluated_depsgraph_get()
        try:
            low, high = bounds([inverse @ point for obj in sources for point in geometry_points(obj, depsgraph)])
            step = high - low + Vector(settings.array_gap) if settings.use_dimensions else Vector(settings.array_step)
            if any(counts[i] > 1 and abs(step[i]) < 1e-6 for i in range(3)):
                raise ValueError('Шаг копий по используемой оси не может быть нулевым')
            placement_collection(context)
        except ValueError as error:
            self.report({'ERROR'}, str(error))
            return {'CANCELLED'}
        copies = []
        for x in range(counts[0]):
            for y in range(counts[1]):
                for z in range(counts[2]):
                    if (x, y, z) == (0, 0, 0):
                        continue
                    delta = frame.to_3x3() @ Vector((x * step.x, y * step.y, z * step.z))
                    for source in sources:
                        obj = create_shared(context, shared(source), source)
                        obj.matrix_world.translation += delta
                        copies.append(obj)
        select_only(context, copies)
        self.report({'INFO'}, f'Создано экземпляров: {len(copies)}')
        return {'FINISHED'}


class VECXY_OT_snap_to_active(bpy.types.Operator):
    bl_idname = 'vecxy.snap_to_active'
    bl_label = 'Состыковать с активным'
    bl_description = 'Передвинуть выделенную группу вплотную к активному объекту по границам геометрии, без изменения пивотов'
    bl_options = {'REGISTER', 'UNDO'}

    @classmethod
    def poll(cls, context):
        return context.mode == 'OBJECT' and context.object in context.selected_objects and len(context.selected_objects) > 1 and not context.scene.vecxy_usd.editing

    def execute(self, context):
        settings = context.scene.vecxy_layout
        target = context.object
        sources = [o for o in context.selected_objects if o != target]
        # Moving both a parent and its child separately would apply the displacement twice.
        roots = [o for o in sources if not any(p in sources for p in ancestors(o))]
        if any(target in list(o.children_recursive) for o in roots):
            self.report({'ERROR'}, 'Активный объект не должен быть дочерним перемещаемой группы')
            return {'CANCELLED'}
        frame = target.matrix_world.to_quaternion().to_matrix().to_4x4() if settings.local_axes else Matrix.Identity(4)
        inverse = frame.inverted()
        depsgraph = context.evaluated_depsgraph_get()
        def points(obj):
            return [inverse @ p for part in [obj, *obj.children_recursive] for p in geometry_points(part, depsgraph)]
        try:
            low, high = bounds([p for obj in roots for p in points(obj)])
            target_low, target_high = bounds(points(target))
        except ValueError as error:
            self.report({'ERROR'}, str(error))
            return {'CANCELLED'}
        axis = 'XYZ'.index(settings.snap_axis)
        delta = Vector()
        delta[axis] = (target_high[axis] + settings.snap_gap - low[axis] if settings.snap_side == 'POSITIVE'
                       else target_low[axis] - settings.snap_gap - high[axis])
        for obj in roots:
            obj.matrix_world.translation += frame.to_3x3() @ delta
        return {'FINISHED'}


def ancestors(obj):
    while obj.parent:
        obj = obj.parent
        yield obj


class VECXY_PT_level_design(bpy.types.Panel):
    bl_label = 'Расстановка уровня'
    bl_idname = 'VECXY_PT_level_design'
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = 'Vecxy'
    bl_order = -10

    def draw(self, context):
        layout = self.layout
        settings = context.scene.vecxy_layout
        if context.scene.vecxy_usd.editing:
            layout.label(text='Завершите редактирование прототипа', icon='INFO')
            return
        box = layout.box()
        box.label(text='Библиотека Shared Prototype', icon='ASSET_MANAGER')
        if any(c.vecxy.is_prototype for c in bpy.data.collections):
            box.prop(settings, 'library_search', text='', icon='VIEWZOOM')
            box.template_list('VECXY_UL_shared_library', '', bpy.data, 'collections', settings, 'library_index', rows=4)
            col = library_collection(context)
            if col:
                box.label(text=col.vecxy.asset_name, icon='OUTLINER_COLLECTION')
                box.operator('vecxy.place_shared', icon='CURSOR')
                box.operator('vecxy.add_shared_at_cursor', icon='PIVOT_CURSOR')
            else:
                box.label(text='Выберите прототип в списке')
        else:
            box.label(text='Создайте Make shared prototype')
        if shared(context.object):
            box.operator('vecxy.library_from_selection', icon='VIEWZOOM')
            row = box.row(align=True)
            op = row.operator('vecxy.place_shared', text='Копии мышью', icon='DUPLICATE')
            op.from_selection = True
            op = row.operator('vecxy.place_shared', text='Переставить', icon='ORIENTATION_GLOBAL')
            op.move_existing = True
        box = layout.box()
        row = box.row(align=True)
        row.prop(settings, 'use_grid')
        row.prop(settings, 'grid', text='')
        row = box.row(align=True)
        row.prop(settings, 'use_magnet')
        row.prop(settings, 'magnet_distance', text='')
        box.prop(settings, 'align_surface')
        box.prop(settings, 'surface_offset')
        box.prop(settings, 'rotation_step')
        box.label(text='Пивот не меняется. Shift — без привязки.', icon='INFO')
        box = layout.box()
        box.label(text='Ряд / сетка связанных копий', icon='MOD_ARRAY')
        row = box.row(align=True)
        for axis in 'xyz':
            row.prop(settings, 'count_' + axis)
        box.prop(settings, 'local_axes')
        box.prop(settings, 'use_dimensions')
        box.prop(settings, 'array_gap' if settings.use_dimensions else 'array_step')
        box.operator('vecxy.duplicate_grid', icon='DUPLICATE')
        box = layout.box()
        box.label(text='Стыковка: активный остаётся на месте')
        row = box.row(align=True)
        row.prop(settings, 'snap_axis', text='')
        row.prop(settings, 'snap_side', text='')
        box.prop(settings, 'snap_gap')
        box.operator('vecxy.snap_to_active', icon='SNAP_ON')


@bpy.app.handlers.persistent
def cancel_placements(_dummy):
    for operator in list(_ACTIVE):
        operator.finish(bpy.context, cancel=True)


@bpy.app.handlers.persistent
def remove_abandoned_previews(_dummy):
    for obj in list(bpy.data.objects):
        if obj.get('_vecxy_layout_preview'):
            bpy.data.objects.remove(obj, do_unlink=True)


def cleanup_when_ready():
    # addon_utils.enable runs register() inside RestrictBlend: bpy.data.objects
    # is unavailable there, even when a file is already open. Wait for normal
    # event-loop access instead of leaving the addon only partially registered.
    if not hasattr(bpy.data, 'objects'):
        return .1
    remove_abandoned_previews(None)
    return None


CLASSES = (VECXY_LayoutSettings, VECXY_UL_shared_library, VECXY_OT_library_from_selection,
           VECXY_OT_add_shared_at_cursor, VECXY_OT_place_shared, VECXY_OT_duplicate_grid,
           VECXY_OT_snap_to_active, VECXY_PT_level_design)


def register():
    for cls in CLASSES:
        bpy.utils.register_class(cls)
    bpy.types.Scene.vecxy_layout = PointerProperty(type=VECXY_LayoutSettings)
    bpy.app.handlers.save_pre.append(cancel_placements)
    bpy.app.handlers.load_pre.append(cancel_placements)
    bpy.app.handlers.load_post.append(remove_abandoned_previews)
    if not bpy.app.timers.is_registered(cleanup_when_ready):
        bpy.app.timers.register(cleanup_when_ready, first_interval=.1)


def unregister():
    if bpy.app.timers.is_registered(cleanup_when_ready):
        bpy.app.timers.unregister(cleanup_when_ready)
    cancel_placements(None)
    for handlers, callback in ((bpy.app.handlers.save_pre, cancel_placements),
                               (bpy.app.handlers.load_pre, cancel_placements),
                               (bpy.app.handlers.load_post, remove_abandoned_previews)):
        if callback in handlers:
            handlers.remove(callback)
    del bpy.types.Scene.vecxy_layout
    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)
