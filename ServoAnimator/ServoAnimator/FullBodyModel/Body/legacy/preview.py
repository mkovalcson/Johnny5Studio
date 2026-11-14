"""Project the torso parts to front and side views so proportions can be checked."""
import math
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.patches import Polygon
import build_torso as bt


def rot(rpy):
    r, p, y = rpy
    cr, sr = math.cos(r), math.sin(r)
    cp, sp = math.cos(p), math.sin(p)
    cy, sy = math.cos(y), math.sin(y)
    Rx = [[1, 0, 0], [0, cr, -sr], [0, sr, cr]]
    Ry = [[cp, 0, sp], [0, 1, 0], [-sp, 0, cp]]
    Rz = [[cy, -sy, 0], [sy, cy, 0], [0, 0, 1]]
    def mul(A, B):
        return [[sum(A[i][k] * B[k][j] for k in range(3)) for j in range(3)] for i in range(3)]
    return mul(Rz, mul(Ry, Rx))


def corners(geom):
    if geom[0] == "box":
        hx, hy, hz = (v / 2 for v in geom[1])
    else:
        hx = hy = geom[1]
        hz = geom[2] / 2
    return [(sx * hx, sy * hy, sz * hz)
            for sx in (-1, 1) for sy in (-1, 1) for sz in (-1, 1)]


def hull_2d(pts):
    pts = sorted(set(pts))
    if len(pts) <= 2:
        return pts
    def half(points):
        stack = []
        for p in points:
            while len(stack) >= 2:
                (ox, oy), (ax, ay) = stack[-2], stack[-1]
                if (ax - ox) * (p[1] - oy) - (ay - oy) * (p[0] - ox) <= 0:
                    stack.pop()
                else:
                    break
            stack.append(p)
        return stack
    return half(pts)[:-1] + half(pts[::-1])[:-1]


HEAD = "/home/claude/AnimationEditor-Source-2026-09-21/ServoAnimator/Models/johnny5_head.urdf"
import xml.etree.ElementTree as ET
PALETTE = {m.get("name"): m.find("color").get("rgba")
           for m in ET.parse(HEAD).getroot().findall("material")}

fig, axes = plt.subplots(1, 2, figsize=(13, 9), facecolor="#101215")
front, side = axes

for ax, title in ((front, "Front view (Y left, Z up)"), (side, "Side view (X forward, Z up)")):
    ax.set_facecolor("#101215")
    ax.set_title(title, color="#dddddd")
    ax.set_aspect("equal")
    for s in ax.spines.values():
        s.set_color("#444")
    ax.tick_params(colors="#888")

for name, xyz, rpy, geom, mat in bt.PARTS:
    R = rot(rpy)
    ox = bt.NX + xyz[0] + bt.TORSO_DX
    oy = bt.NY + xyz[1] + bt.TORSO_DY
    oz = bt.NZ + xyz[2] + bt.TORSO_DZ
    world = []
    for c in corners(geom):
        wx = R[0][0]*c[0] + R[0][1]*c[1] + R[0][2]*c[2] + ox
        wy = R[1][0]*c[0] + R[1][1]*c[1] + R[1][2]*c[2] + oy
        wz = R[2][0]*c[0] + R[2][1]*c[1] + R[2][2]*c[2] + oz
        world.append((wx, wy, wz))
    color = PALETTE[bt.HEAD_MATERIALS[mat]].split()[:3]
    rgb = tuple(float(v) for v in color)
    for ax, proj in ((front, lambda p: (p[1], p[2])), (side, lambda p: (p[0], p[2]))):
        pts = hull_2d([proj(p) for p in world])
        ax.add_patch(Polygon(pts, closed=True, facecolor=rgb, edgecolor="#000", linewidth=0.6, alpha=0.95))

# real neck geometry, projected from the STL, so contact can be checked
import struct, numpy as np, math as _m
def _stl(path, scale=0.001):
    with open(path,'rb') as f:
        h=f.read(84); n=struct.unpack('<I',h[80:84])[0]; d=f.read(n*50)
    a=np.frombuffer(d,dtype=np.uint8).reshape(n,50)
    return a[:,12:48].copy().view('<f4').reshape(n,3,3).astype(float).reshape(-1,3)*scale
def _rot(r,p,y):
    cr,sr=_m.cos(r),_m.sin(r); cp,sp=_m.cos(p),_m.sin(p); cy,sy=_m.cos(y),_m.sin(y)
    return np.array([[cy,-sy,0],[sy,cy,0],[0,0,1]])@np.array([[cp,0,sp],[0,1,0],[-sp,0,cp]])@np.array([[1,0,0],[0,cr,-sr],[0,sr,cr]])
_R=_rot(1.570796,0,1.570796); _o=np.array([-4.7e-8,-8.4e-8,-0.045138975]); _j=np.array([0.123658047,8.4e-8,0.134447975])
MD='/home/claude/pkg/Models/Meshes/NeckAssembly/StepColors/'
for part,col in (('black','#e05c5c'),('cad_97a3da','#6f7ac0')):
    W=(_R@_stl(MD+f'neck_static_{part}.stl').T).T+_o+_j
    step=max(1,len(W)//4000)
    front.scatter(W[::step,1],W[::step,2],s=0.4,c=col,alpha=0.5,linewidths=0)
    side.scatter(W[::step,0],W[::step,2],s=0.4,c=col,alpha=0.5,linewidths=0)
for ax in (front,side):
    ax.axhline(0.134050, color='#e0c05c', linewidth=0.8, linestyle='--')

for ax in axes:
    ax.axhline(0, color="#333", linewidth=0.8)
    ax.axvline(0, color="#333", linewidth=0.8)
front.set_xlim(0.45, -0.45)
front.set_ylim(-0.50, 0.30)
side.set_xlim(-0.35, 0.45)
side.set_ylim(-0.50, 0.30)

plt.tight_layout()
plt.savefig("/home/claude/torso/preview.png", dpi=110, facecolor="#101215")
print("ok")
