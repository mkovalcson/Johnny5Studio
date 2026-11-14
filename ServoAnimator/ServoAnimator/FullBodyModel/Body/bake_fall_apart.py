"""Bake a "fall apart" rigid-body collapse of the Johnny 5 URDF for timeline playback.

Every link with visual geometry becomes a free rigid body, except the head: the
subtree from head_offset_link (the LED mouth up) falls as one piece, and so do the
shoulder laser and toolbox (see TOGETHER). After a short
shudder the parts are released onto the floor and simulated with PyBullet. Each
link's pose in the body_root_link frame is written per frame, so the editor can
play the file forwards (fall apart) or backwards (reassemble).

    py -3.10 bake_fall_apart.py                 # bake + preview
    py -3.10 bake_fall_apart.py --no-preview    # bake only
    py -3.10 bake_fall_apart.py --head-track old.json   # replay the head's path from another clip
    py -3.10 bake_fall_apart.py --head-track none       # let the head fall freely

--head-track keeps a chosen head performance (in the clip this was added for, the head
rolls off to the front right and comes to rest upright, so it can keep talking): the head
piece follows that clip's head path exactly, frame by frame, while every other part is
simulated around it and still collides with it. By default the head replays
fall_apart_head_track.json beside this script.

Needs numpy and pybullet. The model file is only read, never changed.
"""
import argparse, base64, hashlib, json, math, os, struct, sys, time
import xml.etree.ElementTree as ET
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
MODEL_DIR = os.path.dirname(HERE)
HEAD_ROOT = "head_offset_link"          # head_link (LED mouth) and everything above it
# Links that fall as one piece. The laser and toolbox share the shoulder hinge and the editor shows only
# one of them (Toolbox Swap), so they fall together: whichever is visible tumbles naturally, and the
# hidden one never knocks anything about as an invisible separate body.
TOGETHER = [["body_laser_head_link", "body_laser_toolbox_link"]]
FLOOR_Z = -1.0774
FPS = 30
SHUDDER_S = 0.5
FALL_S = 3.0
PHYSICS_HZ = 240
SHELL_M = 0.003                          # large closed parts are treated as 3 mm shells
SEED = 5


# ---------------------------------------------------------------- URDF geometry
def rpy_mat(r, p, y):
    cr, sr, cp, sp, cy, sy = math.cos(r), math.sin(r), math.cos(p), math.sin(p), math.cos(y), math.sin(y)
    return (np.array([[cy, -sy, 0], [sy, cy, 0], [0, 0, 1]]) @
            np.array([[cp, 0, sp], [0, 1, 0], [-sp, 0, cp]]) @
            np.array([[1, 0, 0], [0, cr, -sr], [0, sr, cr]]))


def origin(el):
    o = el.find("origin"); T = np.eye(4)
    if o is not None:
        if o.get("rpy"): T[:3, :3] = rpy_mat(*[float(v) for v in o.get("rpy").split()])
        if o.get("xyz"): T[:3, 3] = [float(v) for v in o.get("xyz").split()]
    return T


def stl_tris(path, scale):
    size = os.path.getsize(path)
    with open(path, "rb") as f:
        head = f.read(84); n = struct.unpack("<I", head[80:84])[0]
        if 84 + n * 50 == size:
            a = np.frombuffer(f.read(n * 50), dtype=np.uint8).reshape(n, 50)
            return a[:, 12:48].copy().view("<f4").reshape(n, 3, 3).astype(float) * scale
    verts = [[float(v) for v in line.split()[1:4]] for line in open(path, errors="ignore") if line.strip().startswith("vertex")]
    return np.array(verts, float).reshape(-1, 3, 3) * scale


def box_tris(sx, sy, sz):
    v = np.array([[x, y, z] for x in (-1, 1) for y in (-1, 1) for z in (-1, 1)]) * np.array([sx, sy, sz]) / 2
    t = []
    for a, b, c, d in [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]:
        t += [[v[a], v[b], v[c]], [v[a], v[c], v[d]]]
    return np.array(t)


