"""Shaded preview of a URDF at zero pose: forward kinematics over the joint tree,
STL meshes (decimated) plus box/cylinder/sphere primitives, flat-shaded."""
import sys, math, struct, os
import numpy as np
import xml.etree.ElementTree as ET
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from mpl_toolkits.mplot3d.art3d import Poly3DCollection


def rpy_mat(r, p, y):
    cr, sr = math.cos(r), math.sin(r); cp, sp = math.cos(p), math.sin(p); cy, sy = math.cos(y), math.sin(y)
    return (np.array([[cy, -sy, 0], [sy, cy, 0], [0, 0, 1]]) @
            np.array([[cp, 0, sp], [0, 1, 0], [-sp, 0, cp]]) @
            np.array([[1, 0, 0], [0, cr, -sr], [0, sr, cr]]))


def origin(el):
    o = el.find("origin")
    xyz = np.zeros(3); rpy = (0, 0, 0)
    if o is not None:
        if o.get("xyz"): xyz = np.array([float(v) for v in o.get("xyz").split()])
        if o.get("rpy"): rpy = tuple(float(v) for v in o.get("rpy").split())
    T = np.eye(4); T[:3, :3] = rpy_mat(*rpy); T[:3, 3] = xyz
    return T


def stl_tris(path, scale):
    size = os.path.getsize(path)
    with open(path, "rb") as f:
        head = f.read(84)
        n = struct.unpack("<I", head[80:84])[0]
        if 84 + n * 50 == size:                      # binary STL
            d = f.read(n * 50)
            a = np.frombuffer(d, dtype=np.uint8).reshape(n, 50)
            return a[:, 12:48].copy().view("<f4").reshape(n, 3, 3).astype(float) * scale
    # ASCII STL
    verts = [tuple(float(v) for v in line.split()[1:4])
             for line in open(path, errors="ignore") if line.strip().startswith("vertex")]
    return np.array(verts, dtype=float).reshape(-1, 3, 3) * scale


def box_tris(sx, sy, sz):
    h = np.array([sx, sy, sz]) / 2
    v = np.array([[x, y, z] for x in (-1, 1) for y in (-1, 1) for z in (-1, 1)]) * h
    quads = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
    t = []
    for a, b, c, d in quads:
        t += [[v[a], v[b], v[c]], [v[a], v[c], v[d]]]
    return np.array(t)


def cyl_tris(r, l, seg=20):
    ang = np.linspace(0, 2 * math.pi, seg + 1)
    top = [(r * math.cos(a), r * math.sin(a), l / 2) for a in ang]
    bot = [(r * math.cos(a), r * math.sin(a), -l / 2) for a in ang]
    t = []
    for i in range(seg):
        t += [[bot[i], bot[i + 1], top[i + 1]], [bot[i], top[i + 1], top[i]],
              [(0, 0, l / 2), top[i], top[i + 1]], [(0, 0, -l / 2), bot[i + 1], bot[i]]]
    return np.array(t)


def sph_tris(r, seg=10):
    t = []
    for i in range(seg):
        for j in range(seg * 2):
            th0, th1 = math.pi * i / seg, math.pi * (i + 1) / seg
            ph0, ph1 = 2 * math.pi * j / (2 * seg), 2 * math.pi * (j + 1) / (2 * seg)
            p = lambda th, ph: (r * math.sin(th) * math.cos(ph), r * math.sin(th) * math.sin(ph), r * math.cos(th))
            t += [[p(th0, ph0), p(th1, ph0), p(th1, ph1)], [p(th0, ph0), p(th1, ph1), p(th0, ph1)]]
    return np.array(t)


