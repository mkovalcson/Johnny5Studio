from pathlib import Path
import json,re
r=Path('ServoAnimator');h=r/'Help'
def read(n):return (h/n).read_text(encoding='utf-8-sig')
def write(n,s):(h/n).write_text(s.strip()+'\n',encoding='utf-8')
def section(s,title):
 a=s.index('## '+title);b=s.find('\n## ',a+3);return s[a:] if b<0 else s[a:b]
# Split detailed reference material into discoverable, focused topics.
s=read('ServoConfiguration.md');cal=section(s,'Speed Calibration');step=section(s,'Stepper Motors')
write('SpeedCalibration.md','# Speed Calibration\n\nOpen **Config > Speed Calibration**. Use **Servos** for Maestro timing and **Stepper Motors** for Eye Pop travel. These settings make the URDF follow estimated physical motion.\n\n'+cal+'\n\n'+step)
s=s.replace(cal,'## Speed Calibration\n\nOpen **Config > Speed Calibration** for predicted or measured servo timing and Eye Pop stepper travel. See **Speed Calibration** in the related topics.\n').replace(step,'');write('ServoConfiguration.md',s)
s=read('RecordingAudio.md');a=s.index('## Audio handle menus and Special Audio Replace');special=s[a:]
write('SpecialAudioReplace.md','# Special Audio Replace\n\nUse this when replacing speech with a processed version whose pauses, stutters or stretches change its duration.\n\n## Quick workflow\n\n1. Stop playback and save the sequence.\n2. Right-click the primary or additional audio handle and choose **Special Audio Replace…**.\n3. Choose the processed audio. Select its Command Update JSON if animation must follow the new timing.\n4. Review durations and effects; choose re-timing, face repetition and optional added motion.\n5. Apply, preview the affected interval, and save the sequence. Undo restores the replacement and animation changes together.\n\n'+special)
write('RecordingAudio.md',s[:a]+'## Replace processed speech\n\nBoth primary and additional audio handles offer **Special Audio Replace…**. It can replace audio alone or use a Command Update JSON to retime following animation. See **Special Audio Replace** for the workflow, format and trim restrictions.\n')
s=read('UrdfBackdrops.md');tv=section(s,'URDF TV commands');s=s.replace(tv,'## TV screen media\n\nUse **URDF TV Front**, **TV Show**, and **URDF TV Return** to present screen media. See **URDF TV and Screen Media** for a step-by-step workflow and supported formats.\n')
s=s.replace('The supplied default is `X:\\Johnny5\\Software\\ServoAnimator\\Models\\MatrixConstruct_backdrop.urdf`. Choose another URDF file to use its geometry as the room.', 'Use **Choose backdrop URDF…** to select the actual scenery file. It may be anywhere; there is no required backdrop folder. If a saved/default path points to a folder that was moved, browse to its new location.')
s=s.replace(' These source changes have not yet been compiled or runtime-tested.','')
s=s.replace('Track Forward · URDF only','Track Forward')
write('UrdfBackdrops.md',s)
write('UrdfTv.md','# URDF TV and Screen Media\n\n## Present a video or image\n\n1. Load a 3D URDF backdrop that contains a supported TV screen.\n2. Add **URDF TV Front** at the desired timeline time. Use **Build…** to choose travel time, screen fill and screen link.\n3. Add **TV Show** at that time or later. Use **Build…** to import the image/video and set fit, start offset, loop and volume.\n4. Add **URDF TV Return** when the presentation should end. Set the return time in its builder.\n5. Apply and close Edit Commands, then preview and save the sequence.\n\nExpected result: the camera faces the TV, plays the selected media, then returns to normal robot framing. These are red-icon URDF-only actions; they do not drive hardware.\n\n'+tv.replace('## URDF TV commands','## Controls and playback details').replace('"TvMedia\\clip','"TvMedia/clip'))
write('UrdfFraming.md','''# URDF Model and Framing

Open **Config > URDF Model and Framing** to choose the robot model and adjust its placement. This is separate from loading scenery through Load Backdrop.

## Choose and save a model

1. Choose a model folder, then a .urdf file if the folder contains several.
2. Adjust the framing controls while viewing the current robot.
3. Press **Save** to load the chosen model immediately and retain its framing.

Meshes resolve relative to the model file. Keep its mesh folders alongside it. Framing is stored per model pathname in Config/UrdfModelSettings.json; after moving folders, select the model at its new location.

## Position, orientation and zoom

- **Horizontal position (URDF Slide)** shifts the robot across the viewport; negative moves left.
- **Vertical position** shifts it vertically; positive moves down. The body can extend below the viewport.
- **Camera Zoom** starts at 10%. Opening this window uses the current zoom.
- **Spin** is cumulative degrees around the neck. Enter 720 for two positive turns or -720 for two negative turns. The ±360° buttons add turns; the slider provides local fine adjustment.
- **Rotate** is front-view roll in degrees, from -360 to +360; positive is clockwise.

Spin retains its turn count for treads and caster movement. Relative controller Spin continues while the stick is held, with the existing 720°/second full-stick rate, dead zone and Expo. Timeline values use signed 32-bit whole degrees, allowing roughly ±5.96 million turns.

## Save settings or add timeline commands

**Save** retains model/framing settings. **Save Position** inserts Slide, Vertical, Zoom, Spin and Rotate commands at the current timeline cursor; save the sequence to retain those commands. These buttons serve different purposes.

Unsplined position commands act immediately. Enable Spline and supply a start and destination command to animate the transition. For two positive turns over four seconds, use Spin 0 at the start and Spin 720 four seconds later. A Break Spline ends the connection to the following command.

## Framing with a 3D backdrop

With **Preserve Robot Framing** enabled, the configured startup stage retains saved horizontal/vertical position and zoom instead of restoring an older backdrop snapshot. Explicit timeline or meeting presets may include their own framing. Stage heading and robot Spin remain separate controls.

Robot Centric stage zoom changes the backdrop's scale without changing the robot's screen size. Room Centric uses perspective for the complete scene. See the stage topic for travel and camera-follow behavior.

## Clean undocked view

Clean view hides buttons, FPS and the legend and turns off Collision Warning temporarily. It does not automatically lock the camera. Explicitly saving framing while clean updates the framing that will be restored. Docking restores the ordinary overlays; a timeline backdrop or framing command can still change the view during playback.
''')
write('FocusControl.md','''# Focus Control and Background Input

Open **Config > Focus Control** or the **Focus Control** button in Hardware. These settings decide which enabled inputs can continue while another application has focus.

## Choose input and output independently

Xbox and Steam each have an enable choice plus separate **Drive URDF model** and **Drive Physical Model** choices. Stream Deck / Library API has the same destination choices. Physical output also requires Drive HW to be on. Save applies the settings.

Controller connections stay open when focus changes. The device button enables or disables control; its connected light indicates availability, not permission to move hardware. Ordinary editor dialogs pause controller output; mapping configuration provides its own Test URDF mode.

## Movie playback and global arrows

After starting a movie, enabling Movie Playback reserves plain arrow keys for the movie while another app has focus, including pauses and waits between cues:

- Up: play/pause.
- Right: next cue.
- Left: restart/previous.
- Down: stop and rewind.

The square Stop button, starting standalone sequence or Library playback, closing the movie, or disabling background Movie Playback releases these keys. Audio playback itself continues when focus changes.

## Stop output

When Live Drive is active, Escape is a global stop for playback and disables servos. Use the Hardware buttons to disable a particular controller or Stream Deck input. Meeting mode has its own Stop meeting button and always drives only the URDF.
''')
write('UrdfEffects.md','''# Fall Apart and Reassemble

**Fall Apart** is a URDF-only timeline action using the full-body model's baked collapse animation. It sends no motion to physical hardware.

## Add an effect

1. Load the compatible full-body model.
2. Place the timeline cursor and add **Fall Apart** in Edit Commands.
3. Press **Build…**, choose forward collapse or reverse reassembly, and set speed from 0.1 to 10 (1 is normal).
4. Apply and close, preview, then save the sequence.

A forward clip leaves the robot in pieces. A reversed clip starts in pieces and reassembles; normal joint control resumes when reassembly finishes. The latest Fall Apart command at or before the cursor determines the effect. Seeking evaluates the clip at the chosen time rather than requiring it to play from the beginning.

The model must include its baked effect assets. Loading an unrelated URDF does not create a collapse simulation automatically. The effect speed is separate from the Sequence playback-speed percentage.
''')
write('GettingStarted.md','''# Getting Started

Animation Editor combines editable motion commands, audio, controller/webcam capture, and a URDF preview. Physical hardware output is a separate option.

## Choose your workflow

- **Build an animation:** follow First Sequence and Smooth Motion in Step-by-Step Tutorials.
- **Puppeteer and record:** configure Xbox/Steam or Webcam Face Tracking, then use Record from Controller / Webcam.
- **Record speech:** enable Record Audio, select a microphone, then review the take before keeping it.
- **Create a full-body scene:** load the robot in URDF Model and Framing, configure a 3D backdrop, then add body and travel commands.
- **Present live:** use Meeting mode and capture the undocked view through OBS.
- **Automate edits:** enable the editor connection and use the API documentation topic.

## Three things to distinguish

A **Sequence** contains commands and audio on one timeline. A **Movie** orders Sequence files into cues. The **Library** stores reusable poses and sequences.

URDF Drive controls the preview. **Drive HW / Live Drive** controls physical output. Green Body icons and red URDF/TV icons identify virtual-only controls; they are not physical servo commands.

**Save Position** creates timeline commands. **Save** in model framing stores model/view settings. **Save Sequence** and **Save Movie** save the animation documents.

## First setup

1. Review the active Configuration folder under **Config > Set Paths**.
2. Leave physical Live Drive off while creating the first animation.
3. Open or create a sequence and leave URDF Drive enabled for preview.
4. Use **Config > Reset Screen Layout** if the timeline or panels are inaccessible.

## Find help

Stop playback completely, then press **F1** over the relevant control or inside a settings window. Help is unavailable while running or paused. Search by a task such as “TV”, “overdub”, “Skip to”, “Expo”, “Spin”, or “background arrows”. Related-topic links appear at the bottom of each page.
''')
# Keep source-specific details but remove the duplicate recording chapter and reorder controls.
s=read('Controllers.md');s='# Xbox and Steam Controller Mapping\n\nUse this page to map inputs and test movement. For recording a performance, see **Record from Controller / Webcam**. For background input permissions, see **Focus Control**.\n\n'+s[s.index('## Configure mappings'):]
s=s.replace('# Saved mapping files','## Saved mapping files')
s=re.sub(r'\nValidation covers[\s\S]*?## Live mapping display','\n## Live mapping display',s)
write('Controllers.md',s)
# Move groups into their own help target, leaving editing instructions first.
s=read('Commands.md');groups=section(s,'Persistent command groups');write('CommandGroups.md','# Command Groups and Timing\n\n'+groups+'''\n\n## Proportional Offset

Select commands, right-click, and choose **Proportional Offset**. Enter a positive multiplier: 2 doubles the spacing and 0.5 halves it. The leftmost selected command stays fixed; the remaining selected times move relative to that anchor. Use Undo to restore the original timing. Review conflicts if moved commands land on existing commands.

## Work by control type

Right-click a selection and choose **Show all modified controls**. **Delete from Selection** removes instances of that exact control in the selection. For splined controls, **Set Breaks** marks selected commands and **Clear Breaks** removes their breaks; the latter appears when relevant breaks exist. Commands outside the selection remain unchanged.
''');s=s.replace(groups,'');s=s.replace('Servo picklist','Control or Action picklist');s+='\n## Groups and proportional timing\n\nSee **Command Groups and Timing** for numbered colored groups, Hide/Show Group, Proportional Offset, and operations on selected control types.\n';write('Commands.md',s)
# Replace obsolete body range table with current paired descriptions that render cleanly in Help.
s=read('UrdfBody.md');a=s.find('| Command |');b=s.find('\n## ',a)
if a>=0:s=s[:a]+'''## Body control families

Left and Right variants are paired in picklists and configuration. Green icons identify Body controls.

- WaistLean, ChestTilt and NeckLift: torso and neck linkage.
- ShoulderPitch and ShoulderRaise: arm forward/outward motion.
- ArmTwist, Elbow, WristPitch and WristRoll: arm and hand orientation.
- FingerSpreadUpper/Lower: independent finger separation.
- FingerCurlUpper/Lower and FingerTipCurlUpper/Lower: base and distal bends.
- ThumbCurl: the opposing digit's base bend.

Use the displayed degree limits and circular range controls in URDF Configuration. Native timeline/controller values remain -100 to +100, not degrees.
'''+(s[b:] if b>=0 else '')
s=s.replace('Command Editor Servo picklist','Command Editor Control or Action picklist').replace("Default travel comes from the v32 model's limits below.", 'Default travel follows the supported body model, including the hand closing stops described below.').replace("The renderer honors your configured range rather than clamping back to the URDF limits.", "The renderer honors configured ranges except for the marked full-body model's coupled finger/thumb closing stops.")
write('UrdfBody.md',s)
# Remove obsolete build-history language from user guidance.
for p in h.glob('*.md'):
 if p.name.startswith('EditorApi') or p.name=='AnimationEditor_API_For_AI.md':continue
 s=p.read_text(encoding='utf-8-sig').replace('Servo picklist','Control or Action picklist')
 s=s.replace('These source changes have not yet been compiled or runtime-tested.','')
 p.write_text(s,encoding='utf-8')
