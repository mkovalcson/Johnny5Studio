"""Johnny 5 Animation Editor: MCP server for Claude Code.

Lets Claude read and edit the open Animation Editor through its local API (the
same-user named pipe `Johnny5.AnimationEditor.<PID>`), following the rules in
AnimationEditor_API_For_AI.md:

* Claude must read the editor's state before editing; each edit is sent with
  the token from the state Claude last read, so an edit made on a stale view of
  the timeline is rejected by the editor instead of landing in the wrong place.
* Every mutation gets a unique requestId.  After a timeout the identical request
  is retried with the same ID (safe: the editor de-duplicates), never a new one.
* Physical output is OFF unless the server is started with J5_ALLOW_PHYSICAL=1.
  Library playback then drives only the on-screen robot, and timeline playback
  is refused while the editor reports hardware drive switched on.
* Undo requires naming the action expected to be undone.

Enable the editor side with File > Enable Codex Connection.
"""
import base64, functools, json, os, re, sys, threading, time, uuid
from pathlib import Path
from typing import Any

from mcp.server.mcpserver import MCPServer, Image
from mcp.types import ToolAnnotations

PIPE_PREFIX = "Johnny5.AnimationEditor."
ALLOW_PHYSICAL = os.environ.get("J5_ALLOW_PHYSICAL", "").strip() in ("1", "true", "yes")
CONNECT_TIMEOUT = 5.0
RESPONSE_TIMEOUT = 35.0
RETRIES_AFTER_TIMEOUT = 2
MAX_LIST = 500

READ_METHODS = {"get_state", "get_status", "get_urdf", "get_collisions", "list_library",
                "get_library_thumbnail", "get_request", "get_video_clips", "get_video_frames", "get_view_snapshot"}
MAX_VIDEO_FRAMES = 12
EDIT_TOKEN_METHODS = {"insert_commands", "insert_library", "undo", "movie_move", "movie_remove", "movie_insert",
                      "movie_create_sequence", "set_backdrop", "set_urdf_framing", "move_robot"}
TRANSPORT_METHODS = {"play", "pause", "stop", "seek", "set_collision_warnings"}
LIBRARY_METHODS = {"apply_library_pose", "play_library_sequence", "stop_library"}
BLOCKED_METHODS = {"stream_deck_status"}                  # plugin-only; an AI must not impersonate the Stream Deck
COMMAND_FIELDS = {"servo", "control", "afterSeconds", "value", "speed", "reason"}


class Uncertain(Exception):
    """The request may or may not have been applied (timeout or broken connection)."""


# ===================================================================== transport
class PipeTransport:
    """One UTF-8 JSON line out, one line back, per connection (Windows named pipe)."""

    def list_pids(self):
        try:
            names = os.listdir("\\\\.\\pipe\\")
        except OSError:
            return []
        return sorted(int(n[len(PIPE_PREFIX):]) for n in names
                      if n.startswith(PIPE_PREFIX) and n[len(PIPE_PREFIX):].isdigit())

    def send(self, pid, request):
        path = f"\\\\.\\pipe\\{PIPE_PREFIX}{pid}"
        deadline = time.monotonic() + CONNECT_TIMEOUT
        while True:                                        # the editor serves one connection at a time
            try:
                f = open(path, "r+b", buffering=0)
                break
            except FileNotFoundError:
                if time.monotonic() > deadline:
                    raise ConnectionError(f"No Animation Editor API on process {pid}. Is the editor open with "
                                          "File > Enable Codex Connection checked?")
                time.sleep(0.05)
            except OSError:                                # busy
                if time.monotonic() > deadline:
                    raise ConnectionError(f"The editor (process {pid}) is busy; try again.")
                time.sleep(0.05)
        box = {}

        def worker():
            try:
                f.write((json.dumps(request, separators=(",", ":")) + "\n").encode("utf-8"))
                buf = bytearray()
                while True:                                # one reply per connection, so reading in blocks is safe
                    chunk = f.read(65536)                  # (image replies run to megabytes)
                    if not chunk:
                        break
                    end = chunk.find(b"\n")
                    if end >= 0:
                        buf += chunk[:end]
                        break
                    buf += chunk
                box["text"] = buf.decode("utf-8")
            except Exception as e:                         # noqa: BLE001
                box["error"] = e
            finally:
                try: f.close()
                except OSError: pass
        t = threading.Thread(target=worker, daemon=True)
        t.start(); t.join(RESPONSE_TIMEOUT)
        if t.is_alive():
            raise Uncertain(f"No response within {RESPONSE_TIMEOUT:.0f} s")
        if "error" in box:
            raise Uncertain(f"Connection broke: {box['error']}")
        if not box.get("text"):
            raise Uncertain("Empty response")
        return json.loads(box["text"])


