"""Proposal render v2: CAD four-bar caster linkage + 61.3° track rotation + tilt-back onto the caster.

Model frame: +X forward, +Z up, metres. CAD scale 962 px/m, CAD view-1 track hub (927,497) = model pelvis drum.
Linkage (fitted to CAD view 1 rest and view 3 tiptoe, within 2.4 mm):
  A  upper brace body hinge (grey circle on the hinge housing)      C  lower brace body hinge
  B  upper brace head hinge: centre of the 45° chamfer, over the column axis
  D  lower brace head hinge: collar ring between the bellows
The upper brace and the swivel top housing are ONE rigid piece hinged at A and B. A-B-D-C is a four-bar
(braces 202 / 223 mm, column 81 mm), so the braces swing by different angles and the column leans slightly.
TipToe t (0..1): tracks rotate t*61.3° about the drum, the upper brace swings t*41.7° (CAD), the robot then
pitches back about its planted front until the caster wheel is on the floor.
"""
import sys, os, math
import numpy as np
from PIL import Image, ImageDraw, ImageFont
import importlib.util
here = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location('tr', os.path.join(here, 'tiptoe_render_lib.py'))
tr = importlib.util.module_from_spec(spec); spec.loader.exec_module(tr)

urdf, cad1, cad3, out = sys.argv[1:5]
M = tr.Model(urdf)
DR = M.world('body_pelvis_drum_link')[:3, 3].copy(); Dx, Dz = DR[0], DR[2]
PX = 962.0
def cad(px, py): return np.array([Dx + (px - 927) / PX, Dz + (497 - py) / PX])
n = np.linalg.norm
A, C = cad(476, 433.75), cad(500, 515)
B0, D0 = cad(282.1, 424.9), cad(285.9, 502.3)
U, L, K = n(B0 - A), n(D0 - C), n(D0 - B0)
PSI_MAX = math.radians(41.7)                   # CAD upper brace swing (view 1 -> view 3)
THETA_MAX = math.radians(61.27)                # front-top tread vertical in the body frame
NOSE_BACK, TOP, NOSE_BOTTOM, BOX_BOTTOM = cad(245, 0)[0], cad(0, 375)[1], cad(0, 400)[1], cad(0, 450)[1]
CH = (NOSE_BOTTOM - BOX_BOTTOM) / 2            # chamfer half-height: face from nose bottom to box bottom, centred on B
print(f'A {A.round(4)} B {B0.round(4)} C {C.round(4)} D {D0.round(4)}; upper {U*1000:.0f} mm lower {L*1000:.0f} mm column {K*1000:.0f} mm')

old_P1x = M.world('body_caster_detail_hinge_pin_link')[:3, 3][0]
old_Q2 = M.world('body_caster_detail_collar_pin_left_link')[:3, 3][[0, 2]]
old_P2 = M.world('body_caster_detail_lower_arm_edge_link')[:3, 3][[0, 2]]
head_x = M.world('body_caster_head_link')[:3, 3][0]
db = A[0] - old_P1x                       # body section longer (hinge housing moves back)
dh = D0[0] - head_x                       # column moves back onto the CAD axis
print(f'body section longer {-db*100:.1f} cm; caster column {-dh*100:.1f} cm further back')

COLUMN = ('body_caster_collar', 'body_caster_spring', 'body_caster_yoke', 'body_caster_fork', 'body_caster_axle', 'body_caster_wheel',
          'body_caster_hub', 'body_caster_detail_collar_pin')
UPPER = ('body_caster_head', 'body_caster_top_plate')
HINGE_AREA = ('body_caster_detail_lower_arm', 'body_caster_detail_hinge', 'body_caster_detail_side_', 'body_caster_detail_hydraulic_eye',
              'body_caster_detail_hydraulic_pin', 'body_caster_detail_hydraulic_body', 'body_caster_detail_hydraulic_gland',
              'body_caster_detail_upper_cylinder', 'body_caster_detail_cylinder_', 'body_rear_housing_')
