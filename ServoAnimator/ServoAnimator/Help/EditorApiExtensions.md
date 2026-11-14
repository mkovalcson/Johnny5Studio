# Library, playback, and URDF API commands

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

The installer and setup guide are in `X:\Johnny5Studio\StreamDeckXL` in this workspace (a sibling of the solution folder). The plugin connects directly to the same-user Windows named pipe,
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

`move_robot` accepts optional `turnDegrees`, `forwardMeters` and `seconds` (defaults 0). It requires a loaded 3D stage. The destination is the current position plus the requested distance along the destination heading (current heading + turn). Negative distance moves backward. It creates `URDFStageX`, `URDFStageY` (whole centimetres) and `URDFStageHeading` (whole degrees). Position range is ±1000 metres; heading is ±36000 degrees.

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

### Disassemble (baked collapse and reassembly)

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

### Saved scene timeline commands

The UI now calls `LoadBackdrop` **Load Scene**. Its text value can reference a saved complete scene:
`{"SceneFile":"LibraryScenes/Scene-Example.json"}`. Playback loads that scene's robot, background,
placement and camera at the command time. Relative paths resolve against Config; absolute scene
paths are also accepted. Existing embedded backdrop settings and the `set_backdrop` API remain
compatible. The command identifier stays `LoadBackdrop`; only its UI name and picker have changed.

## Sequence insertion overlap review

`insert_library` with `libraryKind: "sequence"` assigns all accepted inserted commands one fresh group ID. If existing controls have commands anywhere in the insertion span, an owned overlap review window shows original/inserted counts per control and Keep original / Use inserted / Merge choices. Merge prefers inserted commands only at identical millisecond time keys. Cancel leaves the timeline and Undo history unchanged.

A named sequence with overlaps returns `awaiting_selection`, just like the library picker. Poll `get_request` using the same requestId until `completed`, `cancelled`, or `failed`; do not issue another insertion. A named sequence without overlaps completes immediately. Pose insertion behavior is unchanged.
