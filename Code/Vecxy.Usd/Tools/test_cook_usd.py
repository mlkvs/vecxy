"""Focused OpenUSD integration tests. Run with the Python in requirements.txt."""
import importlib.util
import json
from pathlib import Path
import shutil
import tempfile
import unittest
from pxr import Usd, UsdGeom, UsdShade, Sdf, Gf, UsdUtils

spec=importlib.util.spec_from_file_location('cooker',Path(__file__).with_name('cook_usd.py'))
cooker=importlib.util.module_from_spec(spec);spec.loader.exec_module(cooker)

class UsdCookTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.root=Path(self.temp.name)
        self.asset=self.root/'asset.usda'
        asset=Usd.Stage.CreateNew(str(self.asset));UsdGeom.SetStageMetersPerUnit(asset,1)
        root=UsdGeom.Xform.Define(asset,'/Asset');asset.SetDefaultPrim(root.GetPrim())
        mesh=UsdGeom.Mesh.Define(asset,'/Asset/Mesh');mesh.CreatePointsAttr([(0,0,0),(1,0,0),(1,1,0),(0,1,0)])
        mesh.CreateFaceVertexCountsAttr([4]);mesh.CreateFaceVertexIndicesAttr([0,1,2,3]);mesh.CreateSubdivisionSchemeAttr('none')
        asset.GetRootLayer().Save()
        self.stage=self.root/'scene.usda'
        scene=Usd.Stage.CreateNew(str(self.stage));UsdGeom.SetStageUpAxis(scene,'Y');UsdGeom.SetStageMetersPerUnit(scene,1)
        for i in range(3):
            inst=UsdGeom.Xform.Define(scene,f'/World/Instance_{i}')
            inst.GetPrim().GetReferences().AddReference('asset.usda');inst.GetPrim().SetInstanceable(True)
            inst.AddTranslateOp().Set(Gf.Vec3d(i*2,0,0))
        scene.GetRootLayer().Save()
    def tearDown(self):self.temp.cleanup()
    def cook(self,path=None):return cooker.Cooker(path or self.stage,self.root).run()
    def test_instances_share_geometry_and_invalidate(self):
        doc=self.cook();glb=cooker.Path(self.root/'Compiled/scene.glb').read_bytes()
        nodes=[p for p in doc['prims'] if p['prototype']]
        self.assertEqual(len(nodes),3)
        self.assertTrue(cooker.current(Path(str(self.stage)+'.usdscene')))
        asset=Usd.Stage.Open(str(self.asset));mesh=UsdGeom.Mesh(asset.GetPrimAtPath('/Asset/Mesh'))
        mesh.GetPointsAttr().Set([(0,0,0),(2,0,0),(2,1,0),(0,1,0)]);asset.GetRootLayer().Save()
        self.assertFalse(cooker.current(Path(str(self.stage)+'.usdscene')))
        self.cook();self.assertNotEqual(glb,(self.root/'Compiled/scene.glb').read_bytes())
        # Three nodes all use one GLB mesh after a prototype edit.
        data=(self.root/'Compiled/scene.glb').read_bytes();length=int.from_bytes(data[12:16],'little');model=json.loads(data[20:20+length])
        self.assertEqual(len(model['meshes']),1)
        self.assertEqual([n['mesh'] for n in model['nodes'] if 'mesh' in n],[0,0,0])
    def test_binary_and_usdz(self):
        scene=Usd.Stage.Open(str(self.stage));binary=self.root/'binary.usdc';scene.Export(str(binary));self.cook(binary)
        zipped=self.root/'packed.usdz';UsdUtils.CreateNewUsdzPackage(Sdf.AssetPath(str(self.stage)),str(zipped));self.cook(zipped)
    def test_axes_units_and_reset(self):
        stage=Usd.Stage.Open(str(self.stage));UsdGeom.SetStageUpAxis(stage,'Z');UsdGeom.SetStageMetersPerUnit(stage,.01);stage.GetRootLayer().Save()
        cook=cooker.Cooker(self.stage,self.root);self.assertAlmostEqual(cook.gltf['nodes'][0]['matrix'][0],.01)
        # Coordinate conversion rotates Blender height Z into engine height Y.
        m=cook.gltf['nodes'][0]['matrix'];self.assertAlmostEqual(m[9],.01)
        self.cook()
    def test_concave_ngon(self):
        triangles=cooker.triangulate([(0,0,0),(2,0,0),(2,2,0),(1,1,0),(0,2,0)])
        self.assertEqual(len(triangles),3)
    def test_invalid_topology(self):
        asset=Usd.Stage.Open(str(self.asset));mesh=UsdGeom.Mesh(asset.GetPrimAtPath('/Asset/Mesh'))
        mesh.GetFaceVertexIndicesAttr().Set([0,1,2,99]);asset.GetRootLayer().Save()
        with self.assertRaisesRegex(ValueError,'bounds'):self.cook()
    def test_animated_mesh_rejected(self):
        asset=Usd.Stage.Open(str(self.asset));mesh=UsdGeom.Mesh(asset.GetPrimAtPath('/Asset/Mesh'))
        mesh.GetPointsAttr().Set([(0,0,0),(1,0,0),(1,1,0),(0,1,0)],1);asset.GetRootLayer().Save()
        with self.assertRaisesRegex(ValueError,'animated mesh'):self.cook()
    def test_stale_render_cache(self):
        self.cook();output=self.root/'Compiled/scene.glb';output.write_bytes(b'broken')
        self.assertFalse(cooker.current(Path(str(self.stage)+'.usdscene')))
    def test_unresolved_reference_rejected(self):
        self.asset.unlink()
        with self.assertRaisesRegex(ValueError,'Unresolved USD dependencies'):self.cook()
    def test_point_instancer_rejected(self):
        stage=Usd.Stage.Open(str(self.stage));UsdGeom.PointInstancer.Define(stage,'/Scatter');stage.GetRootLayer().Save()
        with self.assertRaisesRegex(ValueError,'PointInstancer'):self.cook()

if __name__=='__main__':unittest.main()
