# Documentation review and refresh — October 8, 2026

The source Help was reviewed against current menu labels, Scene Setup, command editing, playback, spline evaluation, neck mixing, Meeting lifecycle, saving/export and the local API. Changes target the maintained source documentation and the Release build; the separately deployed J5_Animator copy was not overwritten.

## Organization

The Help contents now has 44 topics in seven groups: Start here; Animation and playback; Capture and live performance; Scenes and presentation; Hardware and calibration; Settings and troubleshooting; Automation.

Workflow Guides is a short index linking six focused walkthroughs: first animation, capture, audio processing, scene creation, live presentation and movie cues. Save, export and reuse your work explains the different save actions. Existing topic IDs remain stable for F1 bindings; technical API material is grouped separately.

The application README now points to maintained guidance. Its previous mixture of release history and obsolete manual sections is preserved in ANIMATION_EDITOR_HISTORY_2026-10-08.md, clearly marked historical.

## Confirmed corrections

| Previous instruction | Current instruction or behavior |
| --- | --- |
| Model and Framing > Save loads a robot | Scene Setup > Robot Model > Load Robot loads it; selecting a path alone does not |
| Load Backdrop > Build creates scenery | Configure/save a complete scene, then Load Scene > Choose Scene; legacy embedded commands remain readable |
| Stop meeting disables controllers | Restore prior controller enable preferences; return held inputs to neutral; physical Live Drive remains off |
| Neck nod and tilt compete for ownership | They combine through NeckMix; Pose reset changes only the selected component |
| Break Spline tooltip says interpolation is unchanged | Hold the starting value until the next point and hide its connecting line |
| Neck insertion breaks one shared preceding point | Nod and tilt have independent preceding points |
| Hidden groups do not contribute to Track Forward | Their commands still contribute; hiding is visual only |
| Fresh API play starts at zero | It starts at the current cursor; seek to zero for a complete replay |
| Save adds spline samples with Default speed | Save Sequence retains control points; Export adds samples, using N/C for generated positions |
| Save Position writes only five channels and prompts on duplicates | Include orbit/applicable stage channels; update those channels at an existing timestamp |
| Dock recovery uses Headtop Controls in the Servo Grid | Use Dock URDF in Commands or Reset Screen Layout |
| API examples refer to ServoAnimatorFaceTracking as the workspace folder | Use the actual ServoAnimator solution folder and compiled-client location |

Clarifications distinguish group visibility from audio-trim archives, live playback cadence from exported sample frequency, scene/background saves from timeline insertion, and physical calibration from visual calibration. Tutorial F9 instructions are limited to webcam and mixed sources. The local AGENTS.md now uses the actual client/help paths and Config menu location.

## Help presentation and navigation

The native contents list displays category headings and wraps long titles. Keyword/title matches no longer require the same words in the body. Local Markdown topic links navigate even when a search filter would exclude the target. Links use the palette accent, text aligns left, and reference tables render as table cells. Repeated excess blank lines and obsolete layout-change descriptions were removed.

## Verification

- Validated all 44 indexed files, unique topic IDs, category assignments, related-topic targets and local Markdown links.
- Help-specific checks cover category grouping, keyword search, filtered-link navigation, preserved numbered workflows and reference-table rendering.
- Rendered native Help at 1040 × 720 and 760 × 500 and inspected the images.
- General editor regression checks passed after the reorganization.

No robot movement or running-editor document mutation was used for this work. Referenced external model/scenery paths remain the user's existing assets. An already-running editor uses its loaded Help catalog; restart using the updated executable to see the new organization.
