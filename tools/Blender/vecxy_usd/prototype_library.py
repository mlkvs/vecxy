"""Create, organize and detach Shared Prototypes."""
import bpy
from bpy.props import BoolProperty, EnumProperty, StringProperty
from mathutils import Matrix, Vector

from .level_design import library_collection, select_only, shared


_PIVOT_METHODS = [('CURSOR', '3D Cursor', ''), ('ACTIVE', 'Active object', '')]


def source_pivot(context, collection, method):
    if method == 'CURSOR':
        return context.scene.cursor.location.copy()
    if context.object and context.object.name in collection.all_objects:
        return context.object.matrix_world.translation.copy()
    raise ValueError('Выберите исходный объект внутри коллекции')


def move_collider_centers(settings, delta, inherited_keys=()):
    from . import schema

    if settings.collider != 'NONE':
        settings.collider_center = Vector(settings.collider_center) - delta
    for component in settings.components:
        if component.type_name not in schema.COLLIDERS or component.type_name == schema.MESH_COLLIDER:
            continue
        center = next((p for p in component.parameters if p.key == 'center'), None)
        if component.key in inherited_keys and (center is None or not center.is_override):
            continue
        current = Vector(center.vector) if center is not None else Vector()
        schema.set_param(component, {'key': 'center', 'kind': 'VECTOR3'}, current - delta)


def change_pivot(collection, position):
    delta = position - collection.instance_offset
    if delta.length_squared == 0:
        return
    keys = {component.key for component in collection.vecxy.components}
    for obj in bpy.data.objects:
        if obj.instance_type != 'COLLECTION' or obj.instance_collection != collection:
            continue
        # M @ T(delta) cancels the offset change for the contents of the instance.
        obj.location += obj.matrix_basis.to_3x3() @ delta
        inverse = Matrix.Translation(-delta)
        for child in obj.children:
            child.matrix_parent_inverse = inverse @ child.matrix_parent_inverse
        move_collider_centers(obj.vecxy, delta, keys)
    move_collider_centers(collection.vecxy, delta)
    collection.instance_offset = position


class VECXY_OT_set_prototype_pivot(bpy.types.Operator):
    bl_idname = 'vecxy.set_prototype_pivot'
    bl_label = 'Set Shared Prototype pivot'
    bl_description = 'Move the prototype pivot while keeping existing instances in place'
    bl_options = {'REGISTER', 'UNDO'}
    editing_source: BoolProperty(options={'SKIP_SAVE'})
    method: EnumProperty(items=_PIVOT_METHODS, default='CURSOR', options={'SKIP_SAVE'})

    def execute(self, context):
        try:
            if self.editing_source:
                collection = context.scene.vecxy_usd.editing
                if collection is None:
                    raise ValueError('Откройте исходную коллекцию прототипа')
                position = source_pivot(context, collection, self.method)
            else:
                instance = context.object
                collection = shared(instance)
                if collection is None:
                    raise ValueError('Выберите экземпляр Shared Prototype')
                position = instance.matrix_world.inverted() @ context.scene.cursor.location + collection.instance_offset
            change_pivot(collection, position)
        except (ValueError, ZeroDivisionError) as error:
            self.report({'ERROR'}, str(error))
            return {'CANCELLED'}
        context.view_layer.update()
        return {'FINISHED'}


def available_collection(collection):
    return collection and not collection.library and not collection.vecxy.is_edit_proxy and not collection.vecxy.is_prototype


def scene_parents(collection, target):
    for child in collection.children:
        if child == target:
            yield collection
        else:
            yield from scene_parents(child, target)


class VECXY_OT_prototype_from_collection(bpy.types.Operator):
    bl_idname = 'vecxy.prototype_from_collection'
    bl_label = 'Прототип из коллекции'
    bl_description = 'Создать Shared Prototype из коллекции, включая вложенные экземпляры'
    bl_options = {'REGISTER', 'UNDO'}
    collection_name: StringProperty(name='Коллекция')
    pivot: EnumProperty(name='Пивот', items=[('COLLECTION', 'Collection offset', ''), *_PIVOT_METHODS],
                        default='COLLECTION', options={'SKIP_SAVE'})

    @classmethod
    def poll(cls, context):
        return context.mode == 'OBJECT' and not context.scene.vecxy_usd.editing

    def invoke(self, context, event):
        collection = context.collection
        self.collection_name = collection.name if collection != context.scene.collection and available_collection(collection) else ''
        return context.window_manager.invoke_props_dialog(self)

    def draw(self, context):
        self.layout.prop_search(self, 'collection_name', bpy.data, 'collections')
        self.layout.prop(self, 'pivot')

    def execute(self, context):
        from . import ensure_id, register_prototype

        col = bpy.data.collections.get(self.collection_name)
        if col is None or not available_collection(col) or not col.all_objects:
            self.report({'ERROR'}, 'Выберите непустую обычную коллекцию')
            return {'CANCELLED'}
        if any(member.vecxy.is_prototype for member in col.children_recursive):
            self.report({'ERROR'}, 'Вложенные Shared Prototype должны быть экземплярами коллекций')
            return {'CANCELLED'}
        if self.pivot != 'COLLECTION':
            try:
                change_pivot(col, source_pivot(context, col, self.pivot))
            except ValueError as error:
                self.report({'ERROR'}, str(error))
                return {'CANCELLED'}
        parents = list(dict.fromkeys(scene_parents(context.scene.collection, col)))
        destinations = parents or [context.scene.collection]
        instance = bpy.data.objects.new(col.name + '_Instance', None)
        # Counteract the collection's instance offset to keep its members in place.
        instance.location = col.instance_offset
        for parent in destinations:
            parent.objects.link(instance)
        for parent in parents:
            parent.children.unlink(col)
        register_prototype(col)
        col['_vecxy_placement_scale'] = (1, 1, 1)
        instance.instance_type = 'COLLECTION'
        instance.instance_collection = col
        ensure_id(instance.vecxy)
        context.view_layer.update()
        select_only(context, [instance])
        bpy.ops.vecxy.library_from_selection()
        return {'FINISHED'}


