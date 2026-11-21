"""Draw the same clipped decal projection onto evaluated Blender surfaces."""
import math

import bpy
from mathutils import Matrix, Vector

from . import decals, schema

_handle = None
_shader = None
_uniforms = None
_meshes = {}
_images = {}
_pending_images = set()


def projection_shader():
    import gpu

    global _shader
    if _shader is not None:
        return _shader
    interface = gpu.types.GPUStageInterfaceInfo('vecxyDecalVaryings')
    interface.smooth('VEC3', 'worldPosition')
    info = gpu.types.GPUShaderCreateInfo()
    info.typedef_source("""
        struct DecalUniforms {
            mat4 viewProjection;
            mat4 model;
            mat4 worldToDecal;
            vec4 normalAndFacing;
            vec4 settings;
        };
    """)
    info.uniform_buf(0, 'DecalUniforms', 'projector')
    info.sampler(0, 'FLOAT_2D', 'DecalImage')
    info.vertex_in(0, 'VEC3', 'pos')
    info.vertex_out(interface)
    info.fragment_out(0, 'VEC4', 'FragColor')
    info.vertex_source("""
        void main() {
            worldPosition = (projector.model * vec4(pos, 1.0)).xyz;
            gl_Position = projector.viewProjection * vec4(worldPosition, 1.0);
            gl_Position.z -= 0.00002 * gl_Position.w;
        }
    """)
    info.fragment_source("""
        void main() {
            vec3 normal = normalize(cross(dFdx(worldPosition), dFdy(worldPosition)));
            vec3 projected = (projector.worldToDecal * vec4(worldPosition, 1.0)).xyz;
            if (dot(normal, projector.normalAndFacing.xyz) < projector.normalAndFacing.w ||
                any(greaterThan(abs(projected), vec3(0.5))))
                discard;
            vec4 color = texture(DecalImage, clamp(projected.xy + vec2(0.5), vec2(0.001), vec2(0.999)));
            FragColor = vec4(color.rgb, color.a * projector.settings.x);
        }
    """)
    _shader = gpu.shader.create_from_info(info)
    return _shader


def bind_projection(shader, view_projection, model, inverse, normal_and_facing, opacity):
    import gpu

    global _uniforms
    values = [value for matrix in (view_projection, model, inverse)
              for column in matrix.transposed() for value in column]
    values.extend((*normal_and_facing, opacity, 0, 0, 0))
    buffer = gpu.types.Buffer('FLOAT', len(values), values)
    if _uniforms is None:
        _uniforms = gpu.types.GPUUniformBuf(buffer)
    else:
        _uniforms.update(buffer)
    shader.uniform_block('projector', _uniforms)


def load_pending_images():
    from . import redraw_viewports

    for path in _pending_images:
        try:
            _images[path] = bpy.data.images.load(str(path), check_existing=True)
        except RuntimeError:
            _images[path] = None
    _pending_images.clear()
    redraw_viewports(bpy.context)


def preview_image(path):
    if path not in _images:
        _pending_images.add(path)
        if not bpy.app.timers.is_registered(load_pending_images):
            bpy.app.timers.register(load_pending_images)
        return None
    return _images[path]


def projector_data(context, obj, matrix):
    if obj.get('_vecxy_decal_preview') and (decals._ACTIVE is None or not decals._ACTIVE.valid):
        return
    from . import instance_collection

    collection = instance_collection(obj)
    for component, data in schema.effective_components(obj.vecxy, collection.vecxy if collection else None):
        if not component.enabled or component.type_name != decals.TYPE:
            continue
        value = data.get('size', {'x': 1, 'y': 1, 'z': .2})
        size = [value[axis] for axis in 'xyz']
        if any(not math.isfinite(v) or v <= 0 for v in size):
            continue
        box = matrix @ Matrix.Diagonal(Vector((*size, 1)))
        if abs(box.to_3x3().determinant()) < 1e-12:
            continue
        yield obj, box, data


def gather(context):
    visible = set(context.visible_objects)
    receivers, projectors = [], []
    for instance in context.evaluated_depsgraph_get().object_instances:
        evaluated = instance.object
        obj = evaluated.original
        owner = instance.parent.original if instance.is_instance and instance.parent else obj
        if owner not in visible or not instance.show_self:
            continue
        matrix = instance.matrix_world.copy()
        if obj.type == 'MESH':
            # Blender's DepsgraphObjectInstance iterator invalidates evaluated object wrappers
            # when advancing. Resolve the original against the graph again before drawing.
            receivers.append((obj, matrix))
        if obj.vecxy.components or obj.type == 'EMPTY':
            try:
                projectors.extend(projector_data(context, obj, matrix))
            except RuntimeError:
                continue  # Invalid field values are reported by the component exporter.
    return receivers, sorted(projectors, key=lambda item: item[2].get('order', 0))


