"""Compose USD with OpenUSD and cook a static scene into Vecxy's GLB render cache.

Supports .usd/.usda/.usdc/.usdz, native instance references, inherited transforms,
mesh topology, UVs/normals, material subsets, PreviewSurface, UVTexture and lights.
The game consumes the cache, never starts Python or loads native USD libraries.
Unchanged stages can be checked using standard Python without usd-core installed.
"""
import argparse
import hashlib
import io
import json
import math
from pathlib import Path
import struct
import sys
import tempfile

VERSION = 1


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def save_if_changed(path, data):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.exists() and path.read_bytes() == data:
        return
    with tempfile.NamedTemporaryFile(dir=path.parent, delete=False) as stream:
        stream.write(data)
        temporary = Path(stream.name)
    temporary.replace(path)


def current(sidecar):
    try:
        doc = json.loads(sidecar.read_text())
        base = sidecar.parent
        if doc.get('compiler') != digest(__file__):
            return False
        for item in doc['inputs'] + doc['outputs']:
            path = (base / item['path']).resolve()
            if not path.is_file() or digest(path) != item['hash']:
                return False
        return True
    except (OSError, KeyError, ValueError):
        return False


class Cooker:
    def __init__(self, source, assets):
        from pxr import Usd, UsdGeom, UsdShade, UsdLux, UsdPhysics, UsdUtils, Sdf, Gf
        self.Usd, self.Geom, self.Shade, self.Lux, self.Physics, self.Gf = Usd, UsdGeom, UsdShade, UsdLux, UsdPhysics, Gf
        self.source, self.assets = source, assets
        layers, asset_paths, unresolved = UsdUtils.ComputeAllDependencies(Sdf.AssetPath(str(source)))
        if unresolved:
            raise ValueError('Unresolved USD dependencies: ' + ', '.join(str(p) for p in unresolved))
        self.stage = Usd.Stage.Open(str(source))
        if not self.stage:
            raise ValueError(f'Could not open USD stage: {source}')
        self.time = Usd.TimeCode.Default()
        self.cache = UsdGeom.XformCache(self.time)
        self.data = bytearray()
        self.gltf = {'asset': {'version': '2.0', 'generator': 'Vecxy.Usd OpenUSD cooker'},
                     'scene': 0, 'scenes': [{'nodes': [0]}], 'nodes': [], 'meshes': [],
                     'materials': [], 'images': [], 'textures': [], 'samplers': [
                         {'magFilter': 9729, 'minFilter': 9729, 'wrapS': 10497, 'wrapT': 10497}],
                     'bufferViews': [], 'accessors': [], 'extensionsUsed': ['KHR_materials_emissive_strength']}
        # USD and System.Numerics use row vectors; glTF arrays store column-vector matrices.
        # Their sequential flattened representations are the same after transposition.
        units = UsdGeom.GetStageMetersPerUnit(self.stage)
        conversion = Gf.Matrix4d(1)
        if UsdGeom.GetStageUpAxis(self.stage) == UsdGeom.Tokens.z:
            conversion.SetRotate(Gf.Rotation(Gf.Vec3d(1, 0, 0), -90))
        conversion = Gf.Matrix4d().SetScale(units) * conversion
        self.gltf['nodes'].append({'name': 'USD coordinate conversion', 'matrix': flatten(conversion)})
        self.records, self.nodes, self.mesh_cache, self.material_cache, self.texture_cache = [], {}, {}, {}, {}
        self.dependencies = {source, Path(__file__).resolve()}
        for asset_path in asset_paths:
            dependency = Path(str(asset_path).split('[', 1)[0]).resolve()
            if dependency.is_file():
                self.dependencies.add(dependency)
        for layer in self.stage.GetUsedLayers():
            if layer.realPath:
                self.dependencies.add(Path(layer.realPath.split('[', 1)[0]).resolve())

    def view(self, payload, target=None):
        self.data.extend(b'\0' * (-len(self.data) % 4))
        entry = {'buffer': 0, 'byteOffset': len(self.data), 'byteLength': len(payload)}
        if target:
            entry['target'] = target
        self.data.extend(payload)
        self.gltf['bufferViews'].append(entry)
        return len(self.gltf['bufferViews']) - 1

    def accessor(self, values, count, kind, component=5126):
        flat = [v for row in values for v in row] if kind != 'SCALAR' else values
        fmt = 'f' if component == 5126 else 'I'
        if any(not math.isfinite(v) for v in flat):
            raise ValueError('Non-finite USD mesh data')
        view = self.view(struct.pack('<' + fmt * len(flat), *flat), 34963 if kind == 'SCALAR' else 34962)
        item = {'bufferView': view, 'componentType': component, 'count': count, 'type': kind}
        if kind == 'VEC3' and values:
            item['min'] = [min(v[i] for v in values) for i in range(3)]
            item['max'] = [max(v[i] for v in values) for i in range(3)]
        self.gltf['accessors'].append(item)
        return len(self.gltf['accessors']) - 1

    def texture(self, shader):
        value = shader.GetInput('file').Get()
        if not value:
            raise ValueError(f'Missing USD texture file at {shader.GetPath()}')
        resolved = value.resolvedPath
        if not resolved:
            raise ValueError(f'Unresolved texture {value.path} at {shader.GetPath()}')
        if resolved in self.texture_cache:
            return self.texture_cache[resolved]
        from pxr import Ar
        from PIL import Image
        # Ar resolves both ordinary textures and assets inside USDZ archives.
        resource = Ar.GetResolver().OpenAsset(Ar.ResolvedPath(resolved))
        if not resource:
            raise ValueError(f'Cannot read texture {resolved}')
        raw = bytes(resource.GetBuffer())
        stream = io.BytesIO()
        Image.open(io.BytesIO(raw)).convert('RGBA').save(stream, format='PNG')
        image = len(self.gltf['images'])
        self.gltf['images'].append({'bufferView': self.view(stream.getvalue()), 'mimeType': 'image/png'})
        index = len(self.gltf['textures'])
        self.gltf['textures'].append({'source': image, 'sampler': 0})
        self.texture_cache[resolved] = index
        if '[' not in resolved:
            self.dependencies.add(Path(resolved).resolve())
        return index

    def material(self, prim):
        material, _ = self.Shade.MaterialBindingAPI(prim).ComputeBoundMaterial()
        if not material:
            key = 'default'
            descriptor = {'name': 'USD default', 'pbrMetallicRoughness': {'baseColorFactor': [.5, .5, .5, 1], 'metallicFactor': 0, 'roughnessFactor': .7}}
        else:
            surface = material.ComputeSurfaceSource()
            shader = surface[0] if surface else None
            if not shader or shader.GetIdAttr().Get() != 'UsdPreviewSurface':
                raise ValueError(f'{material.GetPath()}: export USD Preview Surface materials from Blender')
            def value(name, default):
                item = shader.GetInput(name)
                result = item.Get() if item else None
                return result if result is not None else default
            color = [float(v) for v in value('diffuseColor', (.5, .5, .5))]
            opacity = float(value('opacity', 1))
            descriptor = {'name': str(material.GetPrim().GetName()), 'doubleSided': True,
                          'pbrMetallicRoughness': {'baseColorFactor': color + [opacity],
                             'metallicFactor': float(value('metallic', 0)), 'roughnessFactor': float(value('roughness', .5))}}
            if opacity < .999:
                descriptor['alphaMode'] = 'BLEND'
            emissive = [max(0, float(v)) for v in value('emissiveColor', (0, 0, 0))]
            strength = max(1, max(emissive))
            descriptor['emissiveFactor'] = [v / strength for v in emissive]
            if strength > 1:
                descriptor['extensions'] = {'KHR_materials_emissive_strength': {'emissiveStrength': strength}}
            diffuse = shader.GetInput('diffuseColor')
            if diffuse and diffuse.HasConnectedSource():
                connected, _, _ = diffuse.GetConnectedSource()
                texture_shader = self.Shade.Shader(connected.GetPrim())
                if texture_shader.GetIdAttr().Get() != 'UsdUVTexture':
                    raise ValueError(f'{shader.GetPath()}: only UsdUVTexture color connections are supported')
                descriptor['pbrMetallicRoughness']['baseColorTexture'] = {'index': self.texture(texture_shader)}
            # Scalar texture networks cannot be represented faithfully yet; never silently drop them.
            for name in ('metallic', 'roughness', 'normal', 'opacity', 'emissiveColor'):
                item = shader.GetInput(name)
                if item and item.HasConnectedSource():
                    raise ValueError(f'{shader.GetPath()}: connected {name} is unsupported; bake this input or use a constant')
            key = json.dumps(descriptor, sort_keys=True)
        if key not in self.material_cache:
            self.material_cache[key] = len(self.gltf['materials'])
            self.gltf['materials'].append(descriptor)
        return self.material_cache[key]

    def mesh(self, prim):
        mesh = self.Geom.Mesh(prim)
        if mesh.GetPointsAttr().GetNumTimeSamples() or mesh.GetFaceVertexIndicesAttr().GetNumTimeSamples():
            raise ValueError(f'{prim.GetPath()}: animated mesh data is unsupported in static USD scenes')
        points = mesh.GetPointsAttr().Get(self.time)
        counts = mesh.GetFaceVertexCountsAttr().Get(self.time)
        indices = mesh.GetFaceVertexIndicesAttr().Get(self.time)
        if points is None or counts is None or indices is None or sum(counts) != len(indices):
            raise ValueError(f'{prim.GetPath()}: invalid mesh topology')
        if any(i < 0 or i >= len(points) for i in indices):
            raise ValueError(f'{prim.GetPath()}: mesh index out of bounds')
        holes = set(mesh.GetHoleIndicesAttr().Get() or [])
        normals = mesh.GetNormalsAttr().Get(self.time)
        normal_interpolation = mesh.GetNormalsInterpolation()
        normal_primvar = self.Geom.PrimvarsAPI(prim).GetPrimvar('normals')
        if normal_primvar and normal_primvar.HasValue():
            normals = normal_primvar.ComputeFlattened(self.time)
            normal_interpolation = normal_primvar.GetInterpolation()
        uv = self.Geom.PrimvarsAPI(prim).GetPrimvar('st')
        if not uv or not uv.HasValue():
            uv = next((v for v in self.Geom.PrimvarsAPI(prim).GetPrimvars() if str(v.GetTypeName()) in ('texCoord2f[]', 'float2[]') and v.HasValue()), None)
        uvs = uv.ComputeFlattened(self.time) if uv else None
        uv_interpolation = uv.GetInterpolation() if uv else 'constant'
        def sample(data, interpolation, face, corner, point):
            if not data:
                return None
            index = {'faceVarying': corner, 'vertex': point, 'varying': point, 'uniform': face, 'constant': 0}.get(str(interpolation))
            if index is None or index >= len(data):
                raise ValueError(f'{prim.GetPath()}: invalid {interpolation} attribute')
            return data[index]
        material = self.material(prim)
        face_materials = [material] * len(counts)
        for subset in self.Geom.Subset.GetGeomSubsets(mesh, self.Geom.Tokens.face, 'materialBind'):
            subset_material = self.material(subset.GetPrim())
            for face in subset.GetIndicesAttr().Get() or []:
                if face < 0 or face >= len(counts):
                    raise ValueError(f'{subset.GetPath()}: face subset index out of bounds')
                face_materials[face] = subset_material
        batches = {}
        cursor = 0
        left = mesh.GetOrientationAttr().Get() == self.Geom.Tokens.leftHanded
        for face, count in enumerate(counts):
            corners = list(range(cursor, cursor + count)); cursor += count
            if face in holes or count < 3:
                continue
            # Ear clipping supports concave Blender ngons, without assuming convex polygons.
            polygon = [points[indices[c]] for c in corners]
            for triangle in triangulate(polygon):
                tri = [corners[v] for v in triangle]
                if left:
                    tri.reverse()
                a, b, c = [points[indices[v]] for v in tri]
                flat = self.Gf.Cross(b - a, c - a).GetNormalized()
                if flat.GetLength() < 1e-10:
                    continue
                batch = batches.setdefault(face_materials[face], [[], [], [], []])
                for corner in tri:
                    point = indices[corner]
                    position = tuple(float(v) for v in points[point])
                    normal = sample(normals, normal_interpolation, face, corner, point) or flat
                    normal = self.Gf.Vec3f(normal).GetNormalized()
                    texcoord = sample(uvs, uv_interpolation, face, corner, point)
                    texcoord = (float(texcoord[0]), 1 - float(texcoord[1])) if texcoord is not None else (0, 0)
                    batch[0].append(position); batch[1].append(tuple(normal)); batch[2].append(texcoord)
                    batch[3].append(len(batch[3]))
        if not batches:
            return None
        signature = hashlib.sha256(repr(batches).encode()).hexdigest()
        if signature in self.mesh_cache:
            return self.mesh_cache[signature]
        primitives = []
        for mat, (p, n, uv, ix) in batches.items():
            primitives.append({'attributes': {'POSITION': self.accessor(p, len(p), 'VEC3'),
                               'NORMAL': self.accessor(n, len(n), 'VEC3'), 'TEXCOORD_0': self.accessor(uv, len(uv), 'VEC2')},
                               'indices': self.accessor(ix, len(ix), 'SCALAR', 5125), 'material': mat})
        index = len(self.gltf['meshes'])
        self.gltf['meshes'].append({'name': str(prim.GetName()), 'primitives': primitives})
        self.mesh_cache[signature] = index
        return index

    def run(self):
        Usd, Geom = self.Usd, self.Geom
        for prim in Usd.PrimRange(self.stage.GetPseudoRoot(), Usd.TraverseInstanceProxies()):
            if prim.IsPseudoRoot() or not prim.IsActive() or prim.IsAbstract() or prim.IsInPrototype() or any(p.IsAbstract() for p in ancestors(prim)):
                continue
            if prim.IsA(Geom.PointInstancer):
                raise ValueError(f'{prim.GetPath()}: PointInstancer is unsupported; export scene instances/references')
            if prim.IsA(self.Shade.Material) or prim.IsA(self.Shade.Shader) or prim.IsA(Geom.Subset):
                continue
            xf = Geom.Xformable(prim)
            if xf and xf.TransformMightBeTimeVarying():
                raise ValueError(f'{prim.GetPath()}: time-sampled transforms require an animation importer')
            imageable = Geom.Imageable(prim)
            collider_api = prim.HasAPI(self.Physics.CollisionAPI)
            role = prim.GetAttribute('vecxy:role').Get() or ''
            collider = prim.GetAttribute('vecxy:collider').Get() == 'box' or collider_api and prim.IsA(Geom.Cube)
            if imageable and imageable.ComputeVisibility() == Geom.Tokens.invisible:
                continue
            if imageable and imageable.ComputePurpose() == Geom.Tokens.guide and not collider and not role:
                continue
            parent_path = prim.GetParent().GetPath()
            parent_node = self.nodes.get(str(parent_path), 0)
            world = self.cache.GetLocalToWorldTransform(prim)
            # Reparent after filtering while preserving resetXformStack and arbitrary transforms.
            ancestor = prim.GetParent()
            while ancestor and not ancestor.IsPseudoRoot() and str(ancestor.GetPath()) not in self.nodes:
                ancestor = ancestor.GetParent()
            parent_world = self.cache.GetLocalToWorldTransform(ancestor) if ancestor and not ancestor.IsPseudoRoot() else self.Gf.Matrix4d(1)
            parent_node = self.nodes.get(str(ancestor.GetPath()), 0) if ancestor else 0
            node = {'name': str(prim.GetName()), 'matrix': flatten(world * parent_world.GetInverse())}
            if prim.IsA(Geom.Mesh) and not collider and not any(p.GetAttribute('vecxy:collider').Get() == 'box' for p in ancestors(prim)):
                mesh = self.mesh(prim)
                if mesh is not None:
                    node['mesh'] = mesh
            elif prim.IsA(Geom.Cube) and not collider:
                raise ValueError(f'{prim.GetPath()}: visual Cube requires conversion to mesh')
            elif prim.GetTypeName() not in ('', 'Xform', 'Scope', 'Material', 'Shader', 'GeomSubset', 'Cube', 'Mesh', 'SphereLight', 'RectLight', 'DiskLight', 'DistantLight'):
                raise ValueError(f'{prim.GetPath()}: unsupported USD schema {prim.GetTypeName()} (convert geometry to mesh)')
            index = len(self.gltf['nodes']); self.gltf['nodes'].append(node)
            self.gltf['nodes'][parent_node].setdefault('children', []).append(index)
            self.nodes[str(prim.GetPath())] = index
            prototype = str(prim.GetPrototype().GetPath()) if prim.IsInstance() else ''
            record = {'path': str(prim.GetPath()), 'node': index, 'role': str(role), 'prototype': prototype}
            if collider:
                size = prim.GetAttribute('vecxy:colliderSize').Get()
                if size is None and prim.IsA(Geom.Cube):
                    size = [Geom.Cube(prim).GetSizeAttr().Get()] * 3
                if size is None:
                    raise ValueError(f'{prim.GetPath()}: box collider requires vecxy:colliderSize')
                record['colliderSize'] = [float(v) for v in size]
            if prim.HasAPI(self.Lux.LightAPI):
                light = self.Lux.LightAPI(prim)
                record['light'] = 'directional' if prim.IsA(self.Lux.DistantLight) else 'point'
                record['color'] = list(light.GetColorAttr().Get())
                record['intensity'] = float(light.GetIntensityAttr().Get()) * 2 ** float(light.GetExposureAttr().Get())
                record['range'] = float(prim.GetAttribute('vecxy:range').Get() or 0)
            self.records.append(record)
        stem = self.source.stem
        model_path = self.source.parent / 'Compiled' / (stem + '.glb')
        self.data.extend(b'\0' * (-len(self.data) % 4))
        self.gltf['buffers'] = [{'byteLength': len(self.data)}]
        js = json.dumps(self.gltf, separators=(',', ':'), allow_nan=False).encode()
        js += b' ' * (-len(js) % 4)
        payload = struct.pack('<III', 0x46546C67, 2, 28 + len(js) + len(self.data)) + struct.pack('<II', len(js), 0x4E4F534A) + js + struct.pack('<II', len(self.data), 0x004E4942) + self.data
        save_if_changed(model_path, payload)
        sidecar = Path(str(self.source) + '.usdscene')
        import os
        def file_record(path):
            return {'path': Path(os.path.relpath(path, sidecar.parent)).as_posix(), 'hash': digest(path)}
        document = {'format': VERSION, 'source': self.source.relative_to(self.assets).as_posix(),
                    'model': model_path.relative_to(self.assets).as_posix(), 'prims': self.records,
                    'compiler': digest(__file__), 'inputs': [file_record(p) for p in sorted(self.dependencies)],
                    'outputs': [file_record(model_path)]}
        save_if_changed(sidecar, (json.dumps(document, indent=2, allow_nan=False) + '\n').encode())
        print(f'USD: {len(self.records)} prims, {len(self.gltf["meshes"])} shared meshes -> {model_path.name}')
        return document