def group(link):
    if link.startswith(('body_track_', 'body_tread_', 'body_shroud_')): return 'track'
    if link in ('body_caster_arm_link', 'body_caster_arm_rib_link'): return 'arm'
    if link.startswith('body_caster_detail_collar_brace'): return 'lower'
    if link.startswith('body_caster_detail_hydraulic_shaft'): return 'shaft'
    if link.startswith(COLUMN): return 'column'
    if link.startswith(UPPER): return 'upper'
    if link.startswith(HINGE_AREA): return 'hinge'
    return 'body'

def roty(v, a, c):  # rotate x/z about c by a (+a moves +X down)
    x, z = v[..., 0] - c[0], v[..., 2] - c[1]; ca, sa = math.cos(a), math.sin(a)
    r = v.copy(); r[..., 0] = c[0] + x*ca + z*sa; r[..., 2] = c[1] - x*sa + z*ca; return r
def clip(tri, f):
    """Keep the part of triangle tri where f(point) >= 0 (f linear); returns triangles."""
    vals = [f(p) for p in tri]; poly = []
    for i in range(3):
        a, b, fa, fb = tri[i], tri[(i+1) % 3], vals[i], vals[(i+1) % 3]
        if fa >= 0: poly.append(a)
        if (fa >= 0) != (fb >= 0): poly.append(a + (fa/(fa - fb))*(b - a))
    return [np.array([poly[0], poly[k], poly[k+1]]) for k in range(1, len(poly) - 1)]

tris, cols, links = M.triangles(lambda l: True)
groups = np.array([group(l) for l in links])
cur_tris, cur_cols = tris.copy(), cols.copy()
# Split the caster arm at the existing hinge: front stays with the body section, rear joins the upper piece.
nt, nc, nl, ng = [], [], [], []
for i in range(len(tris)):
    if groups[i] != 'arm':
        nt.append(tris[i]); nc.append(cols[i]); nl.append(links[i]); ng.append(groups[i]); continue
    for piece in clip(tris[i], lambda p: p[0] - old_P1x): nt.append(piece); nc.append(cols[i]); nl.append(links[i]); ng.append('arm')
    for piece in clip(tris[i], lambda p: old_P1x - p[0]): nt.append(piece); nc.append(cols[i]); nl.append(links[i]); ng.append('upper')
tris, cols, links, groups = np.array(nt, dtype=np.float32), np.array(nc), np.array(nl), np.array(ng)

# ---- proposed rest shapes ----
rest = tris.copy()
up = groups == 'upper'; v = rest[up]
ux0, ux1 = v[..., 0].min(), old_P1x; uz0, uz1 = v[..., 2].min(), v[..., 2].max()
v[..., 0] = NOSE_BACK + (v[..., 0] - ux0)*(A[0] - NOSE_BACK)/(ux1 - ux0)
v[..., 2] = BOX_BOTTOM + (v[..., 2] - uz0)*(TOP - BOX_BOTTOM)/(uz1 - uz0)
rest[up] = v
# 45° chamfer centred on B: keep (above the face) or (body side of the face) or (above the nose bottom)
face = lambda p: (p[2] - B0[1]) + (p[0] - B0[0])
pieces = []
for i in np.where(up)[0]:
    seen = []
    for f in (face, lambda p: p[0] - (B0[0] + CH), lambda p: p[2] - NOSE_BOTTOM):
        for q in clip(rest[i], f): pieces.append((q, i))
keep = ~up
tris_r = list(rest[keep]); cols_r = list(cols[keep]); links_r = list(links[keep]); groups_r = list(groups[keep])
for q, i in pieces: tris_r.append(q); cols_r.append(cols[i]); links_r.append(links[i]); groups_r.append('upper')
rest = np.array(tris_r, dtype=np.float32); cols = np.array(cols_r); links = np.array(links_r); groups = np.array(groups_r)

def zmap_column(z):  # piecewise: wheel/fork/yoke fixed, lower bellows to D, upper bellows to just under the chamfer
    kn_old = [-2.0, -0.907, old_Q2[1], -0.838]; kn_new = [-2.0, -0.907, D0[1], B0[1] - .004]
    return np.where(z > kn_old[-1], z + kn_new[-1] - kn_old[-1], np.interp(z, kn_old, kn_new))
