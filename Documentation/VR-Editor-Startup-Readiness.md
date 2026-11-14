# VR Editor startup readiness — 2026-11-09

The Player now waits for a complete live robot scene uploaded to its renderer, nonempty control metadata, completed appearance preparation, preloaded control graphics and the first usable editor image before attaching the interactive VR Editor panel. Normal rendering continues during this asynchronous wait. The status area identifies loading the scene, preparing appearance, preloading graphics or waiting for the first editor screen. Repeated requests share the same startup task. Pose and Follow entry points wait for readiness too.

Preloading decodes the current control icons, warms the actual label/font drawing paths and renders the initial Neck Turn dial. That dial raster is reused when its control/range/value match. This reduces first-use preparation; it does not precompute every weathering level or every future dial value. There is a three-minute startup timeout, with an explanatory error if the editor closes or cannot finish.

When launched in VR mode, the VR Editor skips the Restore autosaved work prompt and reopens its last saved document. Existing recovery files are left alone. Future VR recovery snapshots use VREditorRecovery.json, separate from the Desktop Editor's EditorRecovery.json. Skipping recovery does not silently restore unsaved edits. Save documents normally before closing.

The regular Desktop Editor is unchanged. Config, Settings, models and saved calibrations are not included in the update.

Validation for this change: Release builds/publish only and focused source review. No full test suite or runtime integration test was run, as requested. Verify startup and controller behavior on the gaming PC.

Install: close VR Player and VR Editor, then extract J5Studio-VR-Editor-Startup-Readiness-update.zip over the existing portable VR folder, replacing files while preserving the subfolder layout. This is cumulative since Johnny5VR-0.9.0-cumulative-update-2026-11-08.zip and includes all-step calibration alignment help.
