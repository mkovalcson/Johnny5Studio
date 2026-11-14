"""Assemble a self-contained publish plus only the URDF rendering assets it uses."""
from pathlib import Path
import argparse
import datetime
import hashlib
import json
import os
import shutil
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[1]
import sys, subprocess
sys.path.insert(0, str(ROOT.parent / "tools"))
from maintain_artifacts import mark
parser = argparse.ArgumentParser()
parser.add_argument('--publish', required=True)
parser.add_argument('--editor-publish', required=True)
args = parser.parse_args()
publish = Path(args.publish).resolve()
assert (publish / 'Johnny5VR.exe').is_file()
assert (publish / 'coreclr.dll').is_file(), 'Use dotnet publish --self-contained true -r win-x64'
editor_publish = Path(args.editor_publish).resolve()
assert (editor_publish / 'AnimationEditorPlayer.exe').is_file()
assert (editor_publish / 'coreclr.dll').is_file(), 'The separate VR editor must also be self-contained'
def published_version(folder, app):
    deps = json.loads((folder / f'{app}.deps.json').read_text(encoding='utf-8'))
    entries = [name.split('/', 1)[1] for name in deps['libraries'] if name.startswith(f'{app}/')]
    if len(entries) != 1:
        raise SystemExit(f'Cannot determine the published version of {app} in {folder}.')
    return entries[0]


version = json.loads((ROOT.parent / 'ServoAnimator/Build/PROJECT_VERSION.json').read_text(encoding='utf-8-sig'))['version']
player_version = published_version(publish, 'Johnny5VR')
editor_version = published_version(editor_publish, 'AnimationEditorPlayer')
if player_version != version or editor_version != version:
    raise SystemExit(f'Expected Studio {version}; found VR Player {player_version} and VR Editor {editor_version}. Republish both apps.')
stamp = datetime.datetime.now().strftime('%Y%m%dT%H%M%S')
release_dir = ROOT.parent / 'Releases' / 'VR' / version
archive = release_dir / f'Johnny5VR-{version}-win-x64-portable.zip'
deployment = release_dir / 'Johnny5VR'
if archive.exists() or deployment.exists():
    raise SystemExit('This version already has a finished release; use a new version instead of overwriting it.')
release_dir.mkdir(parents=True, exist_ok=True)
stage = ROOT / 'packaging' / 'staging' / f'bundle-{version}-{stamp}'
bundle = stage / 'Johnny5VR' 
bundle.mkdir(parents=True)
mark(stage, "stage", version)
for published in (publish, editor_publish):
    if published.parent == ROOT / "packaging": mark(published, "publish", version)
for item in publish.iterdir():
    if item.name == ".j5-generated.json": continue
    if item.is_dir():
        shutil.copytree(item, bundle / item.name)
    elif item.suffix.lower() != '.pdb':
        shutil.copy2(item, bundle / item.name)
shutil.copytree(editor_publish, bundle / 'Editor', ignore=shutil.ignore_patterns('*.pdb', '.j5-generated.json'))

config = ROOT.parent / 'Config' if (ROOT.parent / 'Config').is_dir() else ROOT.parent / 'ConfigTemplate'
models = []
for name in ('Johnny5FullBody', 'Johnny5Head'):
    models.extend((config / 'Models' / 'Robots' / name).glob('*.urdf'))
models.extend((config / 'Models' / 'Scenery').rglob('*.urdf'))
copied = set()
for urdf in models:
    files = {urdf}
    for mesh in ET.parse(urdf).findall('.//mesh'):
        files.add((urdf.parent / mesh.attrib['filename']).resolve())
    files.update(urdf.parent.glob('*.json'))
    animation = urdf.parent / 'Meshes' / 'Body' / 'Treads' / 'tread_animation.json'
    if animation.is_file():
        files.add(animation)
        for side in json.loads(animation.read_text())['sides'].values():
            for layer in side['layers']:
                for frame in layer['frames']:
                    asset = (urdf.parent / frame).resolve()
                    assert asset.is_relative_to(urdf.parent.resolve()) and asset.is_file(), str(asset)
                    files.add(asset)
    clip = urdf.parent / 'Meshes' / 'Body' / 'FallApart' / 'fall_apart.json'
    if clip.is_file():
        files.add(clip)
    for file in files:
        relative = file.resolve().relative_to(config.resolve())
        target = bundle / 'Config' / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(file, target)
        copied.add(relative.as_posix())

# Playback documents and media are copied without modifying the editor library.
# Keep their Config-relative hierarchy, so movies retain sequence/audio links.
library_files = []
for directory in ('Projects', 'Library', 'LibraryScenes', 'LibraryBackgrounds', 'Backdrops', 'UrdfBackgrounds', 'VideoMedia'):
    source = config / directory
    if not source.is_dir():
        continue
    for file in source.rglob('*'):
        if not file.is_file() or file.suffix.lower() in ('.bak', '.tmp'):
            continue
        relative = file.relative_to(config)
        target = bundle / 'Config' / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(file, target)
        library_files.append(relative.as_posix())
for name in ('URDFconfig.json', 'ServoConfig.json', 'ServoConfig.default.json', 'UrdfModelSettings.json', 'SpeedCalibration.json', 'XboxControllerMapping.json', 'SteamControllerMapping.json', 'ControllerRecordingSettings.json', 'FocusControl.json'):
    source = config / name
    if source.is_file():
        shutil.copy2(source, bundle / 'Config' / name)
        library_files.append(name)

