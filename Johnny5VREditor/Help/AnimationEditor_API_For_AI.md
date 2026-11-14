# J5 Animation Editor API — standalone guide for AI operators

Updated: 2026-10-08. Source: J5 Animation Editor implementation in the ServoAnimator workspace.

This file is self-contained. Give it to another AI together with access to the Windows computer running the J5 Animation Editor. Uploading this document to a web chat does not itself give that AI access to the computer, named pipe, files, or editor. It can instead prepare JSON requests for a local operator to execute.

## 1. Connection and discovery

The API is local, same-Windows-user named-pipe IPC, not HTTP. The editor must already be open with **Config > Enable Codex Connection** checked. No OpenAI API key is required. Never launch a second editor to simulate a connection.

Current workspace:
`X:\Johnny5Studio\ServoAnimator`

Compiled client:
`X:\Johnny5Studio\ServoAnimator\ServoAnimator\bin\Release\net10.0-windows\AnimationEditorPlayer.exe`

An editor may be running from Debug while the client uses Release. Inspect the running server's reported version and methods; source changes may not yet be compiled. This document describes current source, not a promise that every installed binary supports it.

PowerShell read-only discovery:

```powershell
Get-Process AnimationEditorPlayer | Select-Object Id, Path
$client = 'X:\Johnny5Studio\ServoAnimator\ServoAnimator\bin\Release\net10.0-windows\AnimationEditorPlayer.exe'
$editorProcess = 12345 # replace with the intended editor PID
$response = (& $client --editor-api --process $editorProcess | Out-String) | ConvertFrom-Json
if (-not $response.ok) { throw $response.error }
$state = $response.state
$state | Select-Object sequencePath, moviePath, cursorSeconds, playback, liveDrive
```

Omitting `--request` sends `get_state`. Omitting `--process` works only when exactly one other AnimationEditorPlayer process is found. If there are multiple editors, identify the intended sequence/window before edits. Piping to `Out-String` captures the GUI executable's console response reliably in PowerShell. Inspect JSON `ok`, not merely shell success.

### Direct pipe clients

Pipe name: `Johnny5.AnimationEditor.<PID>` (Win32 path `\\.\pipe\Johnny5.AnimationEditor.<PID>`). Open duplex, send one UTF-8 JSON object followed by newline, read one newline-terminated JSON response, then close. The server handles one connection at a time and dispatches requests on the UI thread. Request limit: 1 MiB. Built-in client connection timeout: 5 seconds; response timeout: 35 seconds. Server connection timeout: 30 seconds. A timeout is an uncertain result, not permission to repeat a mutation with a new ID.

## 2. Operating rules

1. Read `get_state` before editing. Inspect the exact sequence/movie, cursor, existing commands, ranges, children, spline state, playback, and Drive HW.
2. Use an explicit PID. Recheck the intended document whenever context changes.
3. Every ordinary mutation requires a unique `requestId` (1–128 characters) and an appropriate fresh `expectedToken`. Prefer a GUID.
4. After success, inspect the returned state for actual target, times, values, and spline settings. Read fresh state for the next edit; old receipts contain old snapshots.
5. After timeout/disconnection, retry only the identical request with the same ID. Receipt deduplication lasts for that editor process. Changing the payload with an old ID is rejected. After an editor restart inspect the timeline before retrying anything.
6. On stale-context rejection, reread and reconsider the user’s intended target. Do not blindly insert in a newly opened document or shift timing because the cursor moved.
7. Never edit project JSON behind the open editor. API mutations appear in the UI; save using the UI.
8. Pause timeline playback and turn Drive HW off before timeline/movie/background edits. Close modal dialogs and auxiliary windows; the detached URDF Model window is allowed. Recording and speed-calibration sessions block edits/playback. Direct Library performance has separate output rules below.
9. Do not assume native control values are degrees or that their sign means a physical direction. Read ranges and consult the loaded model/configuration. Parent currentValue is not a child's evaluated value.
10. Do not activate physical output unless the user's request authorizes it. URDF body/stage/framing controls are excluded from hardware output. Collision warnings are diagnostics, not a physical safety guarantee.

Token types:

| Token | Where found | Accepted uses |
|---|---|---|
| `token` | `get_state.state.token`, `get_urdf.token` | All ordinary mutations; context includes cursor, command/document/view state |
| `transportToken` | `get_state.state`, `get_status.state` | play, pause, stop, seek, set_collision_warnings; stays stable as cursor advances |
| `libraryToken` | `get_state.state`, `get_status.state` | apply_library_pose, play_library_sequence, stop_library |

The plugin presence heartbeat `stream_deck_status` is a special operation without normal edit tokens/receipts; ordinary AI clients should not impersonate a Stream Deck.

## 3. Method index

Fields below are in addition to `method`, and for ordinary mutations `requestId` / `expectedToken`.

