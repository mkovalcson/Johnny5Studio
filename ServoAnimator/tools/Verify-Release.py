import argparse
import json
import xml.etree.ElementTree as ET
from pathlib import Path

p = argparse.ArgumentParser()
p.add_argument('package')
args = p.parse_args()
package = Path(args.package).resolve()
config = package / 'Config'
files = [file for file in package.rglob('*') if file.is_file()]
assert files and not any('sylogist' in str(file).lower() for file in files)
assert not (package / 'Paths.json').exists()
assert not any('.venv' in file.parts for file in files)
references = 0

def inspect(data, key=''):
    global references
    if isinstance(data, dict):
        for name, item in data.items():
            if key.lower() == 'framing' and name.lower().endswith('.urdf'):
                assert not Path(name).is_absolute() and ':' not in name, name
                assert (config / name).is_file(), name
                references += 1
            inspect(item, name)
    elif isinstance(data, list):
        for item in data: inspect(item)
    elif isinstance(data, str):
        assert 'sylogist' not in data.lower(), data
        if key.lower() in ('modelfile', 'robotmodelfile') and data:
            assert not Path(data).is_absolute() and ':' not in data, data
            assert (config / data).is_file(), data
            references += 1

for file in config.rglob('*.json'):
    inspect(json.loads(file.read_text(encoding='utf-8-sig')))
meshes = 0
for model in package.rglob('*.urdf'):
    # Some legacy placeholder files are not loadable models and are unused.
    if model.stat().st_size < 10: continue
    tree = ET.parse(model)
    for node in tree.findall('.//mesh'):
        filename = node.get('filename')
        assert filename and ':' not in filename, (model, filename)
        assert (model.parent / filename).is_file(), (model, filename)
        meshes += 1
for required in ('AnimationEditorPlayer.exe', 'AnimationEditorPlayer.dll', 'coreclr.dll',
                 'Help/HelpIndex.json', 'Config/ServoConfig.json', 'Config/URDFconfig.json',
                 'Config/TIC/ticcmd.exe', 'FaceTracking/tracker.py', 'FaceTracking/face_landmarker.task',
                 'Config/Models/Robots/Johnny5FullBody/johnny5_head.urdf',
                 'Config/Models/Scenery/MatrixConstruct/MatrixConstruct_backdrop.urdf', 'J5 Studio Desktop.cmd'):
    assert (package / required).is_file(), required
print(f'PASS: {len(files)} packaged files, {references} relative model references, {meshes} mesh references; no Sylogist assets or workstation paths.')