def cyl_tris(r, l, seg=20):
    a = np.linspace(0, 2 * math.pi, seg, endpoint=False); t = []
    for i in range(seg):
        p0 = [r * math.cos(a[i]), r * math.sin(a[i])]; p1 = [r * math.cos(a[(i + 1) % seg]), r * math.sin(a[(i + 1) % seg])]
        b0, b1, t0, t1 = [*p0, -l / 2], [*p1, -l / 2], [*p0, l / 2], [*p1, l / 2]
        t += [[b0, b1, t1], [b0, t1, t0], [[0, 0, l / 2], t0, t1], [[0, 0, -l / 2], b1, b0]]
    return np.array(t, float)


def sph_tris(r, seg=10):
    t = []
    for i in range(seg):
        for j in range(seg * 2):
            th0, th1 = math.pi * i / seg, math.pi * (i + 1) / seg
            ph0, ph1 = math.pi * j / seg, math.pi * (j + 1) / seg
            P = lambda th, ph: [r * math.sin(th) * math.cos(ph), r * math.sin(th) * math.sin(ph), r * math.cos(th)]
            t += [[P(th0, ph0), P(th1, ph0), P(th1, ph1)], [P(th0, ph0), P(th1, ph1), P(th0, ph1)]]
    return np.array(t, float)


def density(material):
    m = material.lower()
    if any(k in m for k in ("metal", "chrome", "steel", "alum", "bolt", "edge")): return 2700.0
    if any(k in m for k in ("tread", "rubber", "tire", "belt")): return 1200.0
    if any(k in m for k in ("lens", "glass")): return 2500.0
    return 1150.0                        # ABS / acrylic / delrin-like plastics


def tri_volume_area(t):
    vol = abs(np.einsum("ij,ij->i", t[:, 0], np.cross(t[:, 1], t[:, 2])).sum()) / 6
    area = np.linalg.norm(np.cross(t[:, 1] - t[:, 0], t[:, 2] - t[:, 0]), axis=1).sum() / 2
    return vol, area


class Model:
    def __init__(self, urdf):
        self.urdf, self.base = urdf, os.path.dirname(urdf)
        root = ET.parse(urdf).getroot()
        self.materials = {m.get("name"): [float(v) for v in m.find("color").get("rgba").split()]
                          for m in root.findall("material") if m.find("color") is not None}
        self.joint_by_child = {j.find("child").get("link"): j for j in root.findall("joint")}
        self.children = {}
        for j in root.findall("joint"):
            self.children.setdefault(j.find("parent").get("link"), []).append(j.find("child").get("link"))
        self.link_el = {l.get("name"): l for l in root.findall("link")}
        self._world = {}

    def world(self, link):                   # zero pose, body_root_link frame
        if link not in self._world:
            j = self.joint_by_child.get(link)
            self._world[link] = np.eye(4) if j is None else self.world(j.find("parent").get("link")) @ origin(j)
        return self._world[link]

    def subtree(self, link):
        out, stack = [], [link]
        while stack:
            l = stack.pop(); out.append(l); stack += self.children.get(l, [])
        return out

    def visuals(self, link):
        """Triangles (link frame), rgba, mass for each visual of a link."""
        out = []
        for v in self.link_el[link].findall("visual"):
            g = v.find("geometry")
            if g is None or not len(g): continue
            mat_el = v.find("material"); mat = mat_el.get("name") if mat_el is not None else ""
            rgba = self.materials.get(mat, [0.6, 0.6, 0.6, 1])
            kind = g[0].tag
            if kind == "mesh":
                path = os.path.join(self.base, g[0].get("filename"))
                if not os.path.exists(path): continue
                t = stl_tris(path, np.array([float(s) for s in (g[0].get("scale") or "1 1 1").split()]))
            elif kind == "box": t = box_tris(*[float(s) for s in g[0].get("size").split()])
            elif kind == "cylinder": t = cyl_tris(float(g[0].get("radius")), float(g[0].get("length")))
            elif kind == "sphere": t = sph_tris(float(g[0].get("radius")))
            else: continue
            T = origin(v)
            t = np.einsum("ij,nkj->nki", T[:3, :3], t) + T[:3, 3]
            vol, area = tri_volume_area(t)
            # Closed solids use the smaller of solid and shell mass; open surfaces use the shell.
            mass = density(mat) * (min(vol, area * SHELL_M) if vol > 1e-7 else area * SHELL_M)
            out.append((t, rgba, mass))
        return out


