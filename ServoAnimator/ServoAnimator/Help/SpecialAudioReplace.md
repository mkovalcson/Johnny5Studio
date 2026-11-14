# Special Audio Replace

Use this when replacing speech with a processed version whose pauses, stutters or stretches change its duration.

## Quick workflow

1. Stop playback and save the sequence.
2. Right-click the primary or additional audio handle and choose **Special Audio Replace…**.
3. Choose the processed audio. Select its Command Update JSON if animation must follow the new timing.
4. Review durations and effects; choose re-timing, face repetition and optional added motion.
5. Apply, preview the affected interval, and save the sequence. Undo restores the replacement and animation changes together.

## Audio handle menus and Special Audio Replace

Right-click either the initial green audio handle or an added audio handle. Both
provide **Audio timing / Skip to / End at…** and **Special Audio Replace…**.
Committing a timing/trim change on primary audio converts it to an ordinary Play
clip, retaining its file and position; Undo restores its primary representation.
Simply opening or canceling the dialog does not convert it.

**Special Audio Replace** selects a replacement sound file and an optional Command
Update JSON. A matching `<audio basename>.timeline.json` is selected automatically
when present beside the audio. Clear the JSON field to replace audio without
changing animation timing. For primary audio the header reference changes; for
added audio its Play command changes. Files selected outside Configuration are
copied into `Config/SpecialAudio` with unique names. Original files are not changed.

The supported update format is `johnny5-headroom-timeline`, version 1. The dialog
shows effect counts and original/output durations. Unknown versions, broken or
noncontiguous maps, and replacement durations differing by more than 20 ms are
rejected. An old-audio duration differing by more than 50 ms requires confirmation.

With **Re-time the animation**, commands before the chosen handle remain in place.
Commands from that point onward follow the map relative to the handle, including
other Play commands; commands after the old audio ends shift by the added time.
The chosen audio handle stays fixed. If the primary audio starts after a secondary
replacement, its start moves too. This applies to all following commands, not just
the recorded take. Marker positions are derived from those commands. Currently the
sequence model has no independent cue objects or duration-bearing numeric commands.

**Repeat the face during stutters** copies/evaluates the original head/eye and RGB
keys into each repeated slice, preserving native values and each repeat's duration.
Other channels hold during inserted time. Without this option all channels hold.
Stretched spans are resampled at the sequence's spline sample rate.

**Add motion** overlays deterministic, event-ID-seeded poses, head movements, eye
pops, viewport slides, body spin and chest lean. **Motion strength** is 0–200%,
default 100%. Native units are converted using the active URDF calibration, and
results are clamped. The neck's shared actuators are authored as child commands so
nod and tilt can be combined. Style pose constants are in `HeadroomTimelineEditor`.
Internal stutter cuts lead by one sample frame; exact event boundaries return to
the recording. Tape-stop reboot motion may extend 150 ms past the event.
Existing moving-image backgrounds receive seed/rotation cuts and static-image
holds during stretches; the static hold uses the image preset, not a capture of
the currently rendered moving frame. Other background types are left unchanged.

Generated motion is editable sampled keyframes, not a separate procedural layer.
Existing spline interpolation and calibrated speed limits still affect playback;
preview before using physical output. Status reports effect totals, added time,
low-confidence words, and clamping. **Ctrl+Z** restores audio references, command
values/timing, and original primary-audio placement together. Imported audio copies
remain on disk after Undo so Redo can reuse them.

Re-timing currently refuses active Skip to/End at trims on affected audio rather
than corrupting their hidden source commands. Use **Reset trims** in Audio timing to restore hidden commands and clear those bounds first, or use
audio-only replacement. Audio-only replacement preserves trims when they fit the
new file. This operation is available in the UI, not through the editor API.
