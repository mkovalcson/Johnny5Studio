# TipToe (B_TipToe)

**TipToe** is a URDF-only full-body control. Use it in a body timeline command, Full Body Pose, URDF Configuration or a controller mapping. In the VR Editor, the pose handle is at the left track's front hub. Saved sequences also play TipToe in the standalone VR Player. The loaded full-body model must include the updated caster linkage.

- **0 or negative:** tracks flat.
- **50:** about 30.6° of track rotation with the default range.
- **100:** about 61.3° of track rotation relative to the body with the default range.

Both tracks rotate about the pelvis drum, front down and rear up. The front rollers stay planted, the caster four-bar swings down, and the robot tips back slightly until the caster wheel touches the floor. At full TipToe the body rises about 20 cm and shifts forward about 29 cm. That shift is part of the pose; it does not change your Robot X, Y or heading commands.

Under **URDF Configuration → Body**, the default degree range is 0–61.27°. A saved stock 45° maximum from the first release is upgraded; a deliberate custom maximum is retained. The model clamps motion to its supported range. For a gradual rise, key TipToe at 0 and 100 and enable its spline. Key a return to 0 to lower the tracks again.

TipToe does not drive physical hardware and is not driven by VR Follow.
