"""Create an installable Blender addon archive, without bundling Python caches."""
import argparse
from pathlib import Path
import zipfile

root=Path(__file__).resolve().parent
repo=next((p for p in root.parents if (p/'.agents').exists()),root.parents[1])
parser=argparse.ArgumentParser();parser.add_argument('--output',type=Path,default=repo/'.agents/local/vecxy_usd.zip')
args=parser.parse_args();args.output.parent.mkdir(parents=True,exist_ok=True)
with zipfile.ZipFile(args.output,'w',zipfile.ZIP_DEFLATED) as archive:
    for path in sorted((root/'vecxy_usd').glob('*.py')):archive.write(path,'vecxy_usd/'+path.name)
print(args.output)