# ====================================================================== sessions
class Editor:
    def __init__(self, pid):
        self.pid = pid
        self.token = None                 # edit token from the state Claude last read
        self.transport_token = None
        self.library_token = None
        self.next_undo = None
        self.last_state = None

    def remember(self, response):
        st = response.get("state") if isinstance(response, dict) else None
        if isinstance(st, dict):
            if st.get("token"): self.token = st["token"]
            if st.get("transportToken"): self.transport_token = st["transportToken"]
            if st.get("libraryToken"): self.library_token = st["libraryToken"]
            if "nextUndo" in st: self.next_undo = st.get("nextUndo")
            if "commands" in st or "controls" in st: self.last_state = st
        if isinstance(response, dict) and response.get("token") and "urdf" in response:
            self.token = response["token"]              # get_urdf returns its token at the top level


class Hub:
    def __init__(self, transport):
        self.transport = transport
        self.editors = {}
        self.active = None

    def editor(self, pid=None):
        if pid is None:
            pid = self.active
        if pid is None:
            pids = self.transport.list_pids()
            if len(pids) == 1:
                pid = pids[0]
            elif not pids:
                raise ValueError("No Animation Editor with its API enabled was found. Open the editor and check "
                                 "File > Enable Codex Connection.")
            else:
                raise ValueError(f"Several editors are running (processes {pids}). Use list_editors, then "
                                 "select_editor with the one the user means.")
            self.active = pid
        pid = int(pid)
        return self.editors.setdefault(pid, Editor(pid))

    def read(self, method, pid=None, **params):
        ed = self.editor(pid)
        req = {"method": method, **{k: v for k, v in params.items() if v is not None}}
        resp = self.transport.send(ed.pid, req)
        ed.remember(resp)
        return ed, resp

    def mutate(self, method, pid=None, token_kind="edit", **params):
        ed = self.editor(pid)
        if token_kind == "edit":
            if not ed.token:
                return ed, {"ok": False, "error": "Read the editor first (get_state) so this edit is based on the "
                                                  "current timeline."}
            token = ed.token
        elif token_kind == "transport":
            if not ed.transport_token:
                self.read("get_status", ed.pid)
            token = ed.transport_token
        else:
            if not ed.library_token:
                self.read("get_status", ed.pid)
            token = ed.library_token
        req = {"method": method, "requestId": str(uuid.uuid4()), "expectedToken": token,
               **{k: v for k, v in params.items() if v is not None}}
        last_err = None
        for attempt in range(1 + RETRIES_AFTER_TIMEOUT):
            try:
                resp = self.transport.send(ed.pid, req)    # identical request, same requestId, on retry
                break
            except Uncertain as e:
                last_err = e
        else:
            return ed, {"ok": False, "uncertain": True, "requestId": req["requestId"],
                        "error": f"{last_err}. The edit may or may not have been applied. Check with get_request "
                                 f"(requestId {req['requestId']}) before doing anything else; do not repeat it as a "
                                 "new edit."}
        ed.remember(resp)
        if not resp.get("ok", True):
            err = str(resp.get("error", ""))
            if re.search(r"token|stale|changed|context", err, re.I):
                resp["hint"] = ("The editor changed since you last read it (the user may have edited, moved the "
                                "cursor or switched documents). Call get_state, confirm the target with the user "
                                "if it's no longer clear, and only then decide again.")
                ed.token = None                            # force a fresh read before the next edit
        return ed, resp


HUB = Hub(PipeTransport())


