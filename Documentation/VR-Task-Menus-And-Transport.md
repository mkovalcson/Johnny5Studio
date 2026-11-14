# VR task menus and compact transport — 2026-11-08

## Navigation
Right A opens the existing VR menu. Home now has six task pages: Library, Playback, Pose, Follow / Record, Editor, Settings. Back and Close are fixed below the page; they remain visible as long lists scroll and can be targeted by ray/trigger or selected with stick navigation. B retains Back. At Home, Back closes the menu. Pages remember their last content selection and scroll position during the current Player session.

Library contains Movies and Sequences. Selecting library playback while editing switches to standalone mode, pausing but preserving unsaved editor work. Playback has playback actions, effects, details and transport access. Pose keeps control labels, Insert and Discard. Follow / Record retains calibration, following, recording/overdub and feedback. Recording controls and capture-group selection now have separate pages. Editor contains workspace visibility, Bring Here, layout lock, Undo/Redo and application switching/closing. Settings contains audio confirmations, transport visibility, Bring Here and Reset VR position. Input-backend and scene setup remain available in desktop settings.

## Compact transport
The small world-anchored panel defaults visible, below and to the left of the initial view. It shows current sequence time (editor cursor in live editing, sequence position in standalone), Play/Pause, Stop and Record body. Hold Trigger on its top strip to move it; release to place. X hides it. Settings > Compact transport toggles it; Bring compact transport here shows and repositions it. Visibility saves in Player Settings/viewer.json as ShowTransport. Placement is session-only.

Play/Pause and Stop route to standalone playback or the existing dedicated-editor command bridge. Record body starts body capture using the currently armed groups if calibration is ready. If calibration is missing, it opens Follow setup; if a draft exists, it opens take review. Overdub and group selection remain on Follow / Record. The panel shows recording or draft status. Saving a sequence remains in the editor.

While Following or Recording, the transport is read-only and indicates Right B to stop. Controller rays stay hidden and A/B mappings remain unchanged. Opening the main menu pauses/stops capture using the existing behavior. Panel hover/grab consumes pointer input before editor workspace/robot posing and locomotion. Rendering is limited to 10 texture refreshes/second; controller transforms remain per-frame. Recoverable overlay failures hide/retry the panel without ending VR.

## Source
Johnny5VR/VrMenu.cs: fixed footer hit tests, navigation and page memory.
Johnny5VR/ViewerWindow.VrTasks.cs: task-page builders and transport callbacks.
Johnny5VR/ViewerWindow.cs, .Editor.cs, .BodyMotion.cs, .Effects.cs, .Feedback.cs: existing actions routed through task pages.
Johnny5VR/VrTransportPanel.cs and VrSession.cs: movable panel, input routing and lifecycle.
Johnny5VR/ViewerSettings.cs: transport visibility preference.
Johnny5VR/VrTaskMenuChecks.cs: footer, page memory, pointer selection, once-per-trigger and Follow isolation checks.
No shared protocol or Desktop Editor source changes were required.

## Validation
3936 Player checks PASS. 720 desktop layout assertions PASS at simulated 100%, 125%, 150%, 200%, 250%, 300% scaling and three menu widths. Live editor smoke PASS: 30 scene updates / 29 WPF image frames, pose and recording start/sample/stop/discard traversed real pipes using isolated Config; viewer preferences restored. Home, scrolling library, idle transport and recording transport bitmaps inspected for unclipped labels and visible footer controls. Headset placement, comfort and controller usability require gaming-PC validation.

## Install
Releases/VR/0.9.0/J5Studio-VR-Task-Menus-Transport-update.zip
Requires the November 8 cumulative 0.9.0 base. Includes the subsequent Controller Feedback, Follow Controller Modes and Point Gesture changes. Close VR Player and VR Editor, extract into the folder containing Johnny5VR.exe and keep the Editor subfolder. Config, Settings, models and calibration are preserved. Regular Desktop Editor is untouched.
