# Verification — 2026-09-28

Release build: **1.36.5**, ServoAnimatorFaceTracking solution.

The solution and updated checks compile successfully. Two existing CS0649 warnings remain for RobotHeadView._ventArcCenter and _rightVentArcCenter.

## Passing checks

| Suite | Assertions |
|---|---:|
| General regression | 6,596 |
| Recent features: Expo, Proportional Offset, Headroom retiming and motion, forward direction, caster swivel | 466 |
| Body controls, audio trim, voice conversion | 139 |
| New-feature UI, including paired body cards and rotary/numeric range synchronization | 86 |
| Controllers | 241 |
| Persistent command groups | 21 |
| URDF presentation | 247 |
| Current full-body model loading and framing | 5 |
| Editor API | 90 |
| Recording audio | 7 |
| Controller recording | 49 |
| Controller mapping files | 6 |
| Meeting | 18 |
| **Total** | **7,971** |

Additional binary STL checks passed for chest_yoke_hood.stl (80 triangles) and chest_yoke_hood_gasket.stl (32 triangles): correct file size, nondegenerate triangles, closed manifold, consistent winding/normals, and positive enclosed volume.

Updated the stale body UI test to inspect the new paired grid instead of the former direct children. Numeric display checks allow the UI's six-decimal rounding. Updated model loading checks to use this solution's current FullBodyModel rather than the older ServoAnimator model.

## Limits

No physical robot was driven. Controller hardware interaction and live microphone/webcam behavior were not manually verified. The external Stream Deck Node integration was skipped because its plugin sources are absent from this solution; local Editor API checks passed. Geometry checks verify mesh integrity, not a visual comparison with the reference photograph. No deployment was performed.