# ======================================================================= helpers
def _summary(st):
    if not isinstance(st, dict):
        return st
    keep = ["sequencePath", "moviePath", "description", "cursorSeconds", "durationSeconds", "playback",
            "liveDrive", "unsavedChanges", "nextUndo", "selectedMarkerSeconds", "splineServos", "videoClips"]
    out = {k: st[k] for k in keep if k in st}
    if isinstance(st.get("controls"), list): out["controlCount"] = len(st["controls"])
    if isinstance(st.get("commands"), list): out["commandCount"] = len(st["commands"])
    if isinstance(st.get("movieSequences"), list):
        out["movieSequences"] = [{k: m.get(k) for k in ("index", "path", "description", "durationSeconds") if k in m}
                                 for m in st["movieSequences"]]
    u = st.get("urdf")
    if isinstance(u, dict):
        bg = u.get("background") if isinstance(u.get("background"), dict) else {}
        out["urdf"] = {"modelFile": u.get("modelFile"), "backgroundMode": bg.get("backgroundMode"),
                       "stage": u.get("stage"), "framing": u.get("framing")}
    out["otherFields"] = sorted(k for k in st if k not in keep and k not in
                                ("controls", "commands", "movieSequences", "urdf", "token", "transportToken", "libraryToken"))
    return out


def _result(ed, resp, extra=None):
    out = {"editorPid": ed.pid}
    if isinstance(resp, dict):
        for k, v in resp.items():
            out[k] = _summary(v) if k == "state" else v
    if extra: out.update(extra)
    return out


def _clean_commands(commands):
    clean = []
    for i, c in enumerate(commands or []):
        if not isinstance(c, dict):
            raise ValueError(f"commands[{i}] must be an object")
        bad = set(c) - COMMAND_FIELDS
        if bad:
            raise ValueError(f"commands[{i}] has unsupported fields {sorted(bad)}; allowed: {sorted(COMMAND_FIELDS)}")
        if "servo" not in c or "value" not in c:
            raise ValueError(f"commands[{i}] needs servo and value")
        v = c["value"]
        if isinstance(v, bool) or not isinstance(v, (int, float)) or (isinstance(v, float) and not v.is_integer()):
            raise ValueError(f"commands[{i}].value must be a whole number (got {v!r})")
        a = c.get("afterSeconds", 0)
        if not isinstance(a, (int, float)) or a < 0:
            raise ValueError(f"commands[{i}].afterSeconds must be a number >= 0")
        clean.append({**c, "value": int(v)})
    return clean


def _physical_on(obj, path=""):
    """Find a hardware-drive flag that is switched on anywhere in a status object."""
    hits = []
    if isinstance(obj, dict):
        for k, v in obj.items():
            p = f"{path}.{k}" if path else k
            kl = k.lower()
            if v is True and (("drive" in kl and ("hw" in kl or "hardware" in kl or "physical" in kl))
                              or kl in ("drivehw", "physicaloutput", "hardwaredrive")):
                hits.append(p)
            hits += _physical_on(v, p)
    elif isinstance(obj, list):
        for i, v in enumerate(obj): hits += _physical_on(v, f"{path}[{i}]")
    return hits


def _hardware_section(st):
    if not isinstance(st, dict): return None
    return {k: v for k, v in st.items() if re.search(r"hardware|hw|servo|physical|device", k, re.I)} or None


def _check_physical(flag):
    if flag and not ALLOW_PHYSICAL:
        raise ValueError("Physical output is disabled in this MCP server. The user can allow it by restarting the "
                         "server with J5_ALLOW_PHYSICAL=1. Until then only the on-screen robot is driven.")


def _guard(fn):
    """Turn expected problems (bad input, no editor) into a clear {ok: false, error} result."""
    @functools.wraps(fn)
    def wrapper(*a, **k):
        try:
            return fn(*a, **k)
        except (ValueError, ConnectionError) as e:
            return {"ok": False, "error": str(e)}
    return wrapper


