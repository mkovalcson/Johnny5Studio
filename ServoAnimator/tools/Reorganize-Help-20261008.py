"""One-time migration of operator documentation to task guides and current behavior."""
from studio_paths import ARCHIVE_ROOT, DOCUMENTATION_ROOT
from pathlib import Path
import json
import re

root = Path(__file__).resolve().parents[1]
help_dir = root / 'ServoAnimator' / 'Help'

def read(path):
    return path.read_text(encoding='utf-8-sig')

def write(path, text):
    path.write_text(text.rstrip() + '\n', encoding='utf-8')

def replace(file, old, new):
    path = help_dir / file
    text = read(path)
    if old not in text:
        raise ValueError(f'Missing source text in {file}: {old[:70]}')
    write(path, text.replace(old, new))

replace('Commands.md', 'The hold applies to URDF playback and sampled hardware exports.',
        'The hold applies to URDF playback, live hardware playback and sampled hardware exports.')
replace('Commands.md', 'skipped; Neck Nod/Tilt share one preceding point.',
        'skipped. Neck Nod and Neck Tilt each have their own preceding point.')
replace('Commands.md', 'Config > URDF Model and Framing > Save Position inserts all five current values at the timeline cursor as one undoable edit; duplicate commands use the usual conflict chooser.',
        '**Config > URDF Scene Setup > Save Position to Timeline** writes the current placement and camera channels at the cursor as one undoable edit. This includes the five channels above plus camera orbit yaw/pitch and applicable 3D stage channels. Saving again at that timestamp updates those channels; it does not open the duplicate-command chooser.')
replace('Controllers.md', 'Stop meeting disables controller input; re-enable it for ordinary editing.',
        'Stop meeting restores the controller enable preferences from before the meeting. Return held inputs to neutral before normal editing. Physical Live Drive stays off.')
replace('Controllers.md', 'show an error in the status bar.', 'show an error in the robot-status area beside the Movie controls.')
replace('Controllers.md', 'Config > X-Box Controller Mapping', 'Config > Xbox Controller Mapping')
replace('Controllers.md', 'Steam callouts are 25% narrower than Xbox callouts. Face buttons and the D-pad\nhave outer columns, with gyro and accelerometer mappings below in those columns.',
        'Hover mapping callouts to read long labels. Gyro and accelerometer mappings appear below the button mappings.')
replace('MeetingMode.md', 'The selected controller must be connected and enabled.',
        'The selected controller must be connected. Meeting enables it for the session and restores its previous enable preference on Stop.')
replace('MeetingMode.md', 'Clean view resets the camera, keeps orbit and zoom available, and temporarily disables Collision Warning;',
        'Clean view keeps orbit and zoom available and temporarily disables Collision Warning;')
replace('UrdfBody.md', 'through Config > URDF Model and Framing',
        'through Config > URDF Scene Setup > Robot Model, then press Load Robot')
replace('UrdfConfiguration.md', 'Use a **Load Backdrop** timeline command to change backgrounds during a sequence.',
        'Use a **Load Scene** timeline command and **Choose Scene…** to load a saved complete scene during a sequence. Older embedded Load Backdrop commands remain readable.')
replace('UrdfBackdrops.md', 'Save Scene persists the complete configuration.', 'Save Complete Scene persists the complete configuration.')
replace('UrdfBackdrops.md', '- **Robot framing** tab: optionally include explicit framing in the preset. Camera Zoom is a percentage; camera yaw/pitch are radians; Spin/Rotate are degrees.',
        '- **Placement & Camera** tab: adjust robot framing and camera orbit. Its yaw/pitch entries are degrees; API framing objects store yaw/pitch in radians. Camera zoom is a percentage and Spin/Rotate use degrees.')
replace('UrdfBackdrops.md', 'Hidden groups do not contribute.', 'Hidden groups continue contributing to movement; Hide Group only changes their display.')
replace('SpeedCalibration.md', 'Config > Speed Calibration', 'Config > Servo Speed Calibration')
replace('ServoConfiguration.md', 'Config > Speed Calibration', 'Config > Servo Speed Calibration')
replace('SplineEditor.md', "A command for a single piston (`NeckNodUp:NeckTiltLeft`, for example) sets that piston directly; the next nod or tilt continues from the pose that pair represents.",
        'A command for a single piston (`NeckNodUp:NeckTiltLeft`, for example) sets that piston directly. In timeline playback, a child command newer than both neck parents overrides its piston until the next nod or tilt parent command. That parent retakes the pair using the authored nod and tilt values. Live face/controller input re-derives nod and tilt from direct piston changes before subsequent live parent input.')