| Method | Parameters / effect |
|---|---|
| get_state | Read complete editor context |
| get_status | Read version/date, transport/library tokens and live performance state |
| get_urdf | Read token and URDF model/framing/background/evaluated stage |
| get_collisions | Read current collision diagnostics |
| list_library | `libraryKind`: `pose` or `sequence` |
| get_library_thumbnail | `libraryKind: pose`, `name`; PNG data URLs or null |
| get_video_clips | Read the Video Clips: index, file, timeline `startSeconds`/`endSeconds`, `skipToSeconds`, `endAtSeconds`, `sourceSeconds`, fit, volume, muted, `fileFound` (also in get_state as `videoClips`) |
| get_video_frames | `times` (up to 12 timeline seconds), or `startSeconds` + `endSeconds` with optional `interval` (default 6 evenly spaced); optional `maxWidth` (480, 64–1280). Each frame: `timelineSeconds`, `clipIndex`, `file`, `videoSeconds`, JPEG data URL `image`, or `image: null` with a `note` |
| get_view_snapshot | optional `maxWidth` (800). The URDF view at the cursor as a JPEG data URL: backdrop, the Video Clip picture with its fit, then the robot (no TV/beam overlays or buttons). Needs the URDF view on screen |
| get_request | `requestId` of original operation; poll its receipt |
| insert_commands | `commands` (1–2000), optional `enableSplines`, `description` |
| insert_library | `libraryKind`, optional `name`; absent name opens picker |
| undo | Undo latest editor action; inspect `nextUndo` first |
| play / pause / stop | Timeline transport; stop also stops Library playback |
| seek | `seconds`: absolute time within current timeline |
| set_collision_warnings | `enabled`: boolean |
| apply_library_pose | optional `name`, output flags |
| play_library_sequence | optional `name`, `loop`, output flags |
| stop_library | Stop shared Library player/picker |
| movie_move | `index`, `toIndex`: zero-based final destination |
| movie_remove | `index`: detach occurrence, retain file |
| movie_insert | `sequencePath`, optional insertion `index` |
| movie_create_sequence | `name`, optional insertion `index` |
| set_backdrop | `backdrop`, optional `backgroundImageSource` |
| set_urdf_framing | complete `framing` object |
| move_robot | `turnDegrees`, `forwardMeters`, `seconds` (all optional; default 0) |
| insert_fall_apart | optional `reverse` (false), `speed` (1), `description`; inserts a Disassemble command at the cursor |
| insert_tv_front | optional `seconds` (3), `fill` (0.85), `screen`; camera glides past the robot to the backdrop TV |
| insert_tv_show | optional `mediaPath`, `fit`, `startSeconds`, `loop`, `volume` (100); image/video over the TV screen |
| insert_tv_return | optional `seconds` (3); fade out and glide back |
| insert_beam | optional `direction` (in/out), `seconds` (3.5), `particles` (true), `sound` (default/none/path), `volume` (100), `rgb` (true), `description` |
| insert_lcd_show | optional `text` (the default date line), `image` (picture path), `fit`, `color` (#7CFFB2), `description`; waist LCD text or picture |
| insert_hide_laser | optional `hide` (true), `description`; hides or shows the shoulder laser assembly |
| insert_toolbox_swap | optional `swap` (true), `description`; toolbox (true) or laser (false) on the laser hinge |
| insert_fire_laser | optional `shots` (3), `interval` (0.25), `speed` (15), `range` (10), `length` (0.4), `color` (#FF2A2A), `sound` (default/none/path), `volume` (100), `description`; laser bolts from the shoulder laser |
| stream_deck_status | Plugin-only presence: `streamDeckSession`, `enabled` |

Property names are case-insensitive; use the camelCase spellings here. Method strings and control enum names are case-sensitive. Unknown JSON properties are rejected; do not invent fields.

## 4. Numeric commands and timing

Each `commands` item supports **only**:

| Field | Type | Meaning |
|---|---|---|
| servo | string | Exact name from `state.controls[].name`, including body and URDF controls |
| control | string or omitted | Exact child of that parent; omit for parent/group |
| afterSeconds | number ≥ 0 | Offset from captured cursor; default 0 |
| value | JSON integer | Required, inside advertised range; use `-100`, not `"-100"` or `-100.5` |
| speed | string | `N/C` (default), `Default`, `Slow`, `Fast`, `Crawl` |
| reason | string | Optional description |

Absolute command time = captured cursor + afterSeconds, rounded to milliseconds, at most 86400 seconds. Negative offsets are rejected. To start at an earlier absolute time, seek there first and obtain fresh state.

For example, captured cursor 5 and afterSeconds 2 means timeline time 7, not time 2. Commands at the same time may target different controls. Conflicting targets at the same millisecond reject the batch without replacing existing commands.

`enableSplines` is a list of parent servo names. It enables their interpolation across the sequence and can affect existing points elsewhere. It cannot disable splines. `N/C` speed preserves the current speed setting; spline timing is defined by the keyframe times.

When asked to spline from the cursor to a position a duration away, put the target servo in `enableSplines` and insert the destination with that `afterSeconds` in the same request. Reuse an existing compatible start anchor; otherwise add an anchor at afterSeconds 0 using a known evaluated starting value. Do not invent a child value from its parent value. Preserve intervening commands and reject/resolve destination conflicts with the user rather than overwriting them.

### Executable example: one undoable head gesture

This deliberately requires an empty timeline and neutral head so it cannot overwrite an existing animation. Remove those restrictions only after inspecting the relevant commands.

```powershell
$state = ((& $client --editor-api --process $editorProcess | Out-String) | ConvertFrom-Json).state
if (-not $state -or $state.commands.Count -ne 0 -or $state.liveDrive -or $state.playback -ne 'Stopped') {
    throw 'Inspect editor context before proceeding.'
}
$head = $state.controls | Where-Object name -eq 'NeckNodUp'
if ($null -eq $head.currentValue -or $head.currentValue -ne 0) { throw 'Expected neutral head.' }
$request = @{
    method = 'insert_commands'
    requestId = [guid]::NewGuid().ToString()
    expectedToken = $state.token
    description = 'Head nod and return'
    enableSplines = @('NeckNodUp')
    commands = @(
        @{servo='NeckNodUp'; afterSeconds=0; value=[int]0},
        @{servo='NeckNodUp'; afterSeconds=2; value=[int]-50},
        @{servo='NeckNodUp'; afterSeconds=4; value=[int]0}
    )
}
$requestFile = Join-Path $env:TEMP ('editor-request-' + $request.requestId + '.json')
$request | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $requestFile -Encoding utf8
$result = (& $client --editor-api --process $editorProcess --request $requestFile | Out-String) | ConvertFrom-Json
if (-not $result.ok) { throw $result.error }
$result.state.commands | Format-Table servo, control, offsetSeconds, value
```

Keep that request file for an identical retry after an uncertain outcome. A successful edit does not mean the sequence has been saved.

## 5. State, responses, and API gaps

`get_state` returns `{ok:true,state:{...}}`. State includes sequence/movie paths, description, cursorSeconds, durationSeconds, playback, liveDrive, unsavedChanges, nextUndo, selectedMarkerSeconds, splineServos, controls, commands, movieSequences, URDF settings and tokens. `controls.currentValue` can be null; children are names, not per-child evaluated positions.

Command snapshots currently expose `servo`, `control`, `offsetSeconds`, `value`, `text`, `speed`, `disable`, `reason`. They **do not expose BreakSpline, persistent group IDs/numbers, hidden-group state, or full audio trim metadata**. Do not assume the response describes all editing state. If these details matter, inspect through the UI or request an API extension.

Responses vary by method: numeric/background edits return `ok`, `requestId`, `inserted`, `state`; receipt-based operations can also return `status` and `error`. Failure is `ok:false` plus `error`; some failures have no receipt. Picker statuses are `awaiting_selection`, `completed`, `cancelled`, `failed`. `get_request` returns the saved receipt, not fresh live state. Session limit is 10,000 mutation receipts.

Not currently available as direct API operations:

- Save/open/switch sequence or movie; create/open a standalone sequence (movie_create_sequence is the narrower exception).
- Replace/delete arbitrary existing commands, change their values/times, proportional offset, group/ungroup/hide groups.
- Set/clear Break Spline, disable commands, disable splines, or request break-preceding-splines insertion.
- Insert arbitrary RGB/audio/text commands via insert_commands. Library insertion can contain them; set_backdrop inserts its own LoadBackdrop text command.
- Insert, move or change Video Clips. They appear in state as `VideoClip` text commands at their start time (settings JSON: path, fit, volume, muted, skipSeconds, endSeconds, sourceSeconds) and through get_video_clips; the user adds them from the timeline menu. To choreograph to one, read its span, look at it with get_video_frames, seek and use get_view_snapshot to compare the pose, then insert_commands. Image reads decode on the editor's UI thread briefly; ask for a few frames at a time.
- Set controller mappings/Expo, start recording, control webcam/Meeting configuration, change servo calibration, or switch the robot model file.
- Set playback-speed slider or request global hardware emergency stop/servo disable. API stop is playback stop, not a documented hardware emergency-stop command.

The UI label “Control or Action” does not rename JSON `servo`. Left/right pairing is presentation only. Persistent naming and values remain unchanged.

# 6. Detailed operation reference

## Direct Library playback / Stream Deck

`get_library_thumbnail` is read-only: supply `libraryKind: "pose"` and `name`
(prefer the exact relative path from `list_library`). It returns `ok: true` and
`images` with `connected`, `disconnected`, `running`, and `active` PNG data URLs,
or `images: null` when the pose has no usable image. Each image is a 144×144
button background: the pose fits within 120×78 pixels above a clear lower
56-pixel caption area, preserving its proportions. No token is required.
The editor caches rendered artwork by image path, modification time, and size;
the plugin refreshes within 30 seconds or on manual Library refresh. Original
files are unchanged, and unreadable or oversized images fall back to an icon.

`apply_library_pose` applies a named Library pose immediately.
`play_library_sequence` plays a named Library sequence; set `loop: true` to repeat.
Both replace existing Library/timeline playback without modifying the document,
timeline commands, or cursor. Names use the same exact/relative-path resolution
as `list_library`. Invalid names reject before stopping current playback.
Invalid file contents stop playback and return a failed receipt.

`driveUrdf` defaults to true and `drivePhysical` defaults to false. Select either
or both; physical requests require Drive HW enabled and connected hardware.
Alternatively, set `useEditorOutputs: true` to request both destinations using
the editor's current settings. This is the Stream Deck plugin's mode: missing
hardware never rejects URDF playback, and physical output is checked against
Drive HW, connection state, and Focus Control at runtime. This mode overrides
explicit output flags. `get_status.state.capabilities` advertises
`editor_configured_library_outputs` when supported.
Unlike timeline-edit APIs, these direct performance commands may run while Live
Drive is on. Background output follows **Focus Control → Stream Deck / Library API**.
The API takes over from active gamepad control while connected gamepads remain
ready for fresh mapped Library/Stop commands. Actual device disconnects and Drive HW off prevent further
physical output; a running Library sequence's clock/audio can continue.

Read `get_status` for lightweight polling: its `state` contains editor version,
generation date, process ID, `libraryToken`, `transportToken`, hardware status,
and a `library` object with name, kind, running, loop, positionSeconds, selecting,
and current output permissions. `get_state` also includes `libraryToken`.
Direct Library mutations require a fresh `libraryToken` as `expectedToken`
(the full edit token is also accepted) and a unique `requestId`.

```json
{
  "method": "play_library_sequence",
  "name": "Greetings/Wave.json",
  "loop": true,
  "useEditorOutputs": true,
  "requestId": "unique-id-for-this-button-press",
  "expectedToken": "libraryToken-from-get_status"
}
```

Omit `name` to open the corresponding Library picker. The initial receipt is
`awaiting_selection`; poll `get_request` until completed, cancelled, or failed.
A newer direct Library command or Stop cancels pending selection. Changes to the
document or Library root while choosing reject the result. Identical retries
return the same receipt and never create another picker or restart playback.

`stop_library` stops the shared Library player and cancels its picker. Existing
`stop` stops both Library and timeline playback and releases movie arrow capture;
it accepts the `transportToken`. The Stream Deck Stop action uses `stop`.

Plugin 1.0.3 sends `stream_deck_status` every second with a unique
`streamDeckSession` and `enabled` reflecting live device connection events. This
updates presence only, never the user's manual enable/disable choice. Sessions
expire after five seconds without a heartbeat. The reply contains `state` with
`streamDeck.connected` and `streamDeck.enabled`. Plugin commands include the same
session ID; a disabled or disconnected session cannot operate the editor. The
Hardware Stream Deck button gates these plugin commands independently of ordinary
API clients. `stream_deck_connection` is advertised in status capabilities.

The installer and setup guide are in `../StreamDeckXL` relative to the application
source project. The plugin connects directly to the same-user Windows named pipe,
not through keyboard shortcuts, an HTTP server, or a Codex task.

## Movie sequence order and membership

`get_state.movieSequences` lists each entry's zero-based index, path, description,
duration, loop/trigger settings, and selection. Movie changes invalidate the edit
token. All operations require a loaded/saved movie, fresh expectedToken and unique
requestId. Pause playback and turn Drive HW off first.

- `movie_move`: supply `index` and `toIndex` (the final position after the move).
- `movie_remove`: supply `index`. Detaches this occurrence only; never deletes its file.
- `movie_insert`: supply `sequencePath` (absolute or relative to Configuration),
  and optional `index` (insertion boundary, defaults to append).
- `movie_create_sequence`: supply `name` and optional `index`. Creates a blank
  sequence JSON beside the movie and inserts it. Existing files cannot be overwritten.

These operations preserve the open sequence and unsaved command edits. Removing
its movie entry leaves that sequence open but detached. The new movie order is
unsaved until **Save Movie** in the UI. Movie-list changes do not use the sequence
command undo stack; reverse a move or reinsert a removed file to restore it.

These commands use the same compiled client described in EditorApi.md. All
mutations require a unique requestId and expectedToken from get_state.

## Library insertion

Read `{"method":"list_library","libraryKind":"pose"}` (or `sequence`) to list
valid item names and relative paths. Insert by supplying `method: "insert_library"`,
`libraryKind: "pose"` or `"sequence"`, and `name: "Greeting"` (with or without
`.json`). Duplicate basenames require the relative path from list_library.

Omit `name` to open the existing Library Pose/Sequence selection window. The
initial response has `status: "awaiting_selection"`. Query
`{"method":"get_request","requestId":"the-original-request-id"}` to obtain
`completed`, `cancelled`, or `failed`. Selection is anchored to the cursor and
document at the original request. Cancel changes nothing. Retrying the identical
request never opens a second picker.

Poses put every command at the cursor; sequences preserve relative offsets.
Both are one undo step. Conflicts are rejected without opening a conflict dialog.
Library audio is referenced within Configuration rather than copied into the
Project folder; missing audio rejects insertion. Original library files are not
changed. These references are portable with the Configuration folder.

## Playback

Methods: `play`, `pause`, `stop`, and `seek` (with `seconds` as an absolute timeline
time). Play uses the Sequence UI behavior: fresh play starts at the current cursor and paused play
resumes there. Use seek, then play, to start at a selected time; seek to zero to play from the beginning. Repeated play while
running does not toggle it off. Movie transport is not included in this version.

For these methods, expectedToken may be the `transportToken` from get_state.
Unlike the edit token it stays valid as playback advances, but changing the active
document invalidates it. API play/seek require Drive HW off. Pause/stop remain
available with Drive HW on. The detached URDF Model window may stay open.

## Collision warnings

`set_collision_warnings` with `enabled: true` or `false` updates the existing
URDF warning toggle. `get_collisions` reads current status without a token:
enabled, modelAvailable, urdfDriveEnabled, cursorSeconds, current collision pair
IDs, and collision command-marker times observed during playback. This information
also appears in get_state. Enable URDF Drive in the UI to track timeline motion.

An empty pair list when no model is loaded, warnings are disabled, or URDF Drive
is off does not establish collision-free animation. These are the existing URDF
preview diagnostics, not a complete swept-motion or physical-robot safety test.

## Backgrounds, 3D stage, and robot framing

`get_state.state.urdf` (or `get_urdf.urdf`) reports the loaded robot `modelFile`, complete `framing`, background preset, and evaluated `stage`. `stage` is null outside 3D mode. Use evaluated stage coordinates for relative motion; background settings contain the preset's initial coordinates. Framing contains `horizontalPercent`, `verticalPercent`, `spinDegrees`, `rotateDegrees`, `yaw`, `pitch`, and `distance`. Distance is metres: camera zoom percentage = `115 / distance`; yaw/pitch are radians. These APIs do not change the robot model file.

The following operations edit the active sequence, require a fresh `expectedToken` and unique `requestId`, and use the standard retry, conflict, Drive HW-off, and paused-playback rules. They are undoable, never drive physical hardware, and must be saved normally through the editor. View changes now also invalidate an edit token. Read state again after each successful operation.

### Set background / load backdrop

`set_backdrop` inserts one `LoadBackdrop` command at the captured cursor. Supply `backdrop.background` with any `UrdfPresentationSettings` values. Allowed `backgroundMode` strings: `Studio`, `Color`, `Image`, `MovingImage`, `Urdf3D` (UI label **3D URDF**). This is a complete preset, not a partial patch: start with the returned background object when preserving existing options. Optional `backdrop.framing` supplies robot framing. An omitted framing preserves the current view; entering a stage with `preserveRobotFraming: true` clears initial Spin/Rotate. Explicit framing takes precedence.

For an image anywhere on disk, supply optional `backgroundImageSource` with its absolute path. The editor copies it into Config/Backdrops and stores the portable relative reference. Direct `backgroundImage` paths are also imported when used. Re-selecting an unchanged source reuses its copy. Image imports may leave an unused copy if a later conflict rejects the timeline edit; the source is never overwritten. All moving-image speed, frame cap, seed, pan, zoom range, rotation and half-turn settings are supported.

Example (replace token, ID and path as appropriate):

```json
{
  "method": "set_backdrop",
  "requestId": "stage-load-001",
  "expectedToken": "TOKEN_FROM_GET_STATE",
  "backdrop": {
    "background": {
      "backgroundMode": "Urdf3D",
      "stage": {
        "modelFile": "Models/Scenery/MatrixConstruct/MatrixConstruct_backdrop.urdf",
        "movementMode": "RobotCentric",
        "preserveRobotFraming": true,
        "robotX": 0, "robotY": 0, "headingDegrees": 0,
        "detectFloor": true, "floorHeight": -1.078381,
        "viewPanX": 0, "viewPanY": 0, "viewPanZ": 0,
        "viewZoomPercent": 100, "viewYawDegrees": 0,
        "edgeMarginPercent": 20
      }
    }
  }
}
```

`RobotCentric` keeps the robot's on-screen size during stage zoom; the backdrop alone scales. `RoomCentric` uses perspective, with lateral edge following. Positions/pan use metres. Heading is degrees: +75 turns left 75 degrees relative to the room; +X is forward at zero heading and +Y is left.

### Framing

`set_urdf_framing` takes `framing` with the same fields as returned by state. It captures the current background and evaluated stage position in a LoadBackdrop command at the cursor, with the requested framing. A LoadBackdrop already at that millisecond is a conflict; include background and framing together in `set_backdrop` if setting both.

For smooth framing changes, use `insert_commands` and `enableSplines` with `URDFSlide`, `URDFVertical`, `URDFZoom`, `URDFSpin`, and `URDFRotate`. They retain the existing native ranges and timed-anchor rules. LoadBackdrop itself switches immediately.

### Turn and move relative to the room

`move_robot` accepts optional `turnDegrees`, `forwardMeters` and `seconds` (defaults 0). It requires a loaded 3D stage. The destination is the current position plus the requested distance along the destination stage heading (current stage heading + turn). This helper uses stage heading only; it does not incorporate a separate URDFSpin/URDFRotate into its direction. Use TrackForward for motion relative to the robot’s full evaluated front orientation. Negative distance moves backward. It creates `URDFStageX`, `URDFStageY` (whole centimetres) and `URDFStageHeading` (whole degrees). Position range is ±1000 metres; heading is ±36000 degrees.

For `seconds > 0`, the endpoint is at captured cursor + seconds. The API enables those spline controls and adds missing start anchors at the cursor. It rejects conflicting anchors/endpoints and preserves other commands. Existing intervening commands can affect the path; inspect state first. A zero-duration request creates immediate commands without changing spline settings. Since these are editable timeline operations, a timed request previews its starting point; play or seek to see the movement.

To **turn 75 degrees left, then move forward**: issue `move_robot` with `turnDegrees: 75, seconds: 2`; seek to the endpoint; read fresh state; then issue `move_robot` with `forwardMeters: 1, seconds: 3`. A combined turn and distance request animates both together toward the computed endpoint, rather than producing a turn-then-drive action.

```json
{"method":"move_robot","requestId":"turn-001","expectedToken":"FRESH_TOKEN","turnDegrees":75,"seconds":2}
```

Use standard `seek` and `play` afterwards. `insert_commands` also accepts the stage numeric controls directly for absolute positions and custom paths. Stage/background commands are excluded from physical hardware output and hardware animation exports. Backdrops are scenery, not collision obstacles.

### Track Forward (signed stage velocity)

Use numeric servo `TrackForward` through `insert_commands`. `value` is **cm/s**, −500…500, with positive forward relative to the evaluated robot front (including URDFSpin/URDFRotate), negative reverse and zero stopped. It requires a 3D URDF backdrop. It never reaches hardware. `get_state.controls` advertises its range/current timeline speed; `urdf.trackForward` also reports controller speed separately.

This example starts at 1 m/s at the captured cursor and stops 3 seconds later. With TrackForward spline disabled it travels 3 metres if heading is fixed. Read spline state first: when enabled, the two points ramp speed instead of holding it.

```json
{
  "method": "insert_commands",
  "requestId": "track-forward-001",
  "expectedToken": "FRESH_TOKEN",
  "commands": [
    { "servo": "TrackForward", "afterSeconds": 0, "value": 100 },
    { "servo": "TrackForward", "afterSeconds": 3, "value": 0 }
  ]
}
```

To ramp speed, include `TrackForward` in `enableSplines` and provide the appropriate start/destination speeds. To reverse, supply a negative speed. URDFStageHeading and URDFSpin commands steer movement relative to the robot’s front. Camera/backdrop view rotation does not change its direction. Track displacement is added to StageX/Y animation since the latest LoadBackdrop; a new backdrop resets it. Absolute StageX/Y command values therefore describe the positional baseline when TrackForward is present. `move_robot` accounts for existing accumulated track displacement when creating its position anchors. Changes are timeline edits: play or seek to preview them, and save the sequence normally.

TrackForward, URDFSpin and URDFStageHeading commands also animate the full-body tread frames and front hubs when the loaded robot has a tread-animation manifest. No separate API calls are needed. Spin/heading turns add opposite left/right belt motion; turning while driving combines the two. These visuals are independent of physical track hardware.

## 7. Complete background and framing field reference

These objects are whole presets, **not patches**. Clone returned state, modify fields, and send the complete object to retain existing settings. Do not send computed state-only fields like `modelFile` from the outer `urdf` object in a framing object.

`backdrop`: `{ "background": { ... }, "framing": { ... } }`; framing is optional.

Background fields:

| Field | Type / units |
|---|---|
| backgroundMode | Studio, Color, Image, MovingImage, Urdf3D |
| backgroundColor | Color string such as #00FF00 (chromakey green) |
| backgroundImage | Imported path relative to Configuration |
| imageLayout | Stretch, Cover, Tile |
| cleanUndockedView | boolean |
| backgroundMotionSpeed | number, motion speed setting |
| backgroundFrameRate | integer, background update cap |
| backgroundZoomMinimum / backgroundZoomMaximum | percent; keep minimum ≥100 and maximum ≥minimum |
| backgroundPanAmount | percent |
| backgroundTargetSeconds | seconds between new targets |
| backgroundSeed | integer for reproducibility |
| backgroundRotation / backgroundHalfTurns | booleans |
| stage | object described below |

Stage fields: `modelFile` (existing stage URDF path), `movementMode` (RobotCentric or RoomCentric), `preserveRobotFraming` (boolean), `robotFraming` (optional framing), `robotX`, `robotY`, `floorHeight`, `viewPanX`, `viewPanY`, `viewPanZ` (metres); `headingDegrees`, `viewYawDegrees` (degrees); `detectFloor` (boolean); `viewZoomPercent` (10–1000); `edgeMarginPercent` (5–45). Coordinates must be finite. ViewPanZ is a backdrop view adjustment, not a robot vertical movement command.

Framing fields: `horizontalPercent`, `verticalPercent`, `spinDegrees`, `rotateDegrees`, `yaw`, `pitch`, `distance`. Yaw/pitch use radians; distance must be .55–11.5 metres. Zoom percent = 115 / distance (100% = 1.15 m; 10% = 11.5 m). Use rotateDegrees, not the legacy rotatePercent compatibility field.

Numeric URDF timeline controls (still confirm ranges with get_state):

| Name | Units / semantics |
|---|---|
| URDFSlide | Horizontal viewport percentage; negative left, positive right |
| URDFVertical | Vertical viewport percentage |
| URDFZoom | Camera zoom percentage |
| URDFSpin | Cumulative whole degrees around robot neck centre; multiple turns supported (signed 32-bit command storage). 720 means two positive turns. |
| URDFRotate | Degrees, front-view rotation; typically −360…360 |
| URDFStageX / URDFStageY | Centimetres, unlike stage object coordinates in metres |
| URDFStageHeading | Degrees relative to room |
| TrackForward | Signed cm/s along robot front; always insert a stop when finite travel is intended |

### URDF TV (camera to the backdrop TV, images and video on its screen)

Three methods insert the matching timeline commands at the captured cursor, each as one undo step. They require a 3D URDF backdrop whose screen link exists (default `backdrop_tv_screen_link`); `get_state.urdf.tv` reports `available`, `active`, `weight` (0 normal camera … 1 facing the TV), `arrived`, `robotHidden`, `screenMetres` and `screenRect` (x, y, width, height in device-independent pixels of the current view, computed from the shot even before arrival).

| Method | Fields | Command |
|---|---|---|
| `insert_tv_front` | `seconds` (0–30, default 3), `fill` (0.2–1, default 0.85), `screen` (link) | URDF TV Front: glide past the robot to face the TV; robot hidden while passing and facing it |
| `insert_tv_show` | `mediaPath` (absolute or Configuration-relative; omit for a blank screen), `fit` (contain/cover/stretch), `startSeconds`, `loop`, `volume` (0–100, default 100) | TV Show: media is copied into `Config/TvMedia` unless already inside Configuration |
| `insert_tv_return` | `seconds` (0–30, default 3) | URDF TV Return: fade out and glide back to the normal camera |

```json
{"method":"insert_tv_front","requestId":"tv-1","expectedToken":"FRESH_TOKEN"}
{"method":"insert_tv_show","requestId":"tv-2","expectedToken":"FRESH_TOKEN","mediaPath":"C:/Videos/intro.mp4","volume":80}
{"method":"insert_tv_return","requestId":"tv-3","expectedToken":"FRESH_TOKEN"}
```

Responses add `text` (the command's settings) and, for Front/Return, `endSeconds`. Evaluation is stateless: the latest Front/Return at or before the cursor decides the camera blend (a new glide starts from the blend in effect), and the latest TV Show since the latest Front supplies the media. The show fades in 0.3 s after arrival and fades out over the first 0.3 s of a return. Video time is `cursor − max(show time, arrival) + startSeconds`, looping or holding the last frame.

### URDF Beam (transporter beam in and out)

`insert_beam` inserts one **URDF Beam** command at the captured cursor as one undo step. It needs a URDF view, and works with any model.

| Field | Meaning |
|---|---|
| `direction` | `in` (materialize, default) or `out` (dematerialize) |
| `seconds` | 0.5–20, default 3.5 |
| `particles` | default true: particles fall from the top of the view (in) or rise to it (out) |
| `sound` | `default` (synthesized `Library/Audio/Beam_In.wav`/`Beam_Out.wav`), `none`, or an audio path (absolute or Configuration-relative; must exist) |
| `volume` | 0–100, default 100 |
| `rgb` | default true: also sends the Arduino `Beam` ring sparkle for the same duration |

```json
{"method":"insert_beam","requestId":"beam-1","expectedToken":"FRESH_TOKEN"}
{"method":"insert_beam","requestId":"beam-2","expectedToken":"FRESH_TOKEN","direction":"out","seconds":3,"sound":"none"}
```

Responses add `text` (the command's settings) and `endSeconds`. Evaluation is stateless. With no beam commands the robot is visible. Before a first Beam In it is hidden. During a beam it shimmers, and afterwards it is visible after In and hidden after Out. The sound plays only during playback.

### Waist LCD, shoulder laser and toolbox

`insert_lcd_show`, `insert_hide_laser` and `insert_toolbox_swap` each insert one command at the captured cursor as one undo step. They need a URDF view; they only show on the full-body model, which has the waist LCD, laser and toolbox.

| Method | Field | Meaning |
|---|---|---|
| `insert_lcd_show` | `text` | Text on the LCD; default "If you can read this, you should have asked me out on a date first!" (empty when an `image` is given). An empty string leaves the screen dark. Up to 2000 characters. |
| | `image` | Picture (absolute or Configuration-relative; must exist). Shown in place of the text. |
| | `fit` | `contain` (default), `cover` or `stretch` |
| | `color` | Text colour as `#RRGGBB`, default `#7CFFB2` |
| `insert_hide_laser` | `hide` | Default true hides the whole laser assembly (support, laser and toolbox); false shows it |
| `insert_toolbox_swap` | `swap` | Default true shows the red toolbox on the hinge; false shows the laser |

```json
{"method":"insert_lcd_show","requestId":"lcd-1","expectedToken":"FRESH_TOKEN","text":"Number 5 is alive!"}
{"method":"insert_toolbox_swap","requestId":"laser-1","expectedToken":"FRESH_TOKEN","swap":false}
{"method":"insert_hide_laser","requestId":"laser-2","expectedToken":"FRESH_TOKEN","hide":true}
```

Responses add `text` (the command's settings). Evaluation is stateless: the latest enabled command at or before the cursor decides. With none, the LCD shows the default text, the laser assembly is shown, and he carries the toolbox. Open the doors with `B_PanelsOpen` to see the LCD, and lower the laser or toolbox with `B_LaserLower`.

### Fire Laser

`insert_fire_laser` inserts one **Fire Laser** command at the captured cursor as one undo step: a burst of glowing bolts that leave the shoulder laser's firing window and fly straight out along the laser into the URDF scene. Nothing fires unless the laser itself is showing (Hide Laser false and Toolbox Swap false; he carries the toolbox by default). Aim with `B_LaserLower`, `B_WaistSwivel` and the chest controls; each bolt keeps the direction it left in.

| Field | Meaning |
|---|---|
| `shots` | Bolts in the burst, 1–20, default 3 |
| `interval` | Seconds between bolts, 0.05–2, default 0.25 |
| `speed` | Metres per second, 1–60, default 15 |
| `range` | Metres each bolt flies before it is gone, 0.5–30, default 10 |
| `length` | Bolt length in metres, 0.05–2, default 0.4 |
| `color` | `#RRGGBB`, default `#FF2A2A` |
| `sound` | `default` (synthesized "pew", `Library/Audio/Laser_Fire.wav`), `medium` (between the two, `Library/Audio/Laser_Medium.wav`), `heavy` (synthesized deep, guttural blast, `Library/Audio/Laser_Heavy.wav`), `none`, or an audio path; plays once per bolt |
| `volume` | 0–100, default 100 |

```json
{"method":"insert_toolbox_swap","requestId":"fire-0","expectedToken":"FRESH_TOKEN","swap":false}
{"method":"insert_fire_laser","requestId":"fire-1","expectedToken":"FRESH_TOKEN","shots":5,"interval":0.15}
```

Responses add `text` and `endSeconds` (when the last bolt is gone). Evaluation is stateless; the sound plays only during playback.

### Disassemble (baked collapse and reassembly)

`insert_fall_apart` inserts one **Disassemble** command at the captured cursor. It plays a clip baked by `FullBodyModel/Body/bake_fall_apart.py`: after a short shudder every body part falls to the floor as a separate rigid piece (the head from the LED mouth up falls as one piece). The clip must sit beside the loaded model at `Meshes/Body/FallApart/fall_apart.json`; `get_state.urdf.fallApart` reports `available`, `fps`, `frameCount` and `durationSeconds`.

| Field | Meaning |
|---|---|
| `reverse` | Optional, default `false`. `false` falls apart and stays in pieces; `true` starts in pieces and reassembles. |
| `speed` | Optional playback multiplier, 0.1–10, default 1. |
| `description` | Optional command reason; defaults to "Disassemble" or "Reassemble". |

```json
{"method":"insert_fall_apart","requestId":"fall-001","expectedToken":"FRESH_TOKEN"}
{"method":"insert_fall_apart","requestId":"rebuild-001","expectedToken":"FRESH_TOKEN","reverse":true,"speed":0.5}
```

The response adds `endSeconds`, the timeline time at which the clip finishes. The latest Disassemble command at or before the cursor decides what is shown, so seeking and backward scrubbing are exact. During the shudder the parts blend from the current servo-driven pose into the clip, so a posed robot settles instead of snapping; reassembly blends back and then releases the override. While in pieces, other timeline motion is ignored and collision warnings are suppressed. The command is display-only: it never drives hardware, is excluded from hardware exports, and is one undo step. Conflicts (another Disassemble at the same millisecond) are rejected.

TrackForward plus turning also animates treads and rear caster when supported by the loaded model. A trailing (offset) caster is rolled round by the motion through the full 360 degrees. It trails the swivel when going forward and swings round under the caster arm in reverse (about halfway after 0.35 m). It turns only while moving and holds at rest. These visuals do not command physical tracks.

## 8. Body controls for animation planning

These are exact numeric `servo` names. All use native −100…100 except B_PanelsOpen, B_LaserLower, B_TipToe and the hand gangs, which use 0…100, mapped piecewise about neutral to the configured angular range. Zero is the built/default joint pose when its configured range includes zero; otherwise it is clamped to the nearest endpoint. URDF Configuration can override angle limits and reversal, so the default radians below are not guaranteed runtime calibration. The API does not expose these body calibration overrides; consult URDF Configuration when physical angle accuracy matters.

Body controls require a model containing those joints. Their presence in get_state's enum list alone does not prove the loaded model supports them. No body control is sent to physical hardware. Paired names use the same semantic positive direction on each side, with mirrored geometry. Set each side explicitly; there are no API “both arms” aliases. The B_OpenLeftHand and B_OpenRightHand hand gangs are described below.

| Exact servo name | Default angular range (radians) | Meaning |
|---|---|---|
| B_WaistLean | -0.35 to 0.35 | Waist: positive leans forward |
| B_ChestTilt | -0.35 to 0.35 | Chest: positive leans forward |
| B_WaistSwivel | -1.57 to 1.57 | Waist swivel: positive turns the upper body to its left (anticlockwise seen from above) |
| B_TipToe | 0 to 1.069363233 | TipToe: both tracks rotate front-down about the pelvis drum (0 tracks flat, 100 = 61.27 degrees, the front-top tread vertical to the body). The caster four-bar swings down (upper brace 41.7°, lower brace 36.9°) and the robot tips back 5.7° on its planted front rollers until the caster wheel is on the floor; the body rises 19.8 cm and moves forward 28.8 cm, and the caster wheel rolls in about 34 cm. Negative values stay flat (native 0…100). Virtual joint: models without the track parts and caster linkage markers do not support it. |
| B_PanelsOpen | 0 to 1.75 | Waist panels: 0 closed, 100 both doors fully open, showing the LCD (native 0…100) |
| B_LaserLower | 0 to 1.221730476 | Shoulder laser or toolbox: 0 up at 70 degrees, 100 lowered level, resting on the shoulder (native 0…100) |
| B_NeckLift | -0.35 to 0.786332313 | Neck linkage: positive swings backward and raises the head |
| B_ShoulderRotateLeft | -1 to 3.577924967 | Left shoulder rotation around lateral hub axis |
| B_ShoulderHugLeft | -0.5 to 0.5 | Left NEW independent hinge between yoke indentations |
| B_ArmTwistLeft | -1.57 to 1.57 | Left Arm: turn forearm outward |
| B_ElbowLeft | -1 to 1.2 | Left Elbow: bend forearm up |
| B_WristPitchLeft | -1 to 1 | Left Wrist: tip hand up |
| B_WristRollLeft | -3.14 to 3.14 | Left Wrist: roll hand inward |
| B_FingerSpreadUpperLeft | -0.2 to 0.6 | Left Upper spread axle |
| B_FingerSpreadLowerLeft | -0.2 to 0.6 | Left Lower spread axle |
| B_FingerCurlUpperLeft | -0.3 to 0.8 | Left Upper finger base curl |
| B_FingerTipCurlUpperLeft | -0.3 to 0.003995 | Left Upper middle-joint curl |
| B_FingerEndCurlUpperLeft | -0.3 to 0.007600 | Left Upper independent outer fingertip hinge (detailed-hand model) |
| B_FingerCurlLowerLeft | -0.3 to 0.8 | Left Lower finger base curl |
| B_FingerTipCurlLowerLeft | -0.3 to 0.003995 | Left Lower middle-joint curl |
| B_FingerEndCurlLowerLeft | -0.3 to 0.007600 | Left Lower independent outer fingertip hinge (detailed-hand model) |
| B_ThumbCurlLeft | -0.3 to 0.002437 | Left Single finger base curl |
| B_ThumbTipCurlLeft | -0.3 to 0.006650 | Left independent thumb-tip hinge (detailed-hand model) |
| B_ShoulderRotateRight | -1 to 3.577924967 | Right shoulder rotation around lateral hub axis |
| B_ShoulderHugRight | -0.5 to 0.5 | Right NEW independent hinge between yoke indentations |
| B_ArmTwistRight | -1.57 to 1.57 | Right Arm: turn forearm outward |
| B_ElbowRight | -1 to 1.2 | Right Elbow: bend forearm up |
| B_WristPitchRight | -1 to 1 | Right Wrist: tip hand up |
| B_WristRollRight | -3.14 to 3.14 | Right Wrist: roll hand inward |
| B_FingerSpreadUpperRight | -0.2 to 0.6 | Right Upper spread axle |
| B_FingerSpreadLowerRight | -0.2 to 0.6 | Right Lower spread axle |
| B_FingerCurlUpperRight | -0.3 to 0.8 | Right Upper finger base curl |
| B_FingerTipCurlUpperRight | -0.3 to 0.003995 | Right Upper middle-joint curl |
| B_FingerEndCurlUpperRight | -0.3 to 0.007600 | Right Upper independent outer fingertip hinge (detailed-hand model) |
| B_FingerCurlLowerRight | -0.3 to 0.8 | Right Lower finger base curl |
| B_FingerTipCurlLowerRight | -0.3 to 0.003995 | Right Lower middle-joint curl |
| B_FingerEndCurlLowerRight | -0.3 to 0.007600 | Right Lower independent outer fingertip hinge (detailed-hand model) |
| B_ThumbCurlRight | -0.3 to 0.002437 | Right Single finger base curl |
| B_ThumbTipCurlRight | -0.3 to 0.006650 | Right independent thumb-tip hinge (detailed-hand model) |

NeckLiftLevel and NeckLiftFrontRod are automatic URDF mimic followers, not independent API controls. For an animation based on video, plan timed poses in native units, provide start/hold/return anchors, and preserve existing commands. Kinematic estimates are not collision validation.

## 9. Source provenance and maintenance

Checked against EditorApi.cs (request DTO, numeric validation, pipe transport), EditorApiClient.cs, MainWindow.Api.cs, MainWindow.Api.LibraryPlayback.cs, MainWindow.Api.DirectLibrary.cs, MainWindow.Api.Movie.cs, MainWindow.Api.Backdrops.cs, UrdfPresentationSettings.cs, UrdfStageSettings.cs, UrdfModelSettings.cs, BodyControlCatalog.cs, and EditorApiExtensions.md. This standalone guide is intended for an AI handoff; Connect to the editor API gives the shorter setup workflow, and API operations gives the field reference. The October 8 update corrects workspace paths and cursor-start playback behavior. Local editor and Help regression checks verify the build; this documentation work does not mutate a running editor or move hardware.

When continuing in another environment, treat the running editor's get_state/get_status responses as capability discovery. Do not claim an unavailable UI feature has an API endpoint merely because this application supports it in the UI.

### Whole-hand opening controls

`B_OpenLeftHand` and `B_OpenRightHand` are URDF-only numeric targets in `insert_commands`, with native values **0 to 100 percent**: 0 closes the hand to its safeguarded limits; 100 opens all finger and thumb bends and spreads to their configured open limits. Intermediate percentages interpolate each joint's angular travel. Splines and controller mappings are supported through the normal numeric-control paths. These are aliases over the loaded hand joints, not joints named OpenHandLeft/Right. A later individual joint command takes precedence over an earlier gang command; individual commands win ties. Saved poses expand the gang into individual joint values.

Shoulder controls: `B_ShoulderRotateLeft/Right` retain their independent lateral-axis shoulder rotation. `B_ShoulderHugLeft/Right` are NEW controls for the upright hinges between the yoke indentations. All four native targets use -100 to +100 values, with zero the default pose. Hug is limited to -0.5 to +0.5 radians to match the yoke clearance; Rotate extends from -1 radian to +205 degrees (3.577924967 radians), reaching the raised vertical housing position in the default body pose. Each control maps to its own URDF joint with the B_ prefix removed. Rotate commands are not aliases for Hug commands. ArmTwist remains independent. The old sideways ShoulderRaise axis remains fixed; its legacy editor commands are accepted only as compatibility aliases to Hug and are not offered in new picklists.