class VECXY_OT_rename_library_prototype(bpy.types.Operator):
    bl_idname = 'vecxy.rename_library_prototype'
    bl_label = 'Переименовать'
    bl_description = 'Изменить имя в библиотеке и сохранить существующий путь USD'
    bl_options = {'REGISTER', 'UNDO'}
    name: StringProperty(name='Имя')

    @classmethod
    def poll(cls, context):
        return library_collection(context) is not None

    def invoke(self, context, event):
        self.name = library_collection(context).vecxy.asset_name
        return context.window_manager.invoke_props_dialog(self)

    def execute(self, context):
        col = library_collection(context)
        name = self.name.strip()
        if not name:
            self.report({'ERROR'}, 'Введите имя прототипа')
            return {'CANCELLED'}
        col.vecxy.asset_name = name
        context.scene.vecxy_layout.library_search = ''
        return {'FINISHED'}


class VECXY_OT_hide_library_prototype(bpy.types.Operator):
    bl_idname = 'vecxy.hide_library_prototype'
    bl_label = 'Убрать из списка'
    bl_description = 'Убрать запись из библиотеки. Вернуть можно через выбранный экземпляр'
    bl_options = {'REGISTER', 'UNDO'}

    @classmethod
    def poll(cls, context):
        return library_collection(context) is not None

    def execute(self, context):
        library_collection(context).vecxy.library_hidden = True
        context.scene.vecxy_layout.library_category = 'ALL'
        return {'FINISHED'}


def flatten_components(settings, defaults):
    from . import schema

    def snapshot(param):
        attributes = ('key', 'kind', 'choices', 'vector', 'number', 'integer', 'boolean', 'text')
        properties = {key: getattr(param, key) for key in attributes}
        properties['vector'] = tuple(properties['vector'])
        if param.kind == 'ENUM':
            properties['enum_value'] = param.enum_value
        return properties

    components = {}
    for source in (defaults, settings):
        for comp in source.components:
            _, _, parameters = components.get(comp.key, (None, None, {}))
            parameters.update({p.key: snapshot(p) for p in comp.parameters if p.is_override})
            components[comp.key] = comp.type_name, comp.enabled, parameters

    if not settings.override_role:
        settings.role = defaults.role
    if not settings.override_static:
        settings.is_static = defaults.is_static
    if not settings.override_tint and defaults.override_tint:
        settings.override_tint = True
        settings.tint = defaults.tint
    settings.components.clear()
    settings.override_components = False
    for key, (type_name, enabled, parameters) in components.items():
        comp = settings.components.add()
        comp.key, comp.type_name, comp.enabled = key, type_name, enabled
        for properties in parameters.values():
            param = comp.parameters.add()
            for name, value in properties.items():
                setattr(param, name, value)
        schema.ensure_fields(comp)


def materialize(root, destinations):
    from . import ensure_id

    col = root.instance_collection
    root.instance_type = 'NONE'
    root.instance_collection = None
    if col.vecxy.is_prototype:
        flatten_components(root.vecxy, col.vecxy)
    originals = list(col.all_objects)
    copies = {}
    for source in originals:
        obj = source.copy()
        if obj.data is not None:
            obj.data = obj.data.copy()
        obj.vecxy.identifier = ''
        ensure_id(obj.vecxy)
        for destination in destinations:
            destination.objects.link(obj)
        copies[source] = obj

    offset = Matrix.Translation(-col.instance_offset)
    for source, obj in copies.items():
        if source.parent in copies:
            obj.parent = copies[source.parent]
        else:
            obj.parent = root
            obj.parent_type = 'OBJECT'
            obj.matrix_parent_inverse = offset
            obj.matrix_basis = source.matrix_world
        for owner in (*obj.constraints, *obj.modifiers):
            for prop in owner.bl_rna.properties:
                if prop.type == 'POINTER' and not prop.is_readonly and prop.fixed_type.identifier == 'Object':
                    replacement = copies.get(getattr(owner, prop.identifier))
                    if replacement is not None:
                        setattr(owner, prop.identifier, replacement)
    for obj in copies.values():
        if obj.instance_type == 'COLLECTION' and obj.instance_collection is not None:
            materialize(obj, destinations)


class VECXY_OT_detach_prototype(bpy.types.Operator):
    bl_idname = 'vecxy.detach_prototype'
    bl_label = 'Отсоединить экземпляр'
    bl_description = 'Заменить связанный экземпляр независимыми объектами на том же месте'
    bl_options = {'REGISTER', 'UNDO'}

    @classmethod
    def poll(cls, context):
        return context.mode == 'OBJECT' and bool(shared(context.object)) and not context.scene.vecxy_usd.editing

    def execute(self, context):
        root = context.object
        materialize(root, list(root.users_collection))
        context.view_layer.update()
        return {'FINISHED'}


CLASSES = (VECXY_OT_set_prototype_pivot, VECXY_OT_prototype_from_collection, VECXY_OT_rename_library_prototype,
           VECXY_OT_hide_library_prototype, VECXY_OT_detach_prototype)


def register():
    for cls in CLASSES:
        bpy.utils.register_class(cls)


def unregister():
    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)
