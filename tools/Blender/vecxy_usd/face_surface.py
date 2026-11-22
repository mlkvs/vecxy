"""Mesh samples and exterior edges of the polygons assigned to the face."""
from collections import Counter

from mathutils import Vector
from mathutils.kdtree import KDTree


ATTRIBUTE = '_VECXY_FACE_UV'


def perimeter(mesh, polygons):
    edges = Counter(edge for polygon in polygons for edge in polygon.edge_keys)
    open_edges = [edge for edge, count in edges.items() if count == 1]
    if not open_edges:
        return []

    # Imported heads may have split vertices at UV/material seams. Cancel
    # coincident edge pairs for the overlay without changing the actual mesh.
    vertices = {index for edge in open_edges for index in edge}
    tree = KDTree(len(vertices))
    minimum = Vector((float('inf'),) * 3)
    maximum = Vector((float('-inf'),) * 3)
    for index in vertices:
        point = mesh.vertices[index].co
        tree.insert(point, index)
        for axis in range(3):
            minimum[axis] = min(minimum[axis], point[axis])
            maximum[axis] = max(maximum[axis], point[axis])
    tree.balance()
    tolerance = max(1e-9, (maximum - minimum).length * 1e-5)
    welded = {index: min(neighbor for _, neighbor, _ in tree.find_range(mesh.vertices[index].co, tolerance))
              for index in vertices}
    groups = {}
    coincident = Counter()
    for a, b in open_edges:
        key = tuple(sorted((welded[a], welded[b])))
        if key[0] != key[1]:
            coincident[key] += 1
            groups[key] = a, b
    return [groups[key] for key, count in coincident.items() if count == 1]


def sample(context, obj, include_perimeter=False):
    evaluated = obj.evaluated_get(context.evaluated_depsgraph_get())
    mesh = evaluated.data
    attribute = mesh.attributes.get(ATTRIBUTE)
    if not attribute or len(attribute.data) != len(mesh.loops):
        return [], [], []
    uv = [tuple(item.vector) for item in attribute.data]
    included = {polygon.index for polygon in mesh.polygons if
                all(-1e-6 <= u <= 1.000001 and -1e-6 <= v <= 1.000001
                    for u, v in (uv[index] for index in polygon.loop_indices))}
    if not included:
        return [], [], []
    mesh.calc_loop_triangles()
    world = evaluated.matrix_world
    positions, coordinates = [], []
    for triangle in mesh.loop_triangles:
        if triangle.polygon_index not in included:
            continue
        positions.extend(world @ mesh.vertices[index].co for index in triangle.vertices)
        coordinates.extend(uv[index] for index in triangle.loops)
    boundary = []
    if include_perimeter:
        polygons = (mesh.polygons[index] for index in included)
        boundary = [world @ mesh.vertices[index].co for edge in perimeter(mesh, polygons) for index in edge]
    return positions, coordinates, boundary
