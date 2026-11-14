# VR pointing, enlarged labels and Flap terminology

Flap Tilt Up is moved one row down from its physically ordered gang position.
Side hand controls are grouped after the wrist: hand gang, thumb controls, upper
finger controls, lower finger controls. Thumb controls precede all finger controls.

Visible historical Brow control names are normalized to Flap through shared
formatting and remaining raw UI labels/tooltips/status text in both editors.
VR labels, dial titles and metadata also use Flap. Historical serialized control
identifiers and URDF joint names are preserved for file/model compatibility.

When posing, pointing at a robot surface highlights the nearest available
individual control and shows its enlarged label. Trigger selects that control
in the persistent dial; pointing alone does not change a pose or insert commands.
Several axes can share the same mechanical area, so the labels remain available
for choosing another axis. Parts without a nearby editable control do not select
an unrelated distant control. Scenery and transparent lens surfaces are excluded.

Picking uses cached mesh-local bounds, current node transforms and real triangle
intersections. Only bounds hit by the ray need triangle testing. The nearest
visible robot hit is used; this is not a screen-space approximation.

Pointing directly at a label enlarges it to 135% with a larger hit area. A normal
neighbor label has selection priority; the enlarged label stays active within
its own larger boundary and returns to normal after the pointer leaves. Enlarged
labels are drawn over the normal bank without shuffling other label positions.
They hide while dragging the dial. All 81 model controls remain available.

Validation: all three application Release builds passed; Player/Editor portable
publishes refreshed. Existing unused-field warnings remain in the editors.
3,796 runtime/layout assertions passed, including ordering, shared naming,
real-triangle picking, transformed/scaled rays, scenery exclusion, pointer hover,
trigger selection and enlarged label persistence. VR Editor integration: 94
assertions passed. Real Player/Editor pipe smoke test passed. Live scene picking
hit robot surfaces for 20/20 probes in 21.79 ms total on this development system.
Headset comfort, axis choice and real-time performance still need gaming-PC tests.

Preview: Johnny5VR/Diagnostics/vr-pose-pointing-layout.png. Generated with the
actual model and drawing code, showing the enlarged left thumb label while Neck
Turn remains selected in the dial. No desktop/editor chrome; all controls shown.

Update: Releases/VR/0.9.0/J5Studio-VR-Pointing-Flap-Controls-update.zip.