s = groups == 'column'; v = rest[s]; v[..., 0] += dh; v[..., 2] = zmap_column(v[..., 2]); rest[s] = v
s = groups == 'hinge'; rest[s, :, 0] += db
s = groups == 'shaft'; v = rest[s]; x = v[..., 0]; k = (x.max() - x)/max(1e-9, x.max() - x.min()); v[..., 0] = x + k*db; rest[s] = v
s = groups == 'arm'; rest[s, :, 0] = Dx + (rest[s, :, 0] - Dx)*(Dx - A[0])/(Dx - old_P1x)
s = groups == 'lower'; v = rest[s]
a0, b0 = old_Q2, old_P2; ax = b0 - a0; u = ax/n(ax); w = np.array([-u[1], u[0]])
rel = np.stack([v[..., 0] - a0[0], v[..., 2] - a0[1]], -1); sp = rel @ u / n(ax); o = rel @ w
nax = C - D0; nu = nax/n(nax); nw = np.array([-nu[1], nu[0]])
v[..., 0] = D0[0] + sp*nax[0] + o*nw[0]; v[..., 2] = D0[1] + sp*nax[1] + o*nw[1]; rest[s] = v

ground = rest[groups == 'track'][..., 2].min()
wheel = np.array([l.startswith(('body_caster_wheel', 'body_caster_hub')) for l in links])
rest_off = rest[wheel][..., 2].min() - ground
front_roller = M.world('body_track_front_roller_left_link')[:3, 3]
print(f'rest: caster wheel {rest_off*1000:.1f} mm from the floor')

def angle(v): return math.atan2(v[1], v[0])
def four_bar(psi):
    B = roty(np.array([B0[0], 0, B0[1]]), -psi, A)[[0, 2]]
    d = n(C - B); a = (L*L - K*K + d*d)/(2*d); h = math.sqrt(max(0.0, L*L - a*a)); m = C + a*(B - C)/d
    p = np.array([-(B - C)[1], (B - C)[0]])/d; cands = [m + h*p, m - h*p]
    D = min(cands, key=lambda q: n(q - D0) if psi == 0 else n(q - (D0 + (B - B0))))
    return B, D
def body_pose(t, base=None):
    """Body frame (drum fixed): tracks rotated, linkage swung."""
    p = (rest if base is None else base).copy(); th = THETA_MAX*t; psi = PSI_MAX*t
    B, D = four_bar(psi)
    s = groups == 'track'; p[s] = roty(p[s], th, (Dx, Dz))
    s = groups == 'upper'; p[s] = roty(p[s], -psi, A)
    s = groups == 'lower'; p[s] = roty(p[s], -(angle(D - C) - angle(D0 - C)), C)
    dc = angle(B - D) - angle(B0 - D0)
    s = groups == 'column'; q = roty(p[s], -dc, D0); q[..., 0] += D[0] - D0[0]; q[..., 2] += D[1] - D0[1]; p[s] = q
    return p, B, D, math.degrees(psi), math.degrees(math.remainder(angle(D0 - C) - angle(D - C), 2*math.pi)), math.degrees(dc)
def world_pose(t):
    p, B, D, us, ls, dc = body_pose(t)
    def place(beta):
        q = roty(p, -beta, (Dx, Dz))
        fr = roty(roty(front_roller[None, :], THETA_MAX*t, (Dx, Dz)), -beta, (Dx, Dz))[0]
        q[..., 2] += ground - q[groups == 'track'][..., 2].min(); q[..., 0] += front_roller[0] - fr[0]
        return q
    lo, hi = math.radians(-30), math.radians(30)
    for _ in range(40):
        mid = (lo + hi)/2; q = place(mid)
        if q[wheel][..., 2].min() - ground > rest_off: lo = mid
        else: hi = mid
    beta = (lo + hi)/2; q = place(beta)
    drum = roty(np.array([[Dx, 0, Dz]]), -beta, (Dx, Dz))[0]  # before translation -> recompute via a marker
    return q, beta, us, ls, dc

