# Recent-feature verification — 2026-10-05

## Builds

- Release solution: succeeded, version 1.41.9.
- Debug solution: succeeded, version 1.41.10.
- No application compilation errors. Two existing CS0649 warnings remain for `_ventArcCenter` and `_rightVentArcCenter` (reported in both WPF compilation phases).

## Focused checks

| Suite | Passed assertions |
|---|---:|
| Recent features | 466 |
| URDF presentation | 247 |
| New-feature UI | 86 |
| Controllers | 241 |
| Controller feedback | 21 |
| Controller layout | 22 |
| Scene setup and artwork (final Debug run) | 151 |
| Total | 1234 |

The new scene checks cover separate scene/background serialization, robot/framing preservation when loading scenery, complete-scene model loading, restoring the base robot/camera when switching sequences, sequence assignment persistence and undo, menu placement, embedded artwork loading, control mapping anchors and Xbox label order, all three Scene Setup tabs, and non-interactive side-positioned button tooltips.

Rendered previews in `scene-setup-previews` were inspected for Scene Setup contrast/layout and Xbox/Steam mapping artwork. Tests use isolated configuration folders and never show the hardware-connected MainWindow. A WPF owner handle was added to the test harness so it could open Scene Setup without activating main-window hardware polling.

An initial test-only compile error (double dimensions passed to an integer render helper) and the missing test owner handle were corrected. No application-source fixes were required by this run.

No physical servo output or real controller actuation was exercised. Controller detection reported no supported controller attached. This was focused recent-feature verification, not the complete historical regression suite.