# ======================================================================== server
INSTRUCTIONS = """Drives the Johnny 5 Animation Editor through its local API.
Rules (from AnimationEditor_API_For_AI.md):
1. Call get_state before any edit and look at the sequence, cursor, existing commands, control ranges,
   playback and liveDrive. Edits use the token from the state you last read; if the editor says the context
   changed, read again and reconsider rather than repeating the edit.
2. With several editors open, use list_editors and select_editor; never guess.
3. Pause playback and make sure Live Drive is off before timeline, movie or backdrop edits.
4. Use exact control names and integer values within each control's range (list_controls). Native values are not
   degrees and signs are not physical directions unless the model says so. afterSeconds is relative to the cursor
   in the state you read: to place something earlier, seek there and read again.
5. Never overwrite or remove the user's commands; conflicts reject the whole batch.
6. Edits are not saved: the user saves in the editor.
7. Physical output is off unless the user started this server with J5_ALLOW_PHYSICAL=1.
8. An 'uncertain' result means check get_request before anything else; never re-issue it as a new edit.
Body controls are named B_... (for example B_ElbowLeft); all use native -100..100.
Choreographing to a video behind the robot (Video Clips): list_video_clips shows each clip's timeline span;
get_video_frames shows what plays at timeline times (up to 12 per call, so look over a clip in passes);
seek to a time, then get_view_snapshot shows the robot's current pose over that moment of the video. Plan moves
against those pictures, insert them with insert_commands, then seek and snapshot again to check. Video clips are
added and moved by the user in the editor; the API only reads them."""

server = MCPServer("johnny5-animation-editor", instructions=INSTRUCTIONS)
RO = ToolAnnotations(readOnlyHint=True, idempotentHint=True, openWorldHint=False)
EDIT = ToolAnnotations(readOnlyHint=False, destructiveHint=False, idempotentHint=False, openWorldHint=False)


@server.tool(annotations=RO)
@_guard
def list_editors() -> dict:
    """List running Animation Editors with the API enabled: process ID, version, and open sequence."""
    out = []
    for pid in HUB.transport.list_pids():
        item = {"pid": pid}
        try:
            _, st = HUB.read("get_status", pid)
            s = st.get("state", {}) if isinstance(st, dict) else {}
            item.update({k: s.get(k) for k in ("version", "generated", "date") if k in s})
            _, gs = HUB.read("get_state", pid)
            g = gs.get("state", {}) if isinstance(gs, dict) else {}
            item.update({"sequencePath": g.get("sequencePath"), "moviePath": g.get("moviePath")})
        except Exception as e:                             # noqa: BLE001
            item["error"] = str(e)
        out.append(item)
    return {"ok": True, "editors": out, "active": HUB.active}


@server.tool(annotations=RO)
@_guard
def select_editor(pid: int) -> dict:
    """Choose which running editor (process ID from list_editors) the other tools use."""
    if pid not in HUB.transport.list_pids():
        return {"ok": False, "error": f"No editor API on process {pid}."}
    HUB.active = pid
    ed, resp = HUB.read("get_state", pid)
    return _result(ed, resp)


@server.tool(annotations=RO)
@_guard
def get_state(full: bool = False, pid: int | None = None) -> dict:
    """Read the editor's context. Returns a summary (sequence, cursor, duration, playback, liveDrive, unsaved
    changes, next undo, splines, counts, URDF) unless full=true, which returns everything (can be very large; prefer
    list_controls and list_commands)."""
    ed, resp = HUB.read("get_state", pid)
    if full:
        r = dict(resp); r["editorPid"] = ed.pid
        if isinstance(r.get("state"), dict):
            r["state"] = {k: v for k, v in r["state"].items() if k not in ("token", "transportToken", "libraryToken")}
        return r
    return _result(ed, resp)


@server.tool(annotations=RO)
@_guard
def list_controls(name_contains: str | None = None, pid: int | None = None) -> dict:
    """List controls (exact names, ranges, children, current values) from a fresh state read, optionally filtered by
    a case-insensitive substring such as 'Neck', 'B_', 'Left' or 'URDF'."""
    ed, resp = HUB.read("get_state", pid)
    ctl = (resp.get("state") or {}).get("controls") or []
    if name_contains:
        ctl = [c for c in ctl if name_contains.lower() in str(c.get("name", "")).lower()]
    return {"ok": True, "editorPid": ed.pid, "count": len(ctl), "controls": ctl[:MAX_LIST],
            "truncated": len(ctl) > MAX_LIST}


