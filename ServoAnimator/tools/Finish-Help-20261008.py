"""Remove retired UI descriptions and validate the reorganized help inventory."""
from pathlib import Path
import json
import re

root = Path(__file__).resolve().parents[1]
folder = root / 'ServoAnimator' / 'Help'
def update(file, old, new):
    path = folder / file
    text = path.read_text(encoding='utf-8-sig')
    if old not in text:
        raise ValueError(f'Missing expected text in {file}: {old[:60]}')
    path.write_text(text.replace(old, new), encoding='utf-8')

update('Troubleshooting.md', 'Use the Dock button under Headtop Controls in the expanded two-column Servo Grid.',
       'Use **Dock URDF** in the Commands header, or **Config > Reset Screen Layout** if the header is inaccessible.')
update('Troubleshooting.md', '- Controller does not drive after a meeting: Stop meeting disables controllers deliberately; enable the hardware-panel button again.',
       '- Controller does not drive after a meeting: Stop meeting restores its previous enable preference. Return inputs to neutral, then check the controller button, Focus Control permissions and URDF Drive. Physical Live Drive remains off until deliberately enabled.')
update('Troubleshooting.md', 'check URDF Model and Framing and the selected model path.',
       'check URDF Scene Setup, the selected robot file and the assigned sequence scene.')
update('UrdfViewer.md', 'The URDF viewer renders the calibrated Johnny 5 head and neck and follows the logical values produced by command editing, the timeline and playback.',
       'The URDF viewer renders the selected robot model, including supported full-body joints, and follows command editing, timeline playback and live input.')
update('UrdfViewer.md', 'Recenter stretches so the right camera arrow lines up with the right edge of UnDock.\nThe NeckTurn circle is offset 50 pixels left from its previous position near the legend.\n\n', '')
update('UrdfViewer.md', 'The neck base is screen-anchored 35 pixels above the bottom of the URDF viewport while zooming or resizing.',
       'Framing and floor placement depend on the selected model and scene; adjust them in URDF Scene Setup.')
update('UrdfViewer.md', 'moving Nod or Tilt transfers ownership of the shared neck actuators to that mode.',
       'Nod and Tilt combine into the two neck actuators. Each has its own value; moving one retains the other.')
update('UrdfViewer.md', 'Resetting NeckNod or NeckTilt also makes that mode the active shared-neck owner.',
       'Resetting NeckNod or NeckTilt resets that component while retaining the other.')
update('UrdfViewer.md', '- Eye Pop sliders are offset 50 pixels left from their projected side positions, in both Joined and Split modes. Their reset buttons follow the shifted sliders.\n', '')
update('UrdfViewer.md', 'For model files, saved vertical/horizontal position, continuous Spin, zoom and Save Position, see **URDF Model and Framing**.',
       'For model files, backgrounds, placement, continuous Spin, zoom and Save Position to Timeline, see [URDF Scene Setup](UrdfFraming.md).')
update('UrdfViewer.md', 'Model and Framing saves update the clean-view restore position;',
       'Saved scene settings determine the retained framing;')
update('UrdfViewer.md', 'Controller polling and hardware dispatch are\nnot throttled by the visual update queue.',
       'Controller polling and its hardware dispatch are\nnot throttled by the visual update queue. Live timeline spline targets are generated at the timeline pose refresh rate, approximately 30 updates per second, and pending samples coalesce if hardware writes are slower. The exported spline sample frequency is a separate setting.')
update('LiveDriveHardware.md', 'Status indicators for Maestro, Arduino and the two Tic controllers appear at the top right.',
       'Connection indicators for Maestro, Arduino and the two Tic controllers appear in Hardware. The robot-status button beside the Movie controls explains permitted outputs and blocking reasons.')
update('CommandGroups.md', 'save the visibility state to the sequence file.',
       'save the visibility state to the sequence file. This differs from commands archived by audio Skip to/End at trims: those are retained with the audio handle and do not play until restored. See [Recording Audio](RecordingAudio.md).')

index = json.loads((folder / 'HelpIndex.json').read_text(encoding='utf-8'))
ids = [topic['id'] for topic in index['topics']]
assert len(ids) == len(set(ids)), 'Duplicate topic IDs'
indexed = {topic['file'] for topic in index['topics']}
for topic in index['topics']:
    assert (folder / topic['file']).is_file(), topic['file']
    assert topic.get('category'), topic['id']
    assert all(related in ids for related in topic.get('related', [])), topic['id']
    text = (folder / topic['file']).read_text(encoding='utf-8')
    for target in re.findall(r'\]\(([^)]+)\)', text):
        if re.match(r'^[a-z]+://', target):
            continue
        basename = Path(target.split('#')[0]).name
        assert (folder / basename).is_file(), f'{topic["file"]}: {target}'
        assert basename in indexed, f'Unindexed linked help page: {basename}'
print(f'Validated {len(ids)} topics, all related-topic targets and local Markdown links.')
