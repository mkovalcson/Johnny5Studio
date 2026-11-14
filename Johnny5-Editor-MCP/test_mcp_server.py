"""End-to-end tests: an MCP client calls the server's tools; the server talks to a
simulated Animation Editor that follows the documented protocol."""
import asyncio, base64, copy, json, sys, uuid
import os; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import johnny5_editor_mcp as srv
from mcp.client import Client

PNG = base64.b64encode(bytes.fromhex("89504e470d0a1a0a0000000d4948445200000001000000010806000000"
                                     "1f15c4890000000d49444154789c6360000002000154a24f5d0000000049454e44ae426082")).decode()


class MockEditor:
    def __init__(self, pid):
        self.pid = pid; self.cursor = 5.0; self.commands = []; self.undo = []; self.receipts = {}
        self.token_n = 1; self.playback = "Stopped"; self.live = False; self.drive_hw = False
        self.library_log = []; self.flaky_once = set()
        self.controls = [{"name": "NeckNodUp", "min": -100, "max": 100, "children": [], "currentValue": 0},
                         {"name": "B_ElbowLeft", "min": -100, "max": 100, "children": [], "currentValue": 0}]
    token = property(lambda s: f"T{s.pid}-{s.token_n}")
    def bump(self): self.token_n += 1
    def state(self):
        return {"sequencePath": "X:/seq/test.json", "moviePath": None, "description": "test", "cursorSeconds": self.cursor,
                "durationSeconds": 10.0, "playback": self.playback, "liveDrive": self.live, "unsavedChanges": bool(self.undo),
                "nextUndo": self.undo[-1][0] if self.undo else None, "splineServos": [], "controls": copy.deepcopy(self.controls),
                "commands": copy.deepcopy(self.commands), "movieSequences": [], "token": self.token,
                "transportToken": f"TR{self.pid}", "libraryToken": f"LB{self.pid}",
                "urdf": {"modelFile": "johnny5_head.urdf", "background": {"backgroundMode": "Studio"}, "framing": {}, "stage": None}}
    def handle(self, r):
        m = r["method"]
        if m == "get_state": return {"ok": True, "state": self.state()}
        if m == "get_status": return {"ok": True, "state": {"version": "9.9", "processId": self.pid, "transportToken": f"TR{self.pid}",
                                                            "libraryToken": f"LB{self.pid}", "hardware": {"driveHw": self.drive_hw, "connected": True}}}
        if m == "get_collisions": return {"ok": True, "collisions": {"enabled": True, "pairs": []}}
        if m == "get_library_thumbnail": return {"ok": True, "images": {"connected": "data:image/png;base64," + PNG}}
        if m == "get_video_clips": return {"ok": True, "videoClips": [{"index": 0, "file": "take.mp4", "startSeconds": 2.0, "endSeconds": 8.0}]}
        if m == "get_video_frames":
            times = r.get("times") or [r["startSeconds"], r["endSeconds"]]
            return {"ok": True, "frames": [{"timelineSeconds": t, "file": "take.mp4", "videoSeconds": t - 2, "image": "data:image/jpeg;base64," + PNG}
                                           if 2 <= t < 8 else {"timelineSeconds": t, "image": None, "note": "No video clip plays at this time."}
                                           for t in times]}
        if m == "get_view_snapshot": return {"ok": True, "cursorSeconds": self.cursor, "videoClip": {"clipIndex": 0, "file": "take.mp4"},
                                             "image": "data:image/jpeg;base64," + PNG}
        if m == "get_request": return self.receipts.get(r["requestId"], ({}, {"ok": False, "error": "Unknown requestId."}))[1]
        rid = r.get("requestId"); payload = json.dumps(r, sort_keys=True)
        if rid in self.receipts:
            if self.receipts[rid][0] != payload: return {"ok": False, "error": "requestId was already used for a different request."}
            return self.receipts[rid][1]
        res = self.mutate(m, r)
        self.receipts[rid] = (payload, res)
        return res
    def mutate(self, m, r):
        tok = r.get("expectedToken")
        if m in ("play", "pause", "stop", "seek", "set_collision_warnings"):
            if tok != f"TR{self.pid}": return {"ok": False, "error": "Stale transport token."}
            if m == "seek": self.cursor = float(r["seconds"]); self.bump()
            if m == "play": self.playback = "Playing"
            if m in ("pause",): self.playback = "Paused"
            if m == "stop": self.playback = "Stopped"
            return {"ok": True, "requestId": r["requestId"], "state": self.state()}
        if m in ("apply_library_pose", "play_library_sequence", "stop_library"):
            if tok != f"LB{self.pid}": return {"ok": False, "error": "Stale library token."}
            self.library_log.append((m, r.get("name"), r.get("drivePhysical")))
            return {"ok": True, "requestId": r["requestId"], "status": "completed"}
        if tok != self.token: return {"ok": False, "error": "Stale context: the editor changed since the token was read."}
        if m == "insert_commands":
            new = []
            for c in r["commands"]:
                ctl = next((x for x in self.controls if x["name"] == c["servo"]), None)
                if not ctl: return {"ok": False, "error": "Use a numeric servo name from get_state.controls."}
                if not ctl["min"] <= c["value"] <= ctl["max"]: return {"ok": False, "error": f"{c['servo']} requires a value from {ctl['min']} to {ctl['max']}."}
                t = round(self.cursor + c.get("afterSeconds", 0), 3)
                new.append({"servo": c["servo"], "control": c.get("control"), "offsetSeconds": t, "value": c["value"], "speed": c.get("speed", "N/C")})
            keys = [(c["servo"], c["control"], c["offsetSeconds"]) for c in self.commands + new]
            if len(keys) != len(set(keys)): return {"ok": False, "error": "Command conflict: a target already has a command at that millisecond. Nothing was changed."}
            self.commands += new; self.undo.append((r.get("description") or "Insert commands", new)); self.bump()
            return {"ok": True, "requestId": r["requestId"], "inserted": len(new), "state": self.state()}
        if m == "undo":
            if not self.undo: return {"ok": False, "error": "Nothing to undo."}
            _, batch = self.undo.pop(); self.commands = [c for c in self.commands if c not in batch]; self.bump()
            return {"ok": True, "requestId": r["requestId"], "state": self.state()}
        return {"ok": True, "requestId": r["requestId"], "state": self.state()}


