"""Create a small, settings-preserving update from two portable releases."""
from pathlib import Path
import hashlib
import json
import sys
import zipfile

old, new = map(Path, sys.argv[1:3])
prefix = 'Johnny5VR/'
with zipfile.ZipFile(old) as baseline, zipfile.ZipFile(new) as latest:
    before = json.loads(baseline.read(prefix+'package-manifest.json'))
    after = json.loads(latest.read(prefix+'package-manifest.json'))
    hashes = {i['file']: i['sha256'] for i in before['files']}
    files = []
    for item in after['files']:
        name = item['file']
        if name.startswith('Settings/'):
            continue
        if name.startswith('Config/') and not any(asset in name for asset in ('/Meshes/Body/Treads/','/Meshes/Body/FallApart/')):
            continue
        if hashes.get(name) != item['sha256']:
            files.append(item)
    target = new.parent / f"Johnny5VR-{after['version']}-update-from-{before['version']}.zip"
    compatible=[before['version']]
    if before['version']=='0.4.0' and after['version']=='0.5.1':compatible.append('0.5.0')
    if before['version']=='0.4.0' and after['version']=='0.6.0':compatible.extend(['0.5.0','0.5.1'])
    if before['version']=='0.4.0' and after['version']=='0.6.1':compatible.extend(['0.5.0','0.5.1','0.6.0'])
    if before['version']=='0.4.0' and after['version']=='0.6.2':compatible.extend(['0.5.0','0.5.1','0.6.0','0.6.1'])
    if before['version']=='0.4.0' and after['version']=='0.7.0':compatible.extend(['0.5.0','0.5.1','0.6.0','0.6.1','0.6.2'])
    if before['version']=='0.4.0' and after['version']=='0.8.0':compatible.extend(['0.5.0','0.5.1','0.6.0','0.6.1','0.6.2','0.7.0'])
    if before['version']=='0.4.0' and after['version']=='0.9.0':compatible.extend(['0.5.0','0.5.1','0.6.0','0.6.1','0.6.2','0.7.0','0.8.0'])
    readme = f"""Johnny 5 VR {after['version']} update (for portable {' or '.join(compatible)})

Close the VR viewer and VR editor, saving any work first.
Extract this ZIP directly into your existing Johnny5VR folder, the folder
containing Johnny5VR.exe. Allow replacement of existing application files.

This update includes the separate VR editor and the new tread animation assets.
It preserves your viewer settings, Config settings, projects and sequences.
If you selected an external Config folder, copy the included Treads folder
to the matching robot model folder there as well.

Launch Johnny5VR.exe. Choose Open / resume VR editor to start its bundled
editor automatically. The normal desktop Animation Editor is unchanged.
Either Index controller's A button opens the VR menu; B goes back.
The direct Index A-button mapping is corrected in this update.

Voice audio drives the mouth LEDs. Track motion drives belts, rollers and
rear casters. Authored RGB commands drive eye and vent illumination.
Version 0.5.1 adds curved-surface smoothing in standalone playback and
rolling caster tires/hubs in standalone and live-editor playback.
Version 0.6.0 adds a trigger-grab top bar, corner resize handles, layout
lock, recovery, sequence toolbar and pointer-based timeline navigation.
Grip/stick-click no longer move or resize the editor workspace.
Version 0.6.1 replaces raw overlay uploads with reusable GPU textures and
streams WPF-rendered editor images instead of PrintWindow capture.
Both controller rays are visible without hitting the panel; the active
right-hand pointer (left fallback) can select menu rows with the trigger.
Recoverable menu image failures retry without stopping VR.
Version 0.6.2 includes native file pickers and confirmation dialogs in
the editor panel, alongside WPF menus, dropdowns and configuration windows.
Version 0.7.0 adds labeled controller pose handles, individual and ganged
controls, robot scene placement, and Insert changed pose at cursor as one Undo.
The viewer and bundled VR editor must both be updated (scene protocol v2).
Version 0.8.0 adds laser bolts/sounds and disassemble/reassemble playback,
required baked animation assets, and A > Robot effects preview controls.
Version 0.9.0 replaces direct pose dragging with always-visible labels and a
circular range dial, default marker, fine stick adjustment and release to finish.
See START HERE.txt for the full usage guide.

update-manifest.json lists the changed files and their hashes.
"""
    with zipfile.ZipFile(target,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as output:
        for item in files:
            data=latest.read(prefix+item['file'])
            assert hashlib.sha256(data).hexdigest()==item['sha256']
            output.writestr(item['file'],data)
        output.writestr('UPDATE READ ME.txt',readme)
        output.writestr('update-manifest.json',json.dumps({'baseVersion':before['version'],'compatibleVersions':compatible,'version':after['version'],'files':files},indent=2))
digest=hashlib.sha256(target.read_bytes()).hexdigest()
target.with_suffix('.zip.sha256').write_text(digest+'  '+target.name+'\n')
print(json.dumps({'archive':str(target),'bytes':target.stat().st_size,'files':len(files),'sha256':digest},indent=2))
