"""Blender export errors preserve complete details and distinguish Python dependencies."""
import os
from pathlib import Path
import sys
from types import SimpleNamespace
from unittest.mock import patch
import bpy
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
import vecxy_usd as addon
addon.register()
from vecxy_usd import exporter
bpy.context.scene.vecxy_usd.python_path=sys.executable
root=Path(os.environ['VECXY_TEST_OUTPUT']).resolve();root.mkdir(parents=True,exist_ok=True)
error='Missing exported asset: textures/missing.png\nMaterial: Test wall\nBlender node: Image Texture\n'+('Details must not be truncated. '*90)+'\nEND OF FULL REPORT'
with patch.object(exporter.subprocess,'run',return_value=SimpleNamespace(returncode=1,stderr=error,stdout='')):
    try:exporter.finalize(bpy.context,root/'native.usda',root/'out.usda',{})
    except RuntimeError as failure:
        assert str(failure)==error
        assert 'Set OpenUSD Python' not in str(failure)
    else:raise AssertionError('Expected a failed export')
with patch.object(exporter.subprocess,'run',return_value=SimpleNamespace(returncode=1,stderr="ModuleNotFoundError: No module named 'pxr'",stdout='')):
    try:exporter.finalize(bpy.context,root/'native.usda',root/'out.usda',{})
    except RuntimeError as failure:assert 'Set OpenUSD Python' in str(failure)
    else:raise AssertionError('Expected missing-dependency error')
with patch.object(exporter,'export_scene',side_effect=RuntimeError(error)):
    try:bpy.ops.vecxy.export_scene()
    except RuntimeError:pass
assert bpy.context.scene.vecxy_usd.last_export_error==error
assert error in bpy.data.texts[addon.EXPORT_REPORT_NAME].as_string()
labels=[]
class Layout:
    def label(self,**kwargs):labels.append(kwargs.get('text',''))
    def separator(self):pass
addon.VECXY_OT_show_export_report.draw(SimpleNamespace(layout=Layout()),bpy.context)
assert any('Text Editor' in label and addon.EXPORT_REPORT_NAME in label for label in labels)
with patch.object(exporter,'export_scene',return_value=root/'out.usda'):
    assert bpy.ops.vecxy.export_scene()=={'FINISHED'}
assert bpy.context.scene.vecxy_usd.last_export_error==''
assert 'END OF FULL REPORT' in bpy.data.texts[addon.EXPORT_REPORT_NAME].as_string()
print('PASS: complete diagnostics, Python dependency hint only when applicable, visible/copyable export report and clearing successful status')
addon.unregister()