@server.tool(annotations=RO)
@_guard
def list_commands(start_seconds: float | None = None, end_seconds: float | None = None,
                  servo: str | None = None, pid: int | None = None) -> dict:
    """List timeline commands from a fresh state read, optionally within a time window (seconds) and/or for one
    servo. Note: the API does not expose BreakSpline, group IDs or hidden-group state."""
    ed, resp = HUB.read("get_state", pid)
    cmds = (resp.get("state") or {}).get("commands") or []
    if servo: cmds = [c for c in cmds if c.get("servo") == servo]
    if start_seconds is not None: cmds = [c for c in cmds if c.get("offsetSeconds", 0) >= start_seconds]
    if end_seconds is not None: cmds = [c for c in cmds if c.get("offsetSeconds", 0) <= end_seconds]
    return {"ok": True, "editorPid": ed.pid, "count": len(cmds), "commands": cmds[:MAX_LIST],
            "truncated": len(cmds) > MAX_LIST}


@server.tool(annotations=RO)
@_guard
def get_status(pid: int | None = None) -> dict:
    """Lightweight status: editor version, hardware status, Library playback, capabilities."""
    ed, resp = HUB.read("get_status", pid)
    r = dict(resp); r["editorPid"] = ed.pid
    if isinstance(r.get("state"), dict):
        r["state"] = {k: v for k, v in r["state"].items() if k not in ("transportToken", "libraryToken", "token")}
    return r


@server.tool(annotations=RO)
@_guard
def get_urdf(pid: int | None = None) -> dict:
    """Read the loaded robot model, framing, background preset and evaluated 3D stage."""
    ed, resp = HUB.read("get_urdf", pid)
    r = {k: v for k, v in resp.items() if k != "token"}; r["editorPid"] = ed.pid
    return r


@server.tool(annotations=RO)
@_guard
def get_collisions(pid: int | None = None) -> dict:
    """Read the URDF preview's collision diagnostics (not a physical safety test)."""
    ed, resp = HUB.read("get_collisions", pid)
    return _result(ed, resp)


@server.tool(annotations=RO)
@_guard
def list_library(kind: str, pid: int | None = None) -> dict:
    """List Library items. kind: 'pose' or 'sequence'. Use the exact relative path when names repeat."""
    ed, resp = HUB.read("list_library", pid, libraryKind=kind)
    return _result(ed, resp)


@server.tool(annotations=RO)
def get_library_thumbnail(name: str, pid: int | None = None) -> Image | dict:
    """Show a Library pose's thumbnail image (the 'connected' button artwork)."""
    try:
        ed, resp = HUB.read("get_library_thumbnail", pid, libraryKind="pose", name=name)
    except (ValueError, ConnectionError) as e:
        return {"ok": False, "error": str(e)}
    imgs = resp.get("images") if isinstance(resp, dict) else None
    url = (imgs or {}).get("connected") if isinstance(imgs, dict) else None
    if not url or "," not in url:
        return {"ok": resp.get("ok", False), "error": resp.get("error") or "This pose has no usable image."}
    return Image(data=base64.b64decode(url.split(",", 1)[1]), format="png")


def _image(data_url):
    """An MCP image from the editor's data URL (JPEG or PNG)."""
    head, _, body = data_url.partition(",")
    return Image(data=base64.b64decode(body), format="jpeg" if "jpeg" in head else "png")


@server.tool(annotations=RO)
@_guard
def list_video_clips(pid: int | None = None) -> dict:
    """List the Video Clips behind the robot: timeline start/end seconds, file, Skip to / End at positions in the
    video, fit, volume and whether the file is found. Clips never overlap."""
    ed, resp = HUB.read("get_video_clips", pid)
    return _result(ed, resp)


@server.tool(annotations=RO)
def get_video_frames(start_seconds: float | None = None, end_seconds: float | None = None,
                     interval_seconds: float | None = None, times: list[float] | None = None,
                     max_width: int = 480, pid: int | None = None) -> list[str | Image] | dict:
    """See the Video Clip picture behind the robot at timeline times (seconds), to plan moves to the video.
    Give times (up to 12), or start_seconds and end_seconds with an optional interval_seconds (default: 6 evenly
    spaced frames). Each picture is captioned with its timeline time and the video position. Times with no clip are
    noted. The robot is not drawn; use get_view_snapshot for that."""
    try:
        if times is not None and not 1 <= len(times) <= MAX_VIDEO_FRAMES:
            return {"ok": False, "error": f"Ask for 1 to {MAX_VIDEO_FRAMES} times per call."}
        ed, resp = HUB.read("get_video_frames", pid, times=times, startSeconds=start_seconds, endSeconds=end_seconds,
                            interval=interval_seconds, maxWidth=max_width)
    except (ValueError, ConnectionError) as e:
        return {"ok": False, "error": str(e)}
    if not resp.get("ok"):
        return {"ok": False, "error": resp.get("error", "The editor refused the request.")}
    out = []
    for f in resp.get("frames") or []:
        caption = {k: v for k, v in f.items() if k != "image"}
        out.append(json.dumps(caption))
        if f.get("image"):
            out.append(_image(f["image"]))
    return out or {"ok": True, "frames": []}