# ---------------------------------------------------------------- pieces
class Piece:
    def __init__(self, name, links, model):
        self.name, self.links = name, links
        self.frame = model.world(links[0])                       # piece frame = its first link's frame
        inv = np.linalg.inv(self.frame)
        self.local = {l: inv @ model.world(l) for l in links}   # link frame relative to piece frame
        pts, self.tris, self.cols, self.mass = [], [], [], 0.0
        for l in links:
            for t, rgba, m in model.visuals(l):
                L = self.local[l]
                t = np.einsum("ij,nkj->nki", L[:3, :3], t) + L[:3, 3]
                self.tris.append(t); self.cols.append(np.tile(rgba[:3], (len(t), 1))); self.mass += m
                pts.append(t.reshape(-1, 3))
        self.has_geometry = bool(pts)
        if not pts: return
        p = np.unique(np.round(np.vstack(pts), 5), axis=0)
        if len(p) > 1500:                                        # keep the extremes, thin the rest
            keep = set(np.argmin(p, 0)) | set(np.argmax(p, 0))
            keep |= set(np.random.default_rng(SEED).choice(len(p), 1500, replace=False))
            p = p[sorted(keep)]
        self.centroid = (p.min(0) + p.max(0)) / 2
        self.hull = p - self.centroid
        self.mass = max(self.mass, 0.002)


def build_pieces(model):
    head = set(model.subtree(HEAD_ROOT))
    together = [[l for l in group if l in model.link_el] for group in TOGETHER]
    together = [group for group in together if group]
    grouped = {l for group in together for l in group}
    order = [l for l in model.link_el if l not in head and l not in grouped]
    pieces = [Piece("head", [HEAD_ROOT] + [l for l in model.subtree(HEAD_ROOT) if l != HEAD_ROOT], model)]
    pieces += [Piece(group[0], group, model) for group in together]
    pieces += [Piece(l, [l], model) for l in order]
    return [p for p in pieces if p.has_geometry]


# ---------------------------------------------------------------- simulation
def quat_from_mat(R):
    m = R.flatten().tolist()
    # pybullet has no matrix->quat helper; use the standard branch-free form
    w = math.sqrt(max(0, 1 + m[0] + m[4] + m[8])) / 2
    x = math.copysign(math.sqrt(max(0, 1 + m[0] - m[4] - m[8])) / 2, m[7] - m[5])
    y = math.copysign(math.sqrt(max(0, 1 - m[0] + m[4] - m[8])) / 2, m[2] - m[6])
    z = math.copysign(math.sqrt(max(0, 1 - m[0] - m[4] + m[8])) / 2, m[3] - m[1])
    q = np.array([x, y, z, w]); return q / np.linalg.norm(q)


def mat_from_quat(q):
    x, y, z, w = q
    return np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                     [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                     [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])


