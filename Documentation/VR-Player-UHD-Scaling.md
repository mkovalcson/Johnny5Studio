# VR Player UHD scaling fix - 2026-11-07

The window advertised per-monitor DPI awareness but had no explicit scaling
baseline for its programmatically built controls. Resize calculations also
reintroduced unscaled pixel widths after layout. Text could scale without the
matching control space. The playback table also lacked constrained root-column
and inner-row styles, allowing buttons to extend past the allocated bounds.

Implemented in Johnny5VR/ViewerWindow.cs and Johnny5VR.csproj:
- Build controls with layout suspended, then set AutoScaleDimensions=96x96 and
  AutoScaleMode=Dpi before layout resumes.
- Explicit ApplicationHighDpiMode=PerMonitorV2 (matching the existing manifest).
- Recalculate dynamic dimensions using current monitor DPI after Load/DpiChanged.
- Preserve measured button/caption/input sizes and wrap sidebar checkbox text.
- Store MenuWidth in logical 96-DPI units. Existing property/file format retained.
- Constrain playback table columns and rows to the allocated panel.

Validation:
- Release build and self-contained Player publish succeeded without warnings.
- --layout-check: 702 real-control geometry/font assertions under simulated
  100%, 125%, 150%, 200%, 250%, 300% scaling and three sidebar widths.
- Layout images: Johnny5VR/Diagnostics/viewer-layout-96dpi.png and -192dpi.png.
- --smoke: actual D3D rendering, window/menu resizing, action/status visibility
  and screenshots passed; original viewer.json restored byte-for-byte.
- Physical UHD/monitor-switch testing remains for the gaming PC.

Delivery: Releases/VR/0.9.0/J5Studio-VR-Player-UHD-scaling-update.zip contains eight
Player application files plus instructions, preserving Config, Settings, Editor.
The development Player executable and retained publish are also updated.

Desktop review: WPF uses device-independent sizing rather than this WinForms
construction/layout path. Main window and URDF dialog sizing were inspected;
this specific missing-baseline fault does not apply. No Desktop/VR Editor source
was changed. A broader desktop high-scaling/dialog audit remains separate work.
