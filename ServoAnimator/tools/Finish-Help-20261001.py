from pathlib import Path
r=Path('ServoAnimator');h=r/'Help'
p=r/'MainWindow.xaml';s=p.read_text(encoding='utf-8-sig')
for label,topic in [('Speed Calibration…','speed-calibration'),('Xbox Controller Mapping…','controllers'),('Steam Controller Mapping…','controllers'),('Focus Control…','focus-control')]:
 s=s.replace(f'Header="{label}" Click=',f'Header="{label}" local:HelpSystem.Topic="{topic}" Click=')
s=s.replace('Header="URDF Model and Framing…" local:HelpSystem.Topic="urdf-viewer"','Header="URDF Model and Framing…" local:HelpSystem.Topic="urdf-framing"')
s=s.replace('x:Name="FocusControlButton" Content=', 'x:Name="FocusControlButton" local:HelpSystem.Topic="focus-control" Content=')
p.write_text(s,encoding='utf-8-sig')
p=h/'Interface.md';s=p.read_text(encoding="utf-8");s=s.replace('Commands and Movie Timeline stay visible; their former visibility toggles have been removed.', 'The active controller mapping can replace the docked Command List. When the URDF is undocked, Commands returns and the controller mapping can appear beside Hardware.')
s=s.replace('When the URDF is undocked, Commands expands across the available width. Its Dock URDF button provides a recovery path for the detached preview. Commands remains visible in both arrangements.', 'When the URDF is undocked, Commands is visible again. The controller mapping sits between it and Hardware when shown. Use Dock URDF to bring the model back.')
a=s.index('Commands are aligned');b=s.index('\nThe Hardware strip',a)
s=s[:a]+'''The Command List shows the control, Spline/Break Spline choices, Speed, and Value; the former Time column is removed. The right-aligned heading reads “N Command(s) at T sec”. Hover a row for full details. Body icons are green; URDF/TV icons are red. Modified-controls inspection offers actions for the selected control types.
'''+s[b:]
s+='''
## Hardware and status

Focus Control and Collision Safeguard sit above the Stream Deck, Xbox and Steam buttons at the bottom of Hardware. A device's connection light and its enabled button are separate states. Collision Safeguard changes color while preventing motion.

Status messages appear in the available right-hand Movie controls area. The Sequence transport includes Record, Meeting, playback speed as a percentage, and audio-relative time colored to match the most recent audio handle. Hidden groups have colored Show Group buttons on the right of the playback bar.
''';p.write_text(s,encoding="utf-8")
p=h/'Troubleshooting.md';s=p.read_text(encoding="utf-8");s+='''
## Recent features: quick checks

- **Saved vertical position changes on reopening:** check URDF Model and Framing and the selected model path. Preserve Robot Framing retains the saved placement for the configured startup stage; explicit timeline or meeting presets can still replace it.
- **Model or backdrop disappeared after moving folders:** select the file at its new path. A backdrop can live anywhere, but its mesh paths must still resolve.
- **Finger bends stop sooner than expected:** the full-body neutral hand is nearly closed. Open with negative curl values. Coupled base/tip closing stops remain active even when Collision Safeguard is off.
- **Spin stops at a mapping endpoint:** select Relative joystick mode for continuous turning. Absolute, Set and Toggle intentionally use mapped angle destinations.
- **TV stays blank:** load a backdrop containing the selected screen link, add TV Front and TV Show, and check the imported media path/Windows codec support. The show begins after camera arrival.
- **Audio replacement refuses a timing JSON:** check its format/version and duration, and Reset trims on affected audio before re-timing.
- **A controller cannot take over a webcam control:** select the takeover checkbox independently of recording selection, map Face Tracking Override, and ensure that controller is connected and enabled.
- **Background arrows affect another app:** the movie transport currently owns them. Use square Stop or disable Movie Playback in Focus Control to release them.
- **Help opens the wrong topic:** stop playback, focus the desired field/window, and press F1 again. Search by the exact command name if needed.
''';p.write_text(s,encoding="utf-8")
p=h/'ControlsHotkeys.md';s=p.read_text(encoding="utf-8");s=s.replace('- Drag a gold downward triangle: move its complete command group.','- Drag a command triangle: move its commands; persistent groups move together and use their assigned group colors.')
s+='\n## Background control\n\nFor global Movie arrows and independently permitted URDF/physical controller output, see **Focus Control and Background Input**. In Live Drive, Escape stops playback and disables servos.\n';p.write_text(s,encoding="utf-8")
p=h/'Commands.md';s=p.read_text(encoding="utf-8");s+='''
## Specialized URDF actions

Use **Build…** for Load Backdrop, Fall Apart, URDF TV Front, TV Show and URDF TV Return. Their dedicated help topics explain camera travel, media files and effect timing. These red-icon actions affect only the virtual scene. Body controls have green icons and native -100 to +100 values; their configured degree ranges are in URDF Configuration.
''';p.write_text(s,encoding="utf-8")
p=h/'UrdfViewer.md';s=p.read_text(encoding="utf-8");a=s.index('## Separate model folders and framing');s=s[:a]+'''## Model placement and backgrounds

For model files, saved vertical/horizontal position, continuous Spin, zoom and Save Position, see **URDF Model and Framing**. For scenery, travel, treads and casters, see **3D URDF Backdrops and Stage Movement**. TV media and Fall Apart each have a dedicated help topic.

Clean Undocked URDF View hides buttons, FPS and the legend and temporarily turns Collision Warning off. It does not automatically lock the camera. Model and Framing saves update the clean-view restore position; docking restores the hidden overlays.
''';p.write_text(s,encoding="utf-8")
# More related links instead of unsupported inline Markdown navigation.
import json
p=h/'HelpIndex.json';d=json.loads(p.read_text(encoding="utf-8"));by={t['id']:t for t in d['topics']}
for ident,links in {'urdf-viewer':['urdf-framing','urdf-backdrops','urdf-tv','urdf-effects'],'interface':['controllers','focus-control','command-groups'],'controls-hotkeys':['focus-control'],'troubleshooting':['urdf-framing','urdf-tv','special-audio-replace'],'meeting-mode':['focus-control','urdf-backdrops']}.items():by[ident]['related']=list(dict.fromkeys(by[ident]['related']+links))
p.write_text(json.dumps(d,indent=2)+'\n')