(bundle / 'Johnny5VR.portable').write_text('Portable bundle: use this folder for settings and bundled Config.\n', encoding='utf-8')
(bundle / 'Settings').mkdir()
(bundle / 'Settings' / 'viewer.json').write_text(json.dumps({
    'ConfigFolder': 'Config',
    'Robot': 'Models/Robots/Johnny5FullBody/johnny5_head.urdf',
    'Backdrop': 'Models/Scenery/MatrixConstruct/MatrixConstruct_backdrop.urdf',
    'Scale': 1, 'RobotHeightMetres': 1.8288,
    'ControllerLocomotion': True, 'MoveSpeed': 1.2, 'SnapTurnDegrees': 30,
    'UseSteamVrBindings': False,
    'UseSavedPlaybackScene': True,
    'MenuWidth': 380,
    'Distance': 2.5, 'Horizontal': 0, 'Heading': 0, 'FloorGrid': False
}, indent=2), encoding='utf-8')
(bundle / 'J5 Studio VR Player.cmd').write_text('@echo off\nstart "J5 Studio VR Player" "%~dp0Johnny5VR.exe"\n', encoding='ascii')
(bundle / 'J5 Studio VR Editor.cmd').write_text('@echo off\nstart "J5 Studio VR Editor" "%~dp0Johnny5VR.exe" --open-editor\n', encoding='ascii')
(bundle / 'Run diagnostics.cmd').write_text(
    '@echo off\nstart "Johnny 5 VR checks" /wait "%~dp0Johnny5VR.exe" --check\n'
    'if errorlevel 1 (echo Checks failed. See Diagnostics\viewer.log.) else (type "%~dp0Diagnostics\checks-result.txt")\npause\n', encoding='ascii')
shutil.copy2(ROOT / 'PORTABLE-START-HERE.txt', bundle / 'START HERE.txt')
shutil.copy2(ROOT / 'README.md', bundle / 'Viewer details.md')
(bundle / 'ThirdParty').mkdir(exist_ok=True)
shutil.copy2(ROOT / 'ThirdParty' / 'SharpDX-LICENSE.txt', bundle / 'ThirdParty' / 'SharpDX-LICENSE.txt')
cache = Path(os.environ.get('NUGET_PACKAGES', str(Path.home() / '.nuget/packages')))
shutil.copy2(cache / 'naudio' / '2.2.1' / 'license.txt', bundle / 'ThirdParty' / 'NAudio-LICENSE.txt')
# Preserve available license/copyright metadata for the editor's packaged dependencies.
deps = json.loads((editor_publish / 'AnimationEditorPlayer.deps.json').read_text())
for package, metadata in deps['libraries'].items():
    if metadata['type'] != 'package':
        continue
    folder = cache / package.lower()
    if not folder.is_dir():
        continue
    target = bundle / 'ThirdParty' / 'EditorPackages' / package.replace('/', '-')
    for file in folder.iterdir():
        if file.is_file() and (file.name.upper().startswith(('LICENSE', 'COPYING', 'NOTICE', 'THIRD-PARTY')) or file.suffix == '.nuspec'):
            target.mkdir(parents=True, exist_ok=True)
            shutil.copy2(file, target / file.name)
runtime = json.loads((publish / 'Johnny5VR.runtimeconfig.json').read_text())['runtimeOptions']
for framework in runtime['includedFrameworks']:
    package = framework['name'].lower() + '.runtime.win-x64'
    cache = Path(os.environ.get('NUGET_PACKAGES', str(Path.home() / '.nuget/packages')))
    pack = cache / package / framework['version']
    notices = [f for f in pack.iterdir() if f.name.upper().startswith(('LICENSE', 'THIRD-PARTY-NOTICES'))]
    assert notices, 'Missing Microsoft runtime license notices: ' + str(pack)
    target = bundle / 'ThirdParty' / framework['name']
    target.mkdir()
    for notice in notices:
        shutil.copy2(notice, target / notice.name)

manifest = []
for file in sorted(bundle.rglob('*')):
    if file.is_file():
        manifest.append({'file': file.relative_to(bundle).as_posix(), 'bytes': file.stat().st_size,
                         'sha256': hashlib.sha256(file.read_bytes()).hexdigest()})
(bundle / 'package-manifest.json').write_text(json.dumps({'version': version, 'files': manifest}, indent=2), encoding='utf-8')
releases = release_dir
with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as output:
    for file in sorted(bundle.rglob('*')):
        if file.is_file():
            output.write(file, Path('Johnny5VR') / file.relative_to(bundle))
digest = hashlib.sha256(archive.read_bytes()).hexdigest()
archive.with_suffix('.zip.sha256').write_text(digest + '  ' + archive.name + '\n', encoding='ascii')
bundle.rename(deployment)
assert stage.resolve().parent == (ROOT / 'packaging/staging').resolve()
shutil.rmtree(stage)
subprocess.run([sys.executable, str(ROOT.parent / 'tools/maintain_artifacts.py'), '--apply'], check=True)
print(json.dumps({'archive': str(archive), 'bundle': str(deployment), 'bytes': archive.stat().st_size,
                  'renderingAssets': len(copied), 'playbackAssets': len(library_files), 'files': len(manifest) + 1, 'sha256': digest}, indent=2))