@server.tool(annotations=RO)
def get_view_snapshot(max_width: int = 800, pid: int | None = None) -> list[str | Image] | dict:
    """See the robot as it stands at the cursor, drawn over the Video Clip picture at that time (or over the
    backdrop when no clip plays), to compare his pose with the video. Seek first to look at another time."""
    try:
        ed, resp = HUB.read("get_view_snapshot", pid, maxWidth=max_width)
    except (ValueError, ConnectionError) as e:
        return {"ok": False, "error": str(e)}
    if not resp.get("ok") or not resp.get("image"):
        return {"ok": False, "error": resp.get("error", "No snapshot was returned.")}
    caption = {"editorPid": ed.pid, **{k: v for k, v in resp.items() if k not in ("image", "ok")}}
    return [json.dumps(caption), _image(resp["image"])]


@server.tool(annotations=RO)
@_guard
def get_request(request_id: str, pid: int | None = None) -> dict:
    """Read the saved receipt of an earlier operation (to resolve an 'uncertain' result or a picker)."""
    ed, resp = HUB.read("get_request", pid, requestId=request_id)
    return _result(ed, resp)


@server.tool(annotations=EDIT)
@_guard
def insert_commands(commands: list[dict], enable_splines: list[str] | None = None,
                    description: str | None = None, pid: int | None = None) -> dict:
    """Insert timed numeric commands at (cursor + afterSeconds) as one undoable step.
    Each command: {servo, value (integer in range), afterSeconds >= 0, optional control, speed
    (N/C|Default|Slow|Fast|Crawl), reason}. enable_splines lists parent servos whose interpolation to turn on
    (affects existing points too). Conflicts reject the whole batch. Uses the cursor from the state you last read."""
    clean = _clean_commands(commands)
    if not 1 <= len(clean) <= 2000:
        return {"ok": False, "error": "Supply 1 to 2000 commands."}
    ed, resp = HUB.mutate("insert_commands", pid, commands=clean, enableSplines=enable_splines or None,
                          description=description)
    extra = None
    st = resp.get("state") if isinstance(resp, dict) else None
    if resp.get("ok") and isinstance(st, dict):
        cur = st.get("cursorSeconds") or 0.0
        want = {(c["servo"], c.get("control"), round(cur + c.get("afterSeconds", 0), 3)) for c in clean}
        stored = [c for c in st.get("commands", [])
                  if (c.get("servo"), c.get("control"), round(float(c.get("offsetSeconds", -1)), 3)) in want]
        extra = {"insertedAsStored": stored, "cursorSeconds": cur,
                 "check": "Compare these with what you intended (targets, times, values)."}
    return _result(ed, resp, extra)


@server.tool(annotations=ToolAnnotations(readOnlyHint=False, destructiveHint=True, openWorldHint=False))
@_guard
def undo(expected_action: str, pid: int | None = None) -> dict:
    """Undo the editor's latest action. expected_action must match the editor's current nextUndo description
    (read it with get_state first), so the wrong action is never undone."""
    ed, resp = HUB.read("get_state", pid)
    nxt = (resp.get("state") or {}).get("nextUndo")
    if not nxt:
        return {"ok": False, "error": "There is nothing to undo."}
    if expected_action.strip().lower() != str(nxt).strip().lower():
        return {"ok": False, "error": f"The next undo is '{nxt}', not '{expected_action}'. Nothing was undone."}
    ed, resp = HUB.mutate("undo", pid)
    return _result(ed, resp)