def simulate(pieces, head_track=None):
    import pybullet as pb
    rng = np.random.default_rng(SEED)
    pb.connect(pb.DIRECT)
    pb.setGravity(0, 0, -9.81)
    pb.setPhysicsEngineParameter(fixedTimeStep=1 / PHYSICS_HZ, numSolverIterations=60, enableConeFriction=1)
    plane = pb.createCollisionShape(pb.GEOM_PLANE)
    floor = pb.createMultiBody(0, plane, basePosition=[0, 0, FLOOR_Z])
    pb.changeDynamics(floor, -1, lateralFriction=0.8, restitution=0.1)

    ids = []
    for p in pieces:
        shape = pb.createCollisionShape(pb.GEOM_MESH, vertices=p.hull.tolist())
        R, c = p.frame[:3, :3], p.frame[:3, :3] @ p.centroid + p.frame[:3, 3]
        b = pb.createMultiBody(p.mass, shape, basePosition=c.tolist(), baseOrientation=quat_from_mat(R).tolist())
        pb.changeDynamics(b, -1, lateralFriction=0.7, restitution=0.15, rollingFriction=0.0005, spinningFriction=0.0005,
                          linearDamping=0.05, angularDamping=0.08, collisionMargin=0.0005)
        ids.append(b)

    # Parts that start touching would blow apart; ignore those pairs until they separate.
    pb.performCollisionDetection()
    blocked = {(min(c[1], c[2]), max(c[1], c[2])) for c in pb.getContactPoints() if c[1] != floor and c[2] != floor}
    for a, b in blocked: pb.setCollisionFilterPair(a, b, -1, -1, 0)
    print(f"{len(ids)} pieces, {len(blocked)} initially touching pairs ignored until they separate")

    rest = [pb.getBasePositionAndOrientation(b) for b in ids]
    centre = np.mean([r[0] for r in rest], axis=0)
    n_shudder, n_fall = round(SHUDDER_S * FPS), round(FALL_S * FPS)
    frames = []                                            # per frame: list of (pos, quat) of piece COM
    # Shudder: growing jitter about the rest pose, bodies held still
    phase = rng.uniform(0, 2 * math.pi, (len(ids), 6)); freq = rng.uniform(11, 17, (len(ids), 1))
    for f in range(n_shudder):
        t = f / FPS; env = (f / max(1, n_shudder - 1)) ** 1.5
        frame = []
        for i, (pos, q) in enumerate(rest):
            s = np.sin(2 * math.pi * freq[i] * t + phase[i])
            dp = 0.004 * env * s[:3]
            dq = pb.getQuaternionFromEuler((0.02 * env * s[3:]).tolist())
            frame.append((np.array(pos) + dp, np.array(pb.multiplyTransforms([0, 0, 0], q, [0, 0, 0], dq)[1])))
        if head_track is not None: frame[0] = head_track[f]
        frames.append(frame)
    # Release from the last shudder pose, with a gentle topple: higher parts drift outward
    for i, b in enumerate(ids):
        pos, q = frames[-1][i]
        pb.resetBasePositionAndOrientation(b, pos.tolist(), q.tolist())
        h = max(0.0, pos[2] - FLOOR_Z)
        out = np.array([pos[0] - centre[0], pos[1] - centre[1], 0.0])
        out = out / (np.linalg.norm(out) + 1e-6)
        v = out * (0.25 * h) + rng.normal(0, 0.08 * h, 3) + np.array([0.12 * h, 0, 0])   # slight forward bias
        w = rng.normal(0, 1.2, 3) * min(1.0, 0.3 / max(0.02, p_size(pieces[i])))
        if head_track is None or i != 0: pb.resetBaseVelocity(b, v.tolist(), w.tolist())
    steps = PHYSICS_HZ // FPS
    for f in range(n_fall):
        for k in range(steps):
            if head_track is not None:                     # drive the head along its recorded path
                (p0, q0), (p1, q1) = head_track[n_shudder + f - 1], head_track[n_shudder + f]
                t = (k + 1) / steps
                pb.resetBasePositionAndOrientation(ids[0], (p0 + (p1 - p0) * t).tolist(), slerp(q0, q1, t).tolist())
                dq = pb.getDifferenceQuaternion(q0.tolist(), q1.tolist()); axis, ang = pb.getAxisAngleFromQuaternion(dq)
                pb.resetBaseVelocity(ids[0], ((p1 - p0) * FPS).tolist(), (np.array(axis) * ang * FPS).tolist())
            pb.stepSimulation()
        if f % 3 == 0 and blocked:                         # restore collisions once parts have separated
            for pair in list(blocked):
                if not pb.getClosestPoints(pair[0], pair[1], 0.002):
                    pb.setCollisionFilterPair(pair[0], pair[1], -1, -1, 1); blocked.discard(pair)
        frame = [tuple(np.array(v) for v in pb.getBasePositionAndOrientation(b)) for b in ids]
        if head_track is not None: frame[0] = head_track[n_shudder + f]
        frames.append(frame)
    pb.disconnect()
    return frames, n_shudder


