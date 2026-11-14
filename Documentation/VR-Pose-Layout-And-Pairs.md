# Separated VR pose layout, paired arms and icons - 2026-11-07

Implemented approved layout, with all current controls retained:
- Named side controls include head, body and hands; ordered by actual joint anchor
  height, captured when the editing context changes so a drag does not shuffle labels.
- Larger side labels, borderless typography and existing control-type icons.
- Two central ganged rows contained within the shoulder span.
- Central body and head controls below those rows; separate X/Y/heading placement row.
- Compact cells size their fonts to the available area without monitor-DPI scaling.

Shared/Pose/PairedBodyControls.cs defines five virtual controls: shoulder rotate,
shoulder hug, elbow, wrist pitch and wrist roll. For the stock robot, right joint axes
are the reflected axial-vector counterparts of the left axes. Matching physical
joint angles therefore produce symmetry. Mapping through each side's BodyMotionSetting
corrects command signs for Reverse and respects the intersection of calibrated ranges.

The VR Editor expands a paired preview to both existing body controls and marks both
as touched. Individual overrides remain possible. Insert emits existing individual
ServoNames commands, preserving document compatibility and one-batch Undo.
No new persistent command IDs or hardware output paths were introduced.

Shared/Controls/ControlIconFiles.cs now supplies the existing approved filename mapping
for both editors and the Player. WPF loaders stay in each editor. The Player embeds
icon PNGs, caches them, composes five bilateral icons with purple linking marks, and
draws them on both side and central labels. No loose icon installation is needed.

Validation:
- Desktop, VR Editor and VR Player builds succeeded. The Desktop version was preserved
  at its current metadata value (2.12.3); an initial attempt to force the older 2.12.2
  was rejected by its version-stamp rule and corrected to the current version.
- Shared pose checks: 5,724 assertions, including reversed/asymmetric calibrated pairs.
- VR runtime checks with complete live model catalog: 3,717 assertions.
- All 81 controls accounted for, with icons; correct side order, no overlapping targets,
  two central gang rows, placement preserved and side labels remain above the floor.
- VR Editor isolated pose checks: 31 assertions, including five paired previews,
  both-member timeline insertion, individual refinement, stale-context rejection,
  conflict cancellation, cursor anchoring and single-batch Undo.
- Prior unrelated unused vent-center field warnings remain.

Files:
- Johnny5VR/Diagnostics/pose-control-layout.tsv is the complete control inventory.
- Releases/VR/0.9.0/J5Studio-VR-Layout-Paired-Arms-Icons-update.zip updates BOTH Player
  and bundled Editor applications, preserving Config and Settings.

Mockup preference: future mockups must account for every control, setting and section.
Do not silently omit placement controls or summarize away individual finger controls.
If multiple views are needed, identify them explicitly and include a full inventory.

Live bundled Player/Editor smoke also passed: toolbar Stop, standalone switch/resume, rendered editor frames, and pose preview/discard over the real bridge; disposable Config was used and viewer settings restored.
