# VR pose labels and dial clipping - 2026-11-07

Implemented user feedback in Johnny5VR/RobotPoseInteraction.cs and PoseHandleOverlay.cs.

- Removed filled label rectangles and outline boxes; use shadowed text and colored dots.
- Explicit left/right head control banks based on each handle's own control key.
- All body controls, plus central controls and gangs, occupy overhead columns.
- Layout uses the highest robot control anchor as the head reference, with enough
  vertical clearance between side labels and overhead labels. It stays stable when
  the viewer turns their head, retaining the existing interaction behavior.
- Fixed-size overlay textures now use GraphicsUnit.Pixel fonts for titles, colored
  subtitles, values, range endpoints, instructions and reset labels. Previous point
  fonts grew with Windows monitor DPI and clipped their fixed texture rectangles.
- Shared pose wire and editor document/placement/movement semantics are unchanged.

Validation: Release build/self-contained publish and 412 VR runtime checks passed.
New checks verify grouping, overhead clearance, border removal and pixel-identical
label/dial rendering at 96 and 192 texture DPI. Existing selection, trigger release,
range paging, default reset, fine adjustment and overlay lifecycle checks passed.
Physical headset placement/readability still needs gaming-PC confirmation.

Artifacts:
- Johnny5VR/Diagnostics/pose-dial-preview.png (actual dial renderer).
- Johnny5VR/Diagnostics/pose-label-borderless.png (actual label renderer).
- Releases/VR/0.9.0/J5Studio-VR-Pose-controls-update.zip (Player-only application patch;
  also includes preceding UHD window layout fix, preserves Config/Settings/Editor).

X/Y are placement offsets, not locomotion speed controls. Timeline travel uses
Track Forward plus heading/spin. Authored X/Y commands can also animate placement.
VR pose changes remain previews until inserted into the timeline.
