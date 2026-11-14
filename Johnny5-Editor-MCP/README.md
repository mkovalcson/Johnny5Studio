# Johnny 5 Animation Editor: MCP server for Claude Code

Lets Claude Code read and edit your open Animation Editor in plain language:
"add a slow head nod at the cursor", "what's on the timeline between 3 and 6
seconds?", "turn him 75 degrees left over two seconds, then drive forward a
metre", "play it from the start". It talks to the editor's local API (the
same-user named pipe), following `AnimationEditor_API_For_AI.md`.

## Setup (once)

1. In the editor, check **Config > Enable Codex Connection**.
2. Run `Setup-Johnny5-MCP.bat` in this folder. It:
   - finds Python 3.10 or newer (the `py` launcher or `python`);
   - creates a private environment in `.venv` and installs the MCP SDK;
   - registers the server with Claude Code for all your projects.
3. In Claude Code, run `/mcp`: **johnny5-editor** should show as connected.

If the `claude` command isn't installed, the script prints the registration
command to run, or to add in the Claude desktop app's MCP settings:

    claude mcp add --scope user --transport stdio johnny5-editor -- "<this folder>\.venv\Scripts\python.exe" "<this folder>\johnny5_editor_mcp.py"

## Safety defaults

- **Read before edit.** Claude must read the editor's state first. Each edit is
  sent with the token from the state Claude last read, so if you change
  something in the editor in the meantime, the edit is rejected rather than
  landing in the wrong place, and Claude is told to re-read and reconsider.
- **No duplicate edits.** Every edit gets a unique ID. If a reply is lost, the
  server retries the identical request (the editor applies it only once); if
  it's still unclear, Claude checks the receipt instead of trying again.
- **No physical output by default.** Library poses and sequences drive only
  the on-screen robot, and timeline **play** is refused while the editor
  reports hardware drive on. To allow physical output, register the server with
  `--env J5_ALLOW_PHYSICAL=1` (before the server name). The Drive HW check reads
  the editor's reported hardware status; turning Drive HW off in the editor is
  the sure way to keep the real robot still.
- **Careful undo.** Claude must name the action it expects to undo, and the
  server checks it against the editor's next undo first.
- **Several editors.** With more than one editor open, Claude must pick one
  (list_editors, select_editor) instead of guessing.
- Edits are **not saved**: save in the editor as usual.

## Tools

| Reading | Editing and playback |
| --- | --- |
| list_editors, select_editor | insert_commands (timed numeric commands, optional splines) |
| get_state (summary, or full) | undo (names the action it undoes) |
| list_controls (filter by name) | play, pause, stop, seek |
| list_commands (time window, servo) | insert_library, apply_library_pose, play_library_sequence, stop_library |
| get_status, get_urdf, get_collisions | movie_move, movie_remove, movie_insert, movie_create_sequence |
| list_library, get_library_thumbnail (as an image) | set_backdrop, set_urdf_framing, move_robot |
| get_request (receipts) | call_method (for methods newer than this server) |
| list_video_clips, get_video_frames, get_view_snapshot (as images) | |

## Choreographing to a video

When a sequence has Video Clips behind the robot (added in the editor from the
timeline menu), Claude can look at them: "watch the clip from 4 to 10 seconds
and have him duck when the car swerves". `list_video_clips` gives each clip's
span; `get_video_frames` shows the video at timeline times (up to 12 pictures
per call); after a `seek`, `get_view_snapshot` shows the robot's pose drawn over
that moment of the video. Claude inserts moves with `insert_commands` and checks
them with another snapshot. These tools only read: adding or moving clips stays
in the editor.

## What the API can't do yet

From the API guide: save, open or switch documents; change or delete existing
commands; set Break Spline or disable splines; insert RGB, audio or text
commands directly; recording, calibration and controller mappings. Claude will
say so rather than work around it.

## Troubleshooting

- **"No Animation Editor with its API enabled was found"**: open the editor and
  check Config > Enable Codex Connection.
- **"Several editors are running"**: ask Claude to list editors and pick one.
- **"Read the editor first"** or **"the editor changed since you last read it"**:
  expected after you edit in the editor yourself; Claude re-reads and carries on.
- **Not connected in `/mcp`**: run the setup again; check that the paths in the
  registration still exist.

`test_mcp_server.py` holds the tests used to develop this server against a
simulated editor (32 checks); it isn't needed to run it. The video tools need an
editor build with Video Clips (2.4 or later).