class MockTransport:
    def __init__(self, editors): self.eds = {e.pid: e for e in editors}; self.sent = []
    def list_pids(self): return sorted(self.eds)
    def send(self, pid, request):
        self.sent.append(copy.deepcopy(request))
        ed = self.eds[pid]
        resp = ed.handle(copy.deepcopy(request))
        if request.get("requestId") in ed.flaky_once:       # applied, but the reply is lost
            ed.flaky_once.discard(request["requestId"]); raise srv.Uncertain("No response within 35 s")
        return resp


def data(res):
    if getattr(res, "structured_content", None) is not None:
        sc = res.structured_content
        return sc.get("result", sc) if isinstance(sc, dict) and set(sc) == {"result"} else sc
    for c in res.content:
        if getattr(c, "type", "") == "text":
            try: return json.loads(c.text)
            except Exception: return c.text
    return res.content


async def main():
    ed = MockEditor(111)
    T = MockTransport([ed]); srv.HUB = srv.Hub(T)
    results = []
    def check(name, cond): results.append((name, bool(cond)))
    async with Client(srv.server) as c:
        tools = {t.name: t for t in (await c.list_tools()).tools}
        check(f"{len(tools)} tools listed, including the core ones",
              {"get_state", "insert_commands", "undo", "play", "seek", "move_robot", "set_backdrop"} <= set(tools))
        props = tools["insert_commands"].input_schema.get("properties", {}) if hasattr(tools["insert_commands"], "input_schema") else tools["insert_commands"].inputSchema["properties"]
        check("insert_commands schema exposes commands / enable_splines / description", {"commands", "enable_splines", "description"} <= set(props))
        check("read-only tools are marked read-only", tools["get_state"].annotations.read_only_hint is True if hasattr(tools["get_state"].annotations, "read_only_hint") else tools["get_state"].annotations.readOnlyHint)

        nod = [{"servo": "NeckNodUp", "afterSeconds": 0, "value": 0}, {"servo": "NeckNodUp", "afterSeconds": 2, "value": -50},
               {"servo": "NeckNodUp", "afterSeconds": 4, "value": 0}]
        r = data(await c.call_tool("insert_commands", {"commands": nod}))
        check("editing before reading state is refused", r.get("ok") is False and "get_state" in r.get("error", ""))

        s = data(await c.call_tool("get_state", {}))
        check("get_state summary (cursor, counts, no tokens leaked)", s["state"]["cursorSeconds"] == 5.0 and "token" not in json.dumps(s["state"]) .lower().replace("transport", ""))
        r = data(await c.call_tool("insert_commands", {"commands": nod, "enable_splines": ["NeckNodUp"], "description": "Head nod"}))
        check("insert works and reports what was stored at cursor+afterSeconds",
              r.get("ok") and [x["offsetSeconds"] for x in r["insertedAsStored"]] == [5.0, 7.0, 9.0])
        sent = T.sent[-1]
        check("request carried a GUID requestId and the token from the read", uuid.UUID(sent["requestId"]) and sent["expectedToken"] == "T111-1")

        ed.cursor = 8.0; ed.bump()                                  # the user moves the cursor in the UI
        r = data(await c.call_tool("insert_commands", {"commands": [{"servo": "B_ElbowLeft", "value": 30}]}))
        check("stale view is rejected with advice to re-read", r.get("ok") is False and "get_state" in r.get("hint", ""))
        r = data(await c.call_tool("insert_commands", {"commands": [{"servo": "B_ElbowLeft", "value": 30}]}))
        check("...and the next edit requires a fresh read", r.get("ok") is False and "Read the editor first" in r.get("error", ""))

        await c.call_tool("get_state", {})
        before = len(ed.commands)
        ed.flaky_once = {"*"}
        orig = T.send
        def flaky_send(pid, request):
            if request.get("method") == "insert_commands" and "*" in ed.flaky_once:
                ed.flaky_once = {request["requestId"]}
            return orig(pid, request)
        T.send = flaky_send
        r = data(await c.call_tool("insert_commands", {"commands": [{"servo": "B_ElbowLeft", "value": 30}], "description": "Elbow"}))
        T.send = orig
        ids = [x["requestId"] for x in T.sent if x.get("method") == "insert_commands"][-2:]
        check("lost reply: identical retry with the same requestId, applied once", r.get("ok") and ids[0] == ids[1] and len(ed.commands) == before + 1)

        r = data(await c.call_tool("insert_commands", {"commands": [{"servo": "B_ElbowLeft", "value": 12.5}]}))
        check("non-integer value refused before sending", r.get("ok") is False and "whole number" in r["error"])
        r = data(await c.call_tool("insert_commands", {"commands": [{"servo": "B_ElbowLeft", "value": 5, "angle": 3}]}))
        check("unknown command field refused", r.get("ok") is False and "unsupported fields" in r["error"])

        r = data(await c.call_tool("undo", {"expected_action": "Head nod"}))
        check("undo refused when it names the wrong action", r.get("ok") is False and "Elbow" in r["error"])
        r = data(await c.call_tool("undo", {"expected_action": "Elbow"}))
        check("undo works when the action matches", r.get("ok") and len(ed.commands) == before)

        r = data(await c.call_tool("apply_library_pose", {"name": "Wave", "drive_physical": True}))
        check("physical Library output refused by default", r.get("ok") is False and "J5_ALLOW_PHYSICAL" in r["error"])
        r = data(await c.call_tool("apply_library_pose", {"name": "Wave"}))
        check("on-screen Library pose works, drivePhysical false", r.get("ok") and ed.library_log[-1] == ("apply_library_pose", "Wave", False))
        ed.drive_hw = True
        r = data(await c.call_tool("play", {}))
        check("play refused while the editor reports Drive HW on", r.get("ok") is False and "Drive HW" in r["error"] and ed.playback == "Stopped")
        ed.drive_hw = False
        r = data(await c.call_tool("play", {}))
        check("play works with Drive HW off", r.get("ok") and ed.playback == "Playing")
        r = data(await c.call_tool("seek", {"seconds": 1.5}))
        r2 = data(await c.call_tool("insert_commands", {"commands": [{"servo": "B_ElbowLeft", "value": 10}]}))
        check("after seek, the next edit requires a fresh read", r.get("ok") and r2.get("ok") is False)

        res = await c.call_tool("get_library_thumbnail", {"name": "Wave"})
        check("thumbnail comes back as an image", any(getattr(x, "type", "") == "image" for x in res.content))

        r = data(await c.call_tool("list_video_clips", {}))
        check("list_video_clips reports clip spans", r.get("ok") and r["videoClips"][0]["endSeconds"] == 8.0)
        res = await c.call_tool("get_video_frames", {"times": [1.0, 3.0, 5.0]})
        kinds = [getattr(x, "type", "") for x in res.content]
        check("get_video_frames: a caption per time, an image per frame inside a clip", kinds == ["text", "text", "image", "text", "image"]
              and [x.mime_type if hasattr(x, "mime_type") else x.mimeType for x in res.content if x.type == "image"] == ["image/jpeg"] * 2)
        sent = T.sent[-1]
        check("frames request carries times and the default width", sent["method"] == "get_video_frames" and sent["times"] == [1.0, 3.0, 5.0] and sent["maxWidth"] == 480)
        r = data(await c.call_tool("get_video_frames", {"times": [float(i) for i in range(13)]}))
        check("more than 12 frames is refused before sending", r.get("ok") is False and "12" in r["error"])
        await c.call_tool("get_video_frames", {"start_seconds": 2, "end_seconds": 7, "interval_seconds": 1})
        check("a range is passed as start/end/interval",
              (T.sent[-1]["startSeconds"], T.sent[-1]["endSeconds"], T.sent[-1]["interval"]) == (2, 7, 1) and "times" not in T.sent[-1])
        res = await c.call_tool("get_view_snapshot", {})
        check("get_view_snapshot returns a caption and the composite image",
              [getattr(x, "type", "") for x in res.content] == ["text", "image"] and "take.mp4" in res.content[0].text)
        check("video tools are read-only", all((tools[n].annotations.read_only_hint if hasattr(tools[n].annotations, "read_only_hint") else tools[n].annotations.readOnlyHint)
                                              for n in ("list_video_clips", "get_video_frames", "get_view_snapshot")))
        r = data(await c.call_tool("call_method", {"method": "stream_deck_status"}))
        check("Stream Deck presence can't be impersonated", r.get("ok") is False)
        r = data(await c.call_tool("call_method", {"method": "get_collisions"}))
        check("call_method passes reads through", r.get("ok") and "collisions" in r)

    # several editors: no guessing
    e1, e2 = MockEditor(111), MockEditor(222)
    srv.HUB = srv.Hub(MockTransport([e1, e2]))
    async with Client(srv.server) as c:
        r = data(await c.call_tool("get_state", {}))
        check("with two editors open, an unspecified target is refused", r.get("ok") is False and "select_editor" in r["error"])
        r = data(await c.call_tool("list_editors", {}))
        check("list_editors shows both", [x["pid"] for x in r["editors"]] == [111, 222])
        r = data(await c.call_tool("select_editor", {"pid": 222}))
        s = data(await c.call_tool("get_state", {}))
        check("select_editor then reads the chosen one", s.get("editorPid") == 222)

    w = max(len(n) for n, _ in results)
    for n, ok in results: print(("PASS " if ok else "FAIL ") + n)
    print(f"\n{sum(ok for _, ok in results)}/{len(results)} passed")

asyncio.run(main())