@server.tool(annotations=EDIT)
@_guard
def play(pid: int | None = None) -> dict:
    """Play the timeline (fresh play starts at zero; paused play resumes). Requires Live Drive off. Refused while
    the editor reports hardware drive on, unless physical output is allowed."""
    ed, st = HUB.read("get_status", pid)
    hits = _physical_on(st.get("state"))
    if hits and not ALLOW_PHYSICAL:
        return {"ok": False, "error": "The editor reports hardware drive switched on (" + ", ".join(hits) + "), so "
                "playing would move the physical robot. Ask the user to turn Drive HW off, or to restart this server "
                "with J5_ALLOW_PHYSICAL=1.", "hardware": _hardware_section(st.get("state"))}
    ed, resp = HUB.mutate("play", pid, token_kind="transport")
    return _result(ed, resp, {"hardwareStatusChecked": _hardware_section(st.get("state"))})


@server.tool(annotations=EDIT)
@_guard
def pause(pid: int | None = None) -> dict:
    """Pause timeline playback."""
    ed, resp = HUB.mutate("pause", pid, token_kind="transport")
    return _result(ed, resp)


@server.tool(annotations=EDIT)
@_guard
def stop(pid: int | None = None) -> dict:
    """Stop timeline and Library playback. (Playback stop, not a hardware emergency stop.)"""
    ed, resp = HUB.mutate("stop", pid, token_kind="transport")
    return _result(ed, resp)


@server.tool(annotations=EDIT)
@_guard
def seek(seconds: float, pid: int | None = None) -> dict:
    """Move the cursor to an absolute timeline time. Requires Live Drive off. Read state again before editing."""
    ed, resp = HUB.mutate("seek", pid, token_kind="transport", seconds=seconds)
    ed.token = None                                        # the cursor moved: the next edit needs a fresh read
    return _result(ed, resp, {"note": "Call get_state before the next edit (the cursor moved)."})


@server.tool(annotations=EDIT)
@_guard
def set_collision_warnings(enabled: bool, pid: int | None = None) -> dict:
    """Turn the URDF collision warnings on or off."""
    ed, resp = HUB.mutate("set_collision_warnings", pid, token_kind="transport", enabled=enabled)
    return _result(ed, resp)


@server.tool(annotations=EDIT)
@_guard
def insert_library(kind: str, name: str | None = None, pid: int | None = None) -> dict:
    """Insert a Library pose (all commands at the cursor) or sequence (relative offsets kept) as one undo step.
    kind: 'pose' or 'sequence'. Omit name to open the editor's picker for the user; then poll get_request."""
    ed, resp = HUB.mutate("insert_library", pid, libraryKind=kind, name=name)
    return _result(ed, resp)


@server.tool(annotations=EDIT)
@_guard
def apply_library_pose(name: str, drive_physical: bool = False, pid: int | None = None) -> dict:
    """Apply a Library pose immediately (a performance, not a timeline edit). Drives the on-screen robot;
    drive_physical needs the server started with J5_ALLOW_PHYSICAL=1."""
    _check_physical(drive_physical)
    ed, resp = HUB.mutate("apply_library_pose", pid, token_kind="library", name=name,
                          driveUrdf=True, drivePhysical=bool(drive_physical))
    return _result(ed, resp)


@server.tool(annotations=EDIT)
@_guard
def play_library_sequence(name: str, loop: bool = False, drive_physical: bool = False,
                          pid: int | None = None) -> dict:
    """Play a Library sequence now (a performance, not a timeline edit). Drives the on-screen robot;
    drive_physical needs the server started with J5_ALLOW_PHYSICAL=1."""
    _check_physical(drive_physical)
    ed, resp = HUB.mutate("play_library_sequence", pid, token_kind="library", name=name, loop=loop,
                          driveUrdf=True, drivePhysical=bool(drive_physical))
    return _result(ed, resp)


@server.tool(annotations=EDIT)
@_guard
def stop_library(pid: int | None = None) -> dict:
    """Stop Library playback and cancel its picker."""
    ed, resp = HUB.mutate("stop_library", pid, token_kind="library")
    return _result(ed, resp)


@server.tool(annotations=EDIT)
@_guard
def movie_move(index: int, to_index: int, pid: int | None = None) -> dict:
    """Move a movie entry (zero-based) to its final position to_index. Unsaved until Save Movie in the editor."""
    ed, resp = HUB.mutate("movie_move", pid, index=index, toIndex=to_index)
    return _result(ed, resp)