def slerp(q0, q1, t):
    q0, q1 = np.array(q0, float), np.array(q1, float)
    if q0 @ q1 < 0: q1 = -q1
    d = min(1.0, float(q0 @ q1)); a = math.acos(d)
    if a < 1e-6: return q0
    return (math.sin((1 - t) * a) * q0 + math.sin(t * a) * q1) / math.sin(a)


def head_track_from(path, piece, n_frames):
    """Head piece centre-of-mass poses per frame, from an earlier clip's head link poses."""
    doc = json.load(open(path, encoding="utf-8"))
    if doc["frameCount"] != n_frames: raise ValueError("head track clip has a different frame count")
    tracks = {l["link"]: np.frombuffer(base64.b64decode(l["poses"]), "<f4").reshape(n_frames, 7).astype(float) for l in doc["links"]}
    link = next(l for l in piece.links if l in tracks)
    def T(p):
        M = np.eye(4); M[:3, :3] = mat_from_quat(p[3:]); M[:3, 3] = p[:3]; return M
    com_rest = np.eye(4); com_rest[:3, :3] = piece.frame[:3, :3]; com_rest[:3, 3] = piece.frame[:3, :3] @ piece.centroid + piece.frame[:3, 3]
    offset = np.linalg.inv(T(tracks[link][0])) @ com_rest
    out = []
    for p in tracks[link]:
        M = T(p) @ offset; out.append((M[:3, 3], np.array(quat_from_mat(M[:3, :3]))))
    return out


def p_size(piece):
    return float(np.linalg.norm(piece.hull.max(0) - piece.hull.min(0)))


def link_poses(pieces, frames):
    """Per link: frames x [px py pz qx qy qz qw] of the link frame, body_root_link frame."""
    out = {}
    for i, p in enumerate(pieces):
        for l in p.links:
            L = p.local[l]; arr = np.zeros((len(frames), 7), np.float32)
            for f, frame in enumerate(frames):
                c, q = frame[i]; R = mat_from_quat(q)
                piece_origin = c - R @ p.centroid
                arr[f, :3] = piece_origin + R @ L[:3, 3]
                arr[f, 3:] = quat_from_mat(R @ L[:3, :3])
            out[l] = arr
    return out


# ---------------------------------------------------------------- output
def write_manifest(path, model, pieces, poses, n_frames, n_shudder):
    with open(model.urdf, "rb") as f: sha = hashlib.sha256(f.read()).hexdigest()
    index = {l: i for i, p in enumerate(pieces) for l in p.links}
    doc = {
        "version": 1,
        "description": "Johnny 5 falls apart: shudder, then every part collapses onto the floor. Play backwards to reassemble.",
        "urdf": os.path.basename(model.urdf), "urdfSha256": sha,
        "rootLink": "body_root_link", "floorZ_m": FLOOR_Z,
        "fps": FPS, "frameCount": n_frames, "shudderFrames": n_shudder,
        "poseLayout": "Per link, base64 of little-endian float32 [frameCount][px, py, pz, qx, qy, qz, qw]: the link frame in rootLink coordinates. Frame 0 is the zero pose.",
        "pieces": [{"name": p.name, "massKg": round(p.mass, 4), "links": p.links} for p in pieces],
        "links": [{"link": l, "piece": index[l], "poses": base64.b64encode(poses[l].astype("<f4").tobytes()).decode()} for l in poses],
    }
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as f: json.dump(doc, f, separators=(",", ":"))
    print(f"wrote {path} ({os.path.getsize(path) / 1e6:.1f} MB, {len(poses)} links, {n_frames} frames)")


