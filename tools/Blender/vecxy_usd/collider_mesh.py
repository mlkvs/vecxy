"""Evaluated triangle geometry shared by the collider overlay and USD export."""
from mathutils import Matrix
from .level_design import geometry_parts


def triangles(depsgraph, obj=None, collection=None):
    if collection is not None:
        offset = Matrix.Translation(-collection.instance_offset)
        parts = (part for member in collection.all_objects if not member.hide_render
                 for part in geometry_parts(member, offset @ member.matrix_world))
    elif obj.instance_type == 'COLLECTION' or obj.type == 'MESH':
        parts = geometry_parts(obj, Matrix.Identity(4))
    else:
        inverse = obj.matrix_world.inverted_safe()
        parts = (part for child in obj.children_recursive if not child.hide_render
                 for part in geometry_parts(child, inverse @ child.matrix_world))
    vertices, indices = [], []
    for source, transform in parts:
        if source.mode == 'EDIT':
            source.update_from_editmode()
        evaluated = source.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh()
        if mesh is None:
            continue
        try:
            mesh.calc_loop_triangles()
            offset = len(vertices)
            mirrored = transform.to_3x3().determinant() < 0
            vertices.extend(transform @ vertex.co for vertex in mesh.vertices)
            for triangle in mesh.loop_triangles:
                a, b, c = triangle.vertices
                if mirrored:
                    b, c = c, b
                if (vertices[offset + b] - vertices[offset + a]).cross(
                    vertices[offset + c] - vertices[offset + a]
                ).length_squared > 0:
                    indices.extend((offset + a, offset + b, offset + c))
        finally:
            evaluated.to_mesh_clear()
    return vertices, indices