@server.tool(annotations=ToolAnnotations(readOnlyHint=False, destructiveHint=True, openWorldHint=False))
@_guard
def movie_remove(index: int, pid: int | None = None) -> dict:
    """Detach one movie entry (zero-based). Its sequence file is kept. Not on the undo stack."""
    ed, resp = HUB.mutate("movie_remove", pid, index=index)
    return _result(ed, resp)


@server.tool(annotations=EDIT)
@_guard
def movie_insert(sequence_path: str, index: int | None = None, pid: int | None = None) -> dict:
    """Insert an existing sequence file into the movie (absolute or relative to Configuration); appends by default."""
    ed, resp = HUB.mutate("movie_insert", pid, sequencePath=sequence_path, index=index)
    return _result(ed, resp)


@server.tool(annotations=EDIT)
@_guard
def movie_create_sequence(name: str, index: int | None = None, pid: int | None = None) -> dict:
    """Create a blank sequence beside the movie and insert it (never overwrites an existing file)."""
    ed, resp = HUB.mutate("movie_create_sequence", pid, name=name, index=index)
    return _result(ed, resp)


@server.tool(annotations=EDIT)
@_guard
def set_backdrop(backdrop: dict, background_image_source: str | None = None, pid: int | None = None) -> dict:
    """Insert a LoadBackdrop command at the cursor. backdrop = {background: {...complete preset...}, framing: {...}
    (optional)}. The background is a whole preset, not a patch: start from get_urdf's values. Modes: Studio, Color,
    Image, MovingImage, Urdf3D. background_image_source: an absolute image path to import."""
    ed, resp = HUB.mutate("set_backdrop", pid, backdrop=backdrop, backgroundImageSource=background_image_source)
    return _result(ed, resp)


@server.tool(annotations=EDIT)
@_guard
def set_urdf_framing(framing: dict, pid: int | None = None) -> dict:
    """Insert a LoadBackdrop at the cursor with this complete framing (horizontalPercent, verticalPercent,
    spinDegrees, rotateDegrees, yaw, pitch [radians], distance [0.55-11.5 m])."""
    ed, resp = HUB.mutate("set_urdf_framing", pid, framing=framing)
    return _result(ed, resp)


@server.tool(annotations=EDIT)
@_guard
def move_robot(turn_degrees: float = 0.0, forward_meters: float = 0.0, seconds: float = 0.0,
               pid: int | None = None) -> dict:
    """Turn and/or move the robot relative to the 3D stage (needs a Urdf3D backdrop). Positive turn is left. With
    seconds > 0 the move is animated from the cursor. For turn-then-drive, do the turn, seek to its end, read state,
    then drive."""
    ed, resp = HUB.mutate("move_robot", pid, turnDegrees=turn_degrees, forwardMeters=forward_meters, seconds=seconds)
    return _result(ed, resp)


@server.tool(annotations=EDIT)
@_guard
def call_method(method: str, params: dict | None = None, pid: int | None = None) -> dict:
    """Call an API method this server doesn't wrap yet (for newer editor versions). Reads are sent as-is; other
    methods get a requestId and the edit token automatically. Physical output rules still apply."""
    params = dict(params or {})
    if method in BLOCKED_METHODS:
        return {"ok": False, "error": f"{method} is for the Stream Deck plugin only."}
    if (params.get("drivePhysical") or params.get("useEditorOutputs")) and not ALLOW_PHYSICAL:
        _check_physical(True)
    if method in READ_METHODS:
        ed, resp = HUB.read(method, pid, **params)
        return _result(ed, resp)
    kind = "transport" if method in TRANSPORT_METHODS else "library" if method in LIBRARY_METHODS else "edit"
    for k in ("requestId", "expectedToken"): params.pop(k, None)
    ed, resp = HUB.mutate(method, pid, token_kind=kind, **params)
    return _result(ed, resp)


def main():
    if sys.argv[1:] == ["--version"]:
        stamp = Path(__file__).resolve().parents[1] / "ServoAnimator" / "Build" / "PROJECT_VERSION.json"
        print(json.loads(stamp.read_text(encoding="utf-8-sig"))["version"])
        return
    if os.name != "nt":
        print("Note: the Animation Editor API is a Windows named pipe; this server must run on the editor's PC.",
              file=sys.stderr)
    server.run("stdio")


if __name__ == "__main__":
    main()