def preview(pieces, frames, n_shudder, out_png, out_gif, budget=40000):
    sys.path.insert(0, HERE)
    from zrender import raster_persp
    from PIL import Image, ImageDraw
    total = sum(len(t) for p in pieces for t in p.tris)
    step = max(1, total // budget)
    tris = [np.concatenate([t[::step] if len(t) > 400 else t for t in p.tris]) for p in pieces]
    cols = [np.concatenate([c[::step] if len(c) > 400 else c for c in p.cols]) for p in pieces]
    # checkerboard floor for a sense of motion
    ft, fc = [], []
    for i in range(-8, 8):
        for j in range(-8, 8):
            x0, y0, s = i * 0.25, j * 0.25, 0.25
            a, b, c, d = [x0, y0, FLOOR_Z], [x0 + s, y0, FLOOR_Z], [x0 + s, y0 + s, FLOOR_Z], [x0, y0 + s, FLOOR_Z]
            shade = 0.34 if (i + j) % 2 else 0.28
            ft += [[a, b, c], [a, c, d]]; fc += [[shade] * 3] * 2
    ft, fc = np.array(ft), np.array(fc)

    def render(f, W, H):
        world = [ft]
        for i, p in enumerate(pieces):
            c, q = frames[f][i]; R = mat_from_quat(q)
            world.append(np.einsum("ij,nkj->nki", R, tris[i] - p.centroid) + c)
        img = raster_persp(np.concatenate(world), np.concatenate([fc] + cols), eye=(2.9, -2.3, 0.35),
                           target=(0.1, 0.1, -0.55), hfov_deg=46, W=W, H=H)
        return Image.fromarray((np.clip(img, 0, 1) * 255).astype(np.uint8)) if not isinstance(img, Image.Image) else img

    picks = [0, n_shudder - 1, n_shudder + 10, n_shudder + 25, n_shudder + 45, len(frames) - 1]
    tiles = []
    for f in picks:
        im = render(f, 480, 360); ImageDraw.Draw(im).text((10, 8), f"frame {f}  t={f / FPS:.2f}s", fill=(230, 230, 230))
        tiles.append(im)
    sheet = Image.new("RGB", (480 * 3, 360 * 2))
    for k, im in enumerate(tiles): sheet.paste(im, ((k % 3) * 480, (k // 3) * 360))
    sheet.save(out_png); print("wrote", out_png)
    gif = [render(f, 360, 270) for f in range(0, len(frames), 2)]
    gif[0].save(out_gif, save_all=True, append_images=gif[1:] + [gif[-1]] * 10, duration=int(2000 / FPS), loop=0)
    print("wrote", out_gif)


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--urdf", default=os.path.join(MODEL_DIR, "johnny5_head.urdf"))
    ap.add_argument("--out", default=os.path.join(MODEL_DIR, "Meshes", "Body", "FallApart", "fall_apart.json"))
    ap.add_argument("--no-preview", action="store_true")
    ap.add_argument("--head-track", default=os.path.join(HERE, "fall_apart_head_track.json"),
                    help="clip (or head track file) whose head path the head replays; 'none' to let it fall freely")
    a = ap.parse_args()
    t0 = time.time()
    model = Model(a.urdf)
    pieces = build_pieces(model)
    lowest = min((p.frame[:3, :3] @ (p.hull + p.centroid).T).T.min(0)[2] + p.frame[2, 3] for p in pieces)
    print(f"{len(pieces)} pieces, total mass {sum(p.mass for p in pieces):.1f} kg, head {pieces[0].mass:.2f} kg "
          f"({len(pieces[0].links)} links), lowest point z={lowest:.4f} m (floor {FLOOR_Z})")
    head_track = None
    if a.head_track and a.head_track.lower() != "none" and os.path.exists(a.head_track):
        head_track = head_track_from(a.head_track, pieces[0], round(SHUDDER_S * FPS) + round(FALL_S * FPS))
        print("head replays its path from", a.head_track)
    frames, n_shudder = simulate(pieces, head_track)
    poses = link_poses(pieces, frames)
    write_manifest(a.out, model, pieces, poses, len(frames), n_shudder)
    if not a.no_preview:
        preview(pieces, frames, n_shudder, os.path.join(HERE, "render_fall_apart.png"), os.path.join(HERE, "fall_apart_preview.gif"))
    print(f"done in {time.time() - t0:.0f} s")


if __name__ == "__main__":
    main()
