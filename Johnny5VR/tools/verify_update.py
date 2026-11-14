"""Apply an update to a disposable old release and verify preservation."""
from pathlib import Path
import hashlib
import json
import sys
import zipfile

archive, folder=map(lambda s:Path(s).resolve(),sys.argv[1:3])
workspace=Path(__file__).resolve().parents[1]/'packaging'/'verification'
assert folder.is_relative_to(workspace.resolve())
assert (folder/'Johnny5VR.exe').is_file()
digest=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
assert digest(archive)==archive.with_suffix('.zip.sha256').read_text().split()[0]
preserved={p.relative_to(folder).as_posix():digest(p) for area in ('Settings','Config') for p in (folder/area).rglob('*') if p.is_file() and not any(asset in p.as_posix() for asset in ('/Meshes/Body/Treads/','/Meshes/Body/FallApart/'))}
with zipfile.ZipFile(archive) as source:
    assert source.testzip() is None
    manifest=json.loads(source.read('update-manifest.json'))
    for entry in source.infolist():
        name=entry.filename
        assert (folder/name).resolve().is_relative_to(folder)
        assert not name.startswith('Settings/')
        assert not name.startswith('Config/') or any(asset in name for asset in ('/Meshes/Body/Treads/','/Meshes/Body/FallApart/'))
    source.extractall(folder)
for item in manifest['files']:
    assert digest(folder/item['file'])==item['sha256'],item['file']
for name,expected in preserved.items():
    assert digest(folder/name)==expected,name
report={'version':manifest['version'],'baseVersion':manifest['baseVersion'],'viewer':str(folder),'preservedFiles':len(preserved),'updatedFiles':len(manifest['files']),'sha256':digest(archive)}
(workspace.parent/'last-update-verification.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report,indent=2))