for filename in ('EditorApiExtensions.md', 'AnimationEditor_API_For_AI.md'):
    replace(filename, 'fresh play begins at zero, paused play\nresumes. To play from a selected time, pause, seek, then play.',
            'fresh play starts at the current cursor and paused play\nresumes there. Use seek, then play, to start at a selected time; seek to zero to play from the beginning.')
replace('EditorApi.md', 'From `X:\\Johnny5\\Software`:', 'From the solution folder `X:\\Johnny5\\Software\\ServoAnimator`:')
replace('EditorApi.md', 'updated 2026-09-28.', 'updated 2026-10-08.')
replace('AnimationEditor_API_For_AI.md', 'Updated: 2026-09-28. Source: ServoAnimatorFaceTracking implementation.',
        'Updated: 2026-10-08. Source: Animation Editor implementation in the ServoAnimator workspace.')
replace('AnimationEditor_API_For_AI.md', 'X:\\Johnny5\\Software\\ServoAnimatorFaceTracking', 'X:\\Johnny5\\Software\\ServoAnimator')
replace('EditorApiExtensions.md', '`../StreamDeckXL` relative to the application\nsource project.',
        '`X:\\Johnny5\\Software\\StreamDeckXL` in this workspace (a sibling of the solution folder).')

# Update the tutorial source before splitting it into workflow guides.
replace('StepByStep.md', '5. Play with URDF Drive enabled, then Save Sequence.',
        '5. Move the cursor back to 0, play with URDF Drive enabled, then Save Sequence. Sequence playback starts from the cursor.')
replace('StepByStep.md', '2. Apply and play the sequence. The control interpolates between its points.',
        '2. Apply, move the cursor to the first point, and play the sequence. The control interpolates between its points.')
replace('StepByStep.md', 'Expected result: timing changes without editing every timestamp. Hidden groups remain saved and recoverable.',
        'Expected result: timing changes without editing every timestamp. Hidden commands remain in the sequence and still play; the temporary visibility choice is not saved.')
replace('StepByStep.md', 'Turn it on during the performance when the controller should own those selected controls.',
        'Hold that button during the performance when the controller should own those selected controls; release it to return control to the webcam.')
replace('StepByStep.md', '1. Open URDF Model and Framing and choose the full-body model folder/file.\n2. Save to load it, then adjust horizontal position, vertical position and zoom. Save again to retain the framing.',
        '1. Open Config > URDF Scene Setup > Robot Model, choose the full-body URDF file, and press Load Robot. Choosing a file alone does not load it.\n2. Adjust horizontal position, vertical position and zoom on Placement & Camera. Use Save Complete Scene to retain the setup.')
replace('StepByStep.md', '5. Use Save Position in Model and Framing if the placement itself should become timeline commands.',
        '5. Use Save Position to Timeline if the placement itself should become timeline commands. Save Sequence separately to keep those commands.')
replace('StepByStep.md', '1. Add Load Backdrop and use Build… to choose 3D URDF and the scenery file.\n2. Choose Robot Centric or Room Centric. Check floor placement and enable Preserve Robot Framing if desired.',
        '1. In Config > URDF Scene Setup > Background, choose 3D URDF, choose the scenery file, and check placement on Placement & Camera. Save Complete Scene.\n2. In Edit Commands add Load Scene, use Choose Scene… to select that saved scene, and apply. Robot Centric keeps the robot framed; Room Centric uses room perspective.')
replace('StepByStep.md', '4. Add Spin 0 at the start and Spin 90 at the end;',
        '4. Add URDF Spin 0 at the start and URDF Spin 90 at the end;')
replace('StepByStep.md', 'explicit Load Backdrop commands.', 'explicit Load Scene commands.')

tutorial = read(help_dir / 'StepByStep.md')
sections = {int(m.group(1)): m.group(2).strip() for m in re.finditer(
    r'^## (\d+)\. (.*?)(?=^## \d+\.|\Z)', tutorial, re.M | re.S)}
guides = [
    ('FirstAnimation.md', 'first-animation', 'Create and edit an animation', [1,2,3,4], ['sequence','pose','spline','hide group']),
    ('CaptureGuide.md', 'capture-guide', 'Map inputs and record a performance', [5,6,7,8], ['record','webcam','controller','speech','takeover']),
    ('AudioGuide.md', 'audio-guide', 'Trim and replace speech', [9], ['audio','trim','skip','replace']),
    ('SceneGuide.md', 'scene-guide', 'Build a scene and animate the robot', [10,11,12], ['scene','load robot','TV','stage','body']),
    ('MeetingGuide.md', 'meeting-guide', 'Present live through OBS or Teams', [13], ['meeting','OBS','Teams']),
    ('MovieGuide.md', 'movie-guide', 'Build a movie and run cues', [14], ['movie','cue','loop','trigger'])]
