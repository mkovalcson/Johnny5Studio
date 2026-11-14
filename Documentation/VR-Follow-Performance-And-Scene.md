# Follow scene, status and performance — 2026-11-08

Open Follow settings from the Player desktop sidebar, VR Follow / Record page, or VR Settings page. Changes apply and save immediately in Settings/viewer.json.

- Use editor's loaded URDF backdrop defaults ON. No backdrop removes scenery during Follow/Record only. Stopping Follow restores the normal editor scene. A loaded 3D URDF backdrop remains in the VR capture even if desktop presentation has hidden its stage group. The editor's saved scene is not changed.
- Show overhead Follow status defaults OFF. Enable it to restore the panel above Johnny. Calibration instructions and recording indicators remain available, along with glove LCDs and the compact transport.
- Controller feedback opens Sounds / Voice / Off and its separate volume controls.

## Performance

The live bridge now budgets a scene cycle to 33.33 ms, including request, capture, decode and consumption time. It no longer adds a fixed 16 ms after that work. Over-budget cycles continue immediately; 30 updates/second is a target, not a guarantee. SteamVR headset rendering keeps its separate refresh cadence.

Dynamic geometry retains a capacity-managed GPU buffer and CPU staging array. Transform-only updates reuse geometry; genuine mesh revisions upload into the retained buffer. Material and transform arrays are reused where their capacity permits. Diagnostics add DynamicBufferAllocations and identify Follow rows with a -follow mode suffix. This reduces avoidable allocations; scene decoding still allocates packet data.

## Validation

Player checks: 3,948 passed. Editor pose checks include backdrop on/off/restore, preserved lighting and floor alignment. A real Player/Editor pipe smoke test passed pose, body motion, recording/discard, scene rendering and surface picking. GPU checks verify transform-only dynamic frames upload once and a mesh revision reuses its GPU buffer. Desktop and VR preference screens rendered correctly. No headset is attached to this development system; actual Follow smoothness and throughput need a fresh gaming-PC timing log.

## Install

Close J5 Studio VR Player and its VR Editor. Extract this update into the existing portable VR Player folder, replacing files and preserving the Editor and Shaders subfolders. Player and Editor must be updated together (scene protocol version 6). Config, calibration, models and saved settings are not included or overwritten. This update includes the controller modes, audio feedback, task menus, transport and glove LCD changes since the 2026-11-08 cumulative update. The regular Desktop Editor is unchanged.