def load(urdf, mesh_budget=45000):
    base = os.path.dirname(urdf)
    root = ET.parse(urdf).getroot()
    mats = {m.get("name"): [float(v) for v in m.find("color").get("rgba").split()]
            for m in root.findall("material") if m.find("color") is not None}
    joints = {j.find("child").get("link"): j for j in root.findall("joint")}
    cache = {}

    def world(link):
        if link in cache: return cache[link]
        j = joints.get(link)
        T = np.eye(4) if j is None else world(j.find("parent").get("link")) @ origin(j)
        cache[link] = T
        return T

    # count mesh triangles first so decimation keeps the total under budget
    mesh_items, prim_items = [], []
    for l in root.findall("link"):
        for v in l.findall("visual"):
            g = v.find("geometry")
            if g is None: continue
            m = v.find("material")
            rgba = mats.get(m.get("name"), [0.6, 0.6, 0.6, 1]) if m is not None else [0.6, 0.6, 0.6, 1]
            T = world(l.get("name")) @ origin(v)
            if g.find("mesh") is not None:
                me = g.find("mesh")
                sc = np.array([float(s) for s in (me.get("scale") or "1 1 1").split()])
                p = os.path.join(base, me.get("filename"))
                if os.path.exists(p):
                    mesh_items.append((p, sc, T, rgba))
            else:
                prim_items.append((g, T, rgba))

    total = sum(max(0, (os.path.getsize(p) - 84) // 50) for p, *_ in mesh_items)
    step = max(1, total // mesh_budget)
    tris, cols = [], []
    for p, sc, T, rgba in mesh_items:
        t = stl_tris(p, sc)
        if len(t) > 2000:                    # thin only the large CAD meshes
            t = t[::step]
        tris.append(np.einsum("ij,nkj->nki", T[:3, :3], t) + T[:3, 3]); cols += [rgba] * len(t)
    for g, T, rgba in prim_items:
        if g.find("box") is not None:
            t = box_tris(*[float(v) for v in g.find("box").get("size").split()])
        elif g.find("cylinder") is not None:
            c = g.find("cylinder"); t = cyl_tris(float(c.get("radius")), float(c.get("length")))
        elif g.find("sphere") is not None:
            t = sph_tris(float(g.find("sphere").get("radius")))
        else:
            continue
        tris.append(np.einsum("ij,nkj->nki", T[:3, :3], t) + T[:3, 3]); cols += [rgba] * len(t)
    return np.vstack(tris), np.array(cols), step


def render(urdf, out, views=((0, 8, "Front"), (-35, 12, "Three-quarter"), (-90, 5, "Side"))):
    tris, cols, step = load(urdf)
    light = np.array([0.55, 0.35, 0.75]); light /= np.linalg.norm(light)
    n = np.cross(tris[:, 1] - tris[:, 0], tris[:, 2] - tris[:, 0])
    nl = np.linalg.norm(n, axis=1); nl[nl == 0] = 1; n /= nl[:, None]
    shade = 0.38 + 0.62 * np.abs(n @ light)
    fc = np.clip(cols[:, :3] * shade[:, None], 0, 1)
    fig = plt.figure(figsize=(18, 11), facecolor="#15171b")
    for i, (az, el, title) in enumerate(views):
        ax = fig.add_subplot(1, len(views), i + 1, projection="3d")
        ax.set_facecolor("#15171b")
        pc = Poly3DCollection(tris, facecolors=fc, edgecolors="none", linewidths=0)
        ax.add_collection3d(pc)
        ax.set_xlim(-0.55, 0.55); ax.set_ylim(-0.55, 0.55); ax.set_zlim(-1.21, 0.42)
        ax.set_box_aspect((1.1, 1.1, 1.63))
        ax.view_init(elev=el, azim=az)
        ax.set_axis_off()
        ax.set_title(title, color="#ddd")
    plt.subplots_adjust(0, 0, 1, 0.96, 0, 0)
    plt.savefig(out, dpi=80, facecolor="#15171b")
    return len(tris), step


if __name__ == "__main__":
    n, step = render(sys.argv[1], sys.argv[2])
    print(f"{n} triangles rendered (head meshes decimated 1:{step})")