for file, topic_id, title, numbers, keywords in guides:
    body = '\n\n'.join('## ' + sections[n] for n in numbers)
    write(help_dir / file, f'# {title}\n\nStop timeline playback completely before opening Help.\n\n{body}\n\nFor saving and export choices, see [Save, export and reuse your work](Saving.md).')
write(help_dir / 'StepByStep.md', '''# Workflow Guides

Choose the task you want to complete. Each guide gives the steps and expected result; the reference topics explain individual fields.

- [Create and edit an animation](FirstAnimation.md): commands, cursor playback, splines, groups and poses.
- [Map inputs and record a performance](CaptureGuide.md): controllers, webcam, takeover and speech recording.
- [Trim and replace speech](AudioGuide.md): source-file trim bounds and processed audio.
- [Build a scene and animate the robot](SceneGuide.md): load a model, save a scene, drive through a stage and present effects.
- [Present live through OBS or Teams](MeetingGuide.md): microphone, webcam and clean virtual output.
- [Build a movie and run cues](MovieGuide.md): sequence blocks, loops and cue playback.
- [Save, export and reuse your work](Saving.md): what each save action retains.

Sequence playback starts at the cursor. Move it to the beginning of the interval you want to preview. Stop completely before requesting F1 Help; paused playback also keeps Help unavailable.
''')

index_path = help_dir / 'HelpIndex.json'
index = json.loads(read(index_path))
topics = {t['id']: t for t in index['topics']}
topics['step-by-step'].update(title='Workflow Guides', related=[g[1] for g in guides] + ['saving'])
topics['servo-grid'].update(title='Commands pane and Pose editing', keywords=['commands pane','pose','Add','Edit','child'])
for file, topic_id, title, numbers, keywords in guides:
    topics[topic_id] = dict(id=topic_id, title=title, file=file, keywords=keywords, related=['step-by-step','saving'])
topics['saving'] = dict(id='saving', title='Save, export and reuse your work', file='Saving.md', keywords=['save','export','sequence','movie','scene','background','position'], related=['files-configuration','animation-library','urdf-framing'])
topics['editor-api-connection'] = dict(id='editor-api-connection', title='Connect to the editor API', file='EditorApi.md', keywords=['API','Codex','connection','named pipe'], related=['editor-api','editor-api-operations'])
topics['editor-api-operations'] = dict(id='editor-api-operations', title='API operations and field reference', file='EditorApiExtensions.md', keywords=['API','methods','libraryToken','transportToken','units'], related=['editor-api-connection','editor-api'])
groups = [
    ('Start here', ['getting-started','step-by-step']+[g[1] for g in guides]+['saving']),
    ('Animation and playback', ['sequence-editor','commands','servo-grid','spline-editor','command-groups','animation-library','movie-timeline','rgb-lighting']),
    ('Capture and live performance', ['controllers','webcam-face-tracking','controller-recording','recording-audio','special-audio-replace','meeting-mode','face-signals','piston-signals']),
    ('Scenes and presentation', ['urdf-framing','urdf-viewer','urdf-body','urdf-backdrops','urdf-tv','urdf-effects']),
    ('Hardware and calibration', ['live-drive-hardware','servo-configuration','speed-calibration','urdf-configuration','collision-warning']),
    ('Settings and troubleshooting', ['interface','controls-hotkeys','focus-control','files-configuration','troubleshooting']),
    ('Automation', ['editor-api-connection','editor-api-operations','editor-api'])]
ordered = []
for category, ids in groups:
    for topic_id in ids:
        topic = topics.pop(topic_id)
        topic['category'] = category
        ordered.append(topic)
if topics:
    raise ValueError(f'Uncategorized topics: {list(topics)}')
index['topics'] = ordered
write(index_path, json.dumps(index, ensure_ascii=False, indent=2))

# Archive the old mixed manual/changelog without losing historical information.
old_readme = root / 'ServoAnimator' / 'README.md'
archive = DOCUMENTATION_ROOT / 'ANIMATION_EDITOR_HISTORY_2026-10-08.md'
if archive.exists():
    raise ValueError('History archive already exists; this migration is one-time.')
write(archive, '# Archived documentation snapshot\n\nCaptured before the October 8, 2026 documentation reorganization. This file preserves historical release notes and obsolete workflows; it is not current operator guidance. Use ServoAnimator/README.md and the bundled Help instead.\n\n' + read(old_readme))

# Normalize excess empty lines left by earlier generated documentation.
for path in help_dir.glob('*.md'):
    write(path, re.sub(r'\n{3,}', '\n\n', read(path)))
print(f'Reorganized {len(ordered)} help topics into {len(groups)} sections; created {len(guides)} workflow guides and a history archive.')