def ancestors(prim):
    parent = prim.GetParent()
    while parent and not parent.IsPseudoRoot():
        yield parent
        parent = parent.GetParent()


def flatten(matrix):
    return [float(matrix[row][column]) for row in range(4) for column in range(4)]


def triangulate(points):
    """Ear clip a simple planar polygon; fail for malformed/self-intersecting faces."""
    if len(points) == 3:
        return [(0, 1, 2)]
    normal = [sum((p[(i + 1) % 3] - q[(i + 1) % 3]) * (p[(i + 2) % 3] + q[(i + 2) % 3])
                  for p, q in zip(points, points[1:] + points[:1])) for i in range(3)]
    drop = max(range(3), key=lambda i: abs(normal[i]))
    axes = [i for i in range(3) if i != drop]
    xy = [(p[axes[0]], p[axes[1]]) for p in points]
    cross = lambda a, b, c: (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0])
    area = sum(a[0] * b[1] - b[0] * a[1] for a, b in zip(xy, xy[1:] + xy[:1]))
    direction = 1 if area >= 0 else -1
    remaining, triangles = list(range(len(points))), []
    while len(remaining) > 3:
        for j, b in enumerate(remaining):
            a, c = remaining[j - 1], remaining[(j + 1) % len(remaining)]
            if cross(xy[a], xy[b], xy[c]) * direction <= 1e-12:
                continue
            if any(all(v >= -1e-12 for v in (cross(xy[a], xy[b], xy[p]) * direction,
                                            cross(xy[b], xy[c], xy[p]) * direction,
                                            cross(xy[c], xy[a], xy[p]) * direction))
                   for p in remaining if p not in (a, b, c)):
                continue
            triangles.append((a, b, c)); remaining.pop(j)
            break
        else:
            raise ValueError('USD mesh contains a degenerate or non-simple polygon; triangulate it in Blender')
    triangles.append(tuple(remaining))
    return triangles


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('stage', type=Path)
    parser.add_argument('--assets', type=Path, required=True)
    parser.add_argument('--force', action='store_true')
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    stage, assets = args.stage.resolve(), args.assets.resolve()
    stage.relative_to(assets)
    sidecar = Path(str(stage) + '.usdscene')
    if not args.force and current(sidecar):
        print(f'USD cache up to date: {stage.name}')
        return
    if args.check:
        raise ValueError(f'USD cache is stale: {stage}. Re-cook with OpenUSD installed.')
    try:
        Cooker(stage, assets).run()
    except ModuleNotFoundError as error:
        raise RuntimeError('USD cooking needs usd-core and Pillow: pip install -r Vecxy.Usd/Tools/requirements.txt. Set VecxyUsdPython to that Python executable.') from error


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        print(f'USD import failed: {error}', file=sys.stderr)
        sys.exit(1)
