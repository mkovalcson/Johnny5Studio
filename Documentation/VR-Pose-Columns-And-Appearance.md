# VR pose columns and appearance controls

The dial and upper-left gang bank are lowered to a small gap above the side
labels. Gangs are ordered by their anchors' actual heights on the robot.
Values precede icons in every label. Label text uses the existing side-bank size;
columns shift slightly right and keep enough clearance for two center columns.

Center rows, left | right:
Microphone Up/Down | MFR Rotate
Nose Basket | MFR Up/Down
Nose Body | Whip Antenna Up/Down
Neck Turn | Whip Antenna Rotate
Neck Lift | Laser Lower
Chest Tilt | Robot X
Waist Swivel | Robot Y
Waist Lean | Robot Turn

The persistent dial retains selection. Purple left/right hand gangs stay beside
individual hand controls. All 81 actual model controls remain present.

Radio groups above the dial apply appearance and accessory choices to the current
VR editor session. Appearance: Normal, Light wear, Medium wear, Outdoor weathered,
Gold. Accessory: None, Laser, Toolbox. Medium wear maps to the existing Workshop
level, Outdoor weathered to the existing Outdoor aged level. Gold selects the
existing Gold Johnny finish with weathering cleared. Selected states come back
from the editor; loading status is shown during appearance preparation. No files
are automatically saved by the selectors. Timeline accessory commands retain
their existing behavior.

The scene protocol is version 3 for appearance/accessory/busy state. Update BOTH
Player and bundled Editor from this ZIP; mixing them with older builds is not
supported. New pose actions use the same context token, unique request IDs,
modal/playback guards and duplicate-request protection as numeric posing.

Validation: Player build/publish clean; Editor build/publish passed with existing
unused-field warnings. 3,784 runtime/layout checks passed, including exact center
row order, gang physical ordering, label overlap, lowered clearance and all eight
radio hit targets. 94 Editor integration assertions passed, including applying
and reporting all accessories and Gold/Normal finishes. Real Player/Editor pipe
smoke test passed with 30 scene frames, desktop frames, preview/discard and Stop.
Physical headset testing remains on the gaming system.

Preview: Johnny5VR/Diagnostics/vr-pose-radio-layout.png. The actual model, layout,
icons, labels, dial and radio drawing code produce a single-eye view with no
editor/desktop chrome. Shows Normal finish and Toolbox accessory. It is a layout
preview rather than a captured headset frame. No controls are omitted.