def overlaps(evaluated, mesh_to_decal):
    vertices = [mesh_to_decal @ Vector(corner) for corner in evaluated.bound_box]
    return all(min(p[i] for p in vertices) <= .5 and max(p[i] for p in vertices) >= -.5 for i in range(3))


def receiver_batch(evaluated, shader):
    from gpu_extras.batch import batch_for_shader

    key = evaluated.as_pointer()
    if key not in _meshes:
        mesh = evaluated.data
        mesh.calc_loop_triangles()
        _meshes[key] = batch_for_shader(shader, 'TRIS',
            {'pos': [vertex.co for vertex in mesh.vertices]},
            indices=[tuple(triangle.vertices) for triangle in mesh.loop_triangles]) if mesh.loop_triangles else None
    return _meshes[key]


def draw():
    import gpu
    from gpu_extras.batch import batch_for_shader
    from .level_design import box_points

    context = bpy.context
    if not context.space_data.overlay.show_overlays or context.mode not in {'OBJECT', 'EDIT_MESH'}:
        return
    receivers, projectors = gather(context)
    if not projectors:
        return

    gl = gpu.state
    depth, blend, mask = gl.depth_test_get(), gl.blend_get(), gl.depth_mask_get()
    gl.depth_test_set('LESS_EQUAL')
    gl.depth_mask_set(False)
    gl.blend_set('ALPHA')
    try:
        if context.scene.vecxy_decals.preview:
            shader = projection_shader()
            shader.bind()
            view_projection = gpu.matrix.get_projection_matrix() @ gpu.matrix.get_model_view_matrix()
            depsgraph = context.evaluated_depsgraph_get()
            for _, box, data in projectors:
                try:
                    path = decals.asset_file(context, data.get('texture', ''))
                except ValueError:
                    continue
                image = preview_image(path) if path else None
                if image is None:
                    continue
                inverse = box.inverted()
                normal = inverse.to_3x3()[2].normalized()
                facing = math.cos(math.radians(data.get('maxSurfaceAngle', 75)))
                shader.uniform_sampler('DecalImage', gpu.texture.from_image(image))
                for obj, matrix in receivers:
                    evaluated = obj.evaluated_get(depsgraph)
                    if not overlaps(evaluated, inverse @ matrix):
                        continue
                    batch = receiver_batch(evaluated, shader)
                    if batch:
                        bind_projection(shader, view_projection, matrix, inverse, (*normal, facing), data.get('opacity', 1))
                        batch.draw(shader)

        points = []
        selected = set(context.selected_objects)
        for obj, box, _ in projectors:
            if obj not in selected and not obj.get('_vecxy_decal_preview'):
                continue
            corners = [box @ corner for corner in box_points(Vector((-.5,)*3), Vector((.5,)*3))]
            points.extend(point for i in range(8) for bit in (1, 2, 4) if i < i ^ bit
                          for point in (corners[i], corners[i ^ bit]))
        if points:
            gl.depth_test_set('NONE')
            shape = gpu.shader.from_builtin('UNIFORM_COLOR')
            shape.bind()
            shape.uniform_float('color', (1, .65, .19, .95))
            batch_for_shader(shape, 'LINES', {'pos': points}).draw(shape)
    finally:
        gl.depth_test_set(depth)
        gl.blend_set(blend)
        gl.depth_mask_set(mask)


@bpy.app.handlers.persistent
def invalidate_geometry(_, depsgraph):
    if any(update.is_updated_geometry for update in depsgraph.updates):
        _meshes.clear()


@bpy.app.handlers.persistent
def clear(_):
    _meshes.clear()
    _images.clear()
    _pending_images.clear()


def register():
    global _handle
    if not bpy.app.background:
        _handle = bpy.types.SpaceView3D.draw_handler_add(draw, (), 'WINDOW', 'POST_VIEW')
        bpy.app.handlers.depsgraph_update_post.append(invalidate_geometry)
        bpy.app.handlers.load_pre.append(clear)
        bpy.app.handlers.undo_pre.append(clear)
        bpy.app.handlers.redo_pre.append(clear)


def unregister():
    global _handle, _shader, _uniforms
    if _handle is not None:
        bpy.types.SpaceView3D.draw_handler_remove(_handle, 'WINDOW')
        _handle = None
        bpy.app.handlers.depsgraph_update_post.remove(invalidate_geometry)
        bpy.app.handlers.load_pre.remove(clear)
        bpy.app.handlers.undo_pre.remove(clear)
        bpy.app.handlers.redo_pre.remove(clear)
    if bpy.app.timers.is_registered(load_pending_images):
        bpy.app.timers.unregister(load_pending_images)
    clear(None)
    _shader = None
    _uniforms = None