results = {}
for t in (0, .25, .5, .75, 1.0):
    q, beta, us, ls, dc = world_pose(t)
    # drum rise / forward from a drum marker transformed the same way
    marker = np.array([[[Dx, 0, Dz]]*3], dtype=np.float32)
    results[t] = (q, beta, us, ls, dc)
    print(f't={t:.2f}: tracks {math.degrees(THETA_MAX*t):.1f}°, upper brace {us:.1f}°, lower brace {abs(ls):.1f}°, column leans forward {-dc:.1f}°, robot pitch back {math.degrees(beta):.1f}°, front tread to floor {90 - math.degrees(beta) if t == 1 else float("nan"):.1f}°')
# body rise / forward at full: track the drum point through the same placement
def drum_motion(t):
    p, *_ = body_pose(t); p = np.concatenate([p, np.array([[[Dx, 0, Dz]]*3], dtype=np.float32)])
    g = np.concatenate([groups, ['marker']]); w = np.concatenate([wheel, [False]])
    beta = results[t][1]; q = roty(p, -beta, (Dx, Dz))
    fr = roty(roty(front_roller[None, :], THETA_MAX*t, (Dx, Dz)), -beta, (Dx, Dz))[0]
    q[..., 2] += ground - q[g == 'track'][..., 2].min(); q[..., 0] += front_roller[0] - fr[0]
    return q[-1, 0] - np.array([Dx, 0, Dz]), q[w][..., 0].mean()
dm, wc1 = drum_motion(1.0); wc0 = rest[wheel][..., 0].mean()
print(f'full: drum rises {dm[2]*100:.1f} cm, forward {dm[0]*100:.1f} cm; caster wheel moves {(wc1 - wc0)*100:+.1f} cm; footprint {(front_roller[0] - wc0)*100:.0f} -> {(front_roller[0] - wc1)*100:.0f} cm')

# ---------- rendering ----------
tint = {'track': (1.0, .55, .15), 'arm': (.55, .35, .85), 'hinge': (.55, .35, .85), 'shaft': (.55, .35, .85), 'upper': (.95, .25, .25), 'lower': (.1, .8, .8), 'column': (.2, .55, .95)}
def colours(highlight, base=None, grp=None):
    c = (cols if base is None else base).copy()/255.0; grp = groups if grp is None else grp
    if highlight:
        for g, rgb in tint.items():
            s = grp == g; c[s] = c[s]*.45 + np.array(rgb)*.55
    return c
def draw(t, c, size, scale, centre, ground_line=None, marks=()):
    sx = size[0]/2 + (t[..., 0] - centre[0])*scale; sy = size[1]*.62 - (t[..., 2] - centre[1])*scale
    nn = np.cross(t[:, 1] - t[:, 0], t[:, 2] - t[:, 0]); nn /= np.maximum(np.linalg.norm(nn, axis=1, keepdims=True), 1e-9)
    light = np.array([.35, .6, .72]); light /= np.linalg.norm(light)
    rgb = np.clip(c*(.4 + .6*np.abs(nn @ light))[:, None], 0, 1)*255
    img = Image.new('RGBA', size, (240, 241, 245, 255)); d = ImageDraw.Draw(img)
    for i in np.argsort(t[:, :, 1].mean(1)): d.polygon([(sx[i, k], sy[i, k]) for k in range(3)], fill=tuple(int(v) for v in rgb[i]) + (255,))
    if ground_line is not None:
        gy = size[1]*.62 - (ground_line - centre[1])*scale; d.line([(0, gy), (size[0], gy)], fill=(80, 80, 80, 255), width=2)
    for (x, z) in marks:
        px, py = size[0]/2 + (x - centre[0])*scale, size[1]*.62 - (z - centre[1])*scale
        d.ellipse([px - 6, py - 6, px + 6, py + 6], outline=(255, 255, 255, 255), width=3); d.ellipse([px - 3, py - 3, px + 3, py + 3], fill=(0, 0, 0, 255))
    return img
def hinge_marks(t):
    # hinge points through the same world placement: transform A, B, C, D as tiny triangles
    _, B, D, *_ = body_pose(t); pts = [A, B, C, D]; beta = results[t][1]
    p, *_ = body_pose(t); q = roty(p, -beta, (Dx, Dz))
    fr = roty(roty(front_roller[None, :], THETA_MAX*t, (Dx, Dz)), -beta, (Dx, Dz))[0]
    dz = ground - q[groups == 'track'][..., 2].min(); dx = front_roller[0] - fr[0]
    out = []
    for pt in pts:
        r = roty(np.array([pt[0], 0, pt[1]]), -beta, (Dx, Dz)); out.append((r[0] + dx, r[2] + dz))
    return out
font = ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf', 24); small = ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf', 18)
size = (700, 860); scale = 520; centre = (-0.10, -0.55)
panels = [("Current model · rest", cur_tris, colours(False, cur_cols), ())]
for t in (0, .5, 1.0):
    q, beta, us, ls, dc = results[t]
    lab = f"Proposed · TipToe {int(t*100)}" + (f" · pitched back {math.degrees(beta):.1f}°" if t else "")
    panels.append((lab, q, colours(True), hinge_marks(t)))
imgs = []
for lab, t, c, marks in panels:
    im = draw(t, c, size, scale, centre, ground, marks); ImageDraw.Draw(im).text((16, 12), lab, fill=(20, 20, 20), font=font); imgs.append(im)
sheet = Image.new('RGB', (len(imgs)*size[0], size[1] + 210), (255, 255, 255))
for i, im in enumerate(imgs): sheet.paste(im.convert('RGB'), (i*size[0], 0))
q1, beta1, us1, ls1, dc1 = results[1.0]
lines = [f'Red: ONE-PIECE upper brace = rear of the caster arm + swivel top housing, hinged at the body (grey circle) and at the centre of its 45° chamfer over the column axis.',
         f'Cyan: lower brace (collar braces), hinged at the body and at the collar ring. Four-bar, not a parallelogram: upper {U*1000:.0f} mm swings {us1:.1f}°, lower {L*1000:.0f} mm swings {abs(ls1):.1f}°; blue column leans {-dc1:.1f}° forward at full.',
         f'Orange: tracks rotate {math.degrees(THETA_MAX):.1f}° about the pelvis drum (front-top tread vertical to the body), then the robot pitches back {math.degrees(beta1):.1f}° on its planted front until the caster wheel',
         f'touches the floor, so the front tread ends at {90 - math.degrees(beta1):.1f}° to the floor. Drum rises {dm[2]*100:.1f} cm and moves {dm[0]*100:.1f} cm forward. Body section {-db*100:.1f} cm longer; column {-dh*100:.1f} cm further back,',
         f'bellows lengthened so the collar ring and chamfer hinge sit at the CAD heights (model wheel size kept). Dots: the four hinges.']
d = ImageDraw.Draw(sheet)
for i, s in enumerate(lines): d.text((18, size[1] + 14 + i*36), s, fill=(30, 30, 30), font=small)
sheet.save(out); print(out)

lower_only = lambda t: t[..., 2].max(1) < Dz + .32
def overlay(cadp, t, hubp, title):
    base = Image.open(cadp).convert('RGBA'); W, H = base.size
    sx = hubp[0] + (t[..., 0] - Dx)*PX; sy = hubp[1] - (t[..., 2] - Dz)*PX
    layer = Image.new('RGBA', (W, H), (0, 0, 0, 0)); d = ImageDraw.Draw(layer); c = colours(True)*255
    keep = lower_only(t) & np.isin(groups, list(tint))
    for i in np.argsort(t[:, :, 1].mean(1)):
        if keep[i]: d.polygon([(sx[i, k], sy[i, k]) for k in range(3)], fill=tuple(int(v) for v in c[i]) + (105,))
    base.alpha_composite(layer); d = ImageDraw.Draw(base); d.rectangle([0, 0, W, 40], fill=(255, 255, 255, 220)); d.text((12, 6), title, fill=(0, 0, 0), font=font)
    return base.convert('RGB')
overlay(cad1, rest, (927, 497), 'Proposed TipToe 0 over CAD view 1 (962 px/m, drum on hub)').save(out.replace('.png', '-cad1.png'))
overlay(cad3, body_pose(1.0)[0], (927 - 30, 497 + 37), 'Proposed TipToe 100 over CAD view 3 (body frame, before the pitch back)').save(out.replace('.png', '-cad3.png'))
