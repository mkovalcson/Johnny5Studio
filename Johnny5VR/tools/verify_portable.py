"""Verify a release archive and extract into a fresh directory containing spaces."""
from pathlib import Path
import datetime
import hashlib
import json
import sys
import zipfile
import xml.etree.ElementTree as ET

import argparse,shutil
parser=argparse.ArgumentParser();parser.add_argument('archive',type=Path);parser.add_argument('--keep-extracted',action='store_true');args=parser.parse_args()
archive = args.archive.resolve()
root = Path(__file__).resolve().parents[1]
digest = hashlib.sha256(archive.read_bytes()).hexdigest()
assert digest == archive.with_suffix('.zip.sha256').read_text().split()[0]
destination = root / 'packaging' / 'verification' / ('VR Playback ' + datetime.datetime.now().strftime('%Y%m%d %H%M%S'))
destination.mkdir(parents=True)
(destination / ".j5-generated.json").write_text(json.dumps({"kind":"verification"}))
with zipfile.ZipFile(archive) as source:
    assert source.testzip() is None
    for entry in source.infolist():
        target = (destination / entry.filename).resolve()
        assert target.is_relative_to(destination.resolve()), entry.filename
    source.extractall(destination)
viewer = destination / 'Johnny5VR'
manifest = json.loads((viewer / 'package-manifest.json').read_text())
for item in manifest['files']:
    file = viewer / item['file']
    assert file.stat().st_size == item['bytes'], item['file']
    assert hashlib.sha256(file.read_bytes()).hexdigest() == item['sha256'], item['file']
assert (viewer / 'coreclr.dll').is_file()
assert (viewer / 'NAudio.dll').is_file()
assert (viewer / 'Johnny5VR.portable').is_file()
assert (viewer / 'Editor' / 'AnimationEditorPlayer.exe').is_file()
assert (viewer / 'Editor' / 'coreclr.dll').is_file()
assert (viewer / 'Config' / 'SpeedCalibration.json').is_file()
for urdf in (viewer / 'Config' / 'Models').rglob('*.urdf'):
    for mesh in ET.parse(urdf).findall('.//mesh'):
        asset = (urdf.parent / mesh.attrib['filename']).resolve()
        assert asset.is_relative_to((viewer / 'Config').resolve()), str(asset)
        assert asset.is_file(), str(asset)
    animation = urdf.parent / 'Meshes' / 'Body' / 'Treads' / 'tread_animation.json'
    if 'Johnny5FullBody' in urdf.parts:
        assert animation.is_file(), 'Full robot needs its tread animation manifest'
        assert (urdf.parent / 'Meshes/Body/FallApart/fall_apart.json').is_file(), 'Full robot needs its baked disassembly clip'
    if animation.is_file():
        specification = json.loads(animation.read_text())
        for side in specification['sides'].values():
            for layer in side['layers']:
                assert len(layer['frames']) == specification['phases']
                for name in layer['frames']:
                    asset = (urdf.parent / name).resolve()
                    assert asset.is_relative_to(urdf.parent.resolve()) and asset.is_file(), str(asset)
report = {'viewer': str(viewer), 'version': manifest['version'], 'verifiedFiles': len(manifest['files']), 'sha256': digest}
(root / 'packaging' / 'last-verification.json').write_text(json.dumps(report, indent=2))
if not args.keep_extracted:
    assert destination.resolve().parent == (root / 'packaging/verification').resolve()
    shutil.rmtree(destination)
    report['temporaryExtractionRemoved'] = True
print(json.dumps(report, indent=2))
