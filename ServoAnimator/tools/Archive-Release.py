import argparse
import hashlib
import subprocess
import sys
import zipfile
from pathlib import Path

p = argparse.ArgumentParser()
p.add_argument('package')
p.add_argument('zip')
args = p.parse_args()
package, target = Path(args.package).resolve(), Path(args.zip).resolve()
if target.exists(): raise SystemExit('Archive already exists; it will not be overwritten.')
subprocess.run([sys.executable, str(Path(__file__).with_name('Verify-Release.py')), str(package)], check=True)
files = sorted(file for file in package.rglob('*') if file.is_file())
with zipfile.ZipFile(target, 'x', zipfile.ZIP_DEFLATED, compresslevel=6, allowZip64=True) as archive:
    for file in files:
        archive.write(file, package.name + '/' + file.relative_to(package).as_posix())
with zipfile.ZipFile(target) as archive:
    assert len(archive.infolist()) == len(files)
    assert archive.testzip() is None, 'ZIP integrity verification failed.'
digest = hashlib.sha256()
with target.open('rb') as file:
    for chunk in iter(lambda: file.read(1024 * 1024), b''): digest.update(chunk)
target.with_suffix(target.suffix + '.sha256').write_text(digest.hexdigest() + '  ' + target.name + '\n', encoding='ascii')
print(f'PASS: {len(files)} ZIP entries verified; {target.stat().st_size / 1024**2:.1f} MiB; SHA256 {digest.hexdigest()}')
print(target)
