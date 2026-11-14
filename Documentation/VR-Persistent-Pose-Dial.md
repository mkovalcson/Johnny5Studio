# Persistent pose dial and side hand gangs

Pose mode opens with the dial already visible and Neck Turn selected. Selection
is remembered for the current application session when labels/pose mode return,
after dragging, after Reset default and across compatible editor contexts. If the
selected control is unavailable, Neck Turn (or the first available control) is
used. Selection itself never inserts or changes a timeline command.

The dial stays in its fixed upper-right location on the label depth plane. Its
close button is removed because it remains available throughout posing. Labels
hide while dragging and return on release with the same control still selected.
The idle dial follows updates from the desktop editor. Pending VR previews are
kept until acknowledged, avoiding a jump back to stale editor values. An idle
dial does not block editor panel interaction or take locomotion input while the
pointer is away from the pose controls.

Open Left Hand and Open Right Hand return to their corresponding anatomical side
banks beside the individual hand controls. Their text and values are purple.
Other ganged controls retain the upper-left column; body/placement controls stay
centered above the head. All 81 model controls and existing icons are retained.

Validation: Release build/publish passed. 3,770 runtime/layout assertions passed,
including default selection, retained selection, desktop value updates, no idle
locomotion capture, hand gang bank assignments and existing layout/wire checks.
Headset interaction and readability still need testing on the gaming system.

Preview: Johnny5VR/Diagnostics/vr-persistent-dial-layout.png. Generated directly
from the model, pose layout, control icons and dial/label drawing code, without
editor chrome, desktop controls or explanatory annotations. It is a single-eye
layout preview, not a captured headset frame. All 81 labels are present. Recreate
with the Player's --pose-preview diagnostic option (requires the exported pose
catalog/bounds under Diagnostics).

Update: Releases/VR/0.9.0/J5Studio-VR-Persistent-Pose-Dial-update.zip.
