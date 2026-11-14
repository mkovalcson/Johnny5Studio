"""Proposal render: CAD-matched caster parallelogram + 61.3° TipToe (front tread vertical).

Model frame: +X forward, +Z up, metres. CAD scale 962 px/m, CAD track hub = model pelvis drum.
Body section (drum → hinge housing) is lengthened so the body hinges sit where the CAD has them;
upper brace (rear of the caster arm) and lower brace (collar braces) hinge at both ends and form a
parallelogram, so the head / bellows column / fork / wheel stays vertical while it swings down.
"""
import sys, os, math
import numpy as np
from PIL import Image, ImageDraw, ImageFont
import importlib.util
here = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location('tr', os.path.join(here, 'tiptoe_render_lib.py'))
tr = importlib.util.module_from_spec(spec); spec.loader.exec_module(tr)

urdf, cad1, cad2, out = sys.argv[1:5]
M = tr.Model(urdf)
D = M.world('body_pelvis_drum_link')[:3, 3].copy(); Dx, Dz = D[0], D[2]
PX = 962.0  # CAD px per metre

# --- CAD measurements (view 1 pixels; hub at (927,497)) -------------------------------------------
hub = np.array([927.0, 497.0])
def cad_m(p): return np.array([(p[0] - hub[0]) / PX, (hub[1] - p[1]) / PX])
H1c, H2c = cad_m((476, 433.75)), cad_m((500, 515))
# Upper head hinge where view 3's purple arrow points (view 3 -> view 1: +30, -37 px; tiptoe pose,
# carried back to rest with the wheel): on the bellows column axis at its top.
A1c = cad_m((288, 412.5))
hinge_off = H1c - H2c                      # lower body hinge -> upper body hinge
brace_vec = A1c - H1c                      # body hinge -> head hinge (both braces: parallelogram)
Q2c = A1c - hinge_off                      # lower head hinge on the collar ring, forward of the axis
L = np.linalg.norm(brace_vec); rest_rise = math.degrees(math.atan2(brace_vec[1], -brace_vec[0]))

# --- Model placement --------------------------------------------------------------------------
old_Q2 = M.world('body_caster_detail_collar_pin_left_link')[:3, 3][[0, 2]]   # collar pin on the column
old_P2 = M.world('body_caster_detail_lower_arm_edge_link')[:3, 3][[0, 2]]    # collar brace's body pin
old_P1x = M.world('body_caster_detail_hinge_pin_link')[:3, 3][0]              # existing hinge on the arm
P2 = np.array([Dx + H2c[0], old_Q2[1] - brace_vec[1]])   # CAD x; height keeps the head (wheel on the floor)
P1 = P2 + hinge_off
Q2 = P2 + brace_vec; Q1 = Q2 + hinge_off
dh = Q1[0] - old_Q2[0]                                   # head assembly moves back (column axis = upper head hinge)
db = P2[0] - old_P2[0]                                   # hinge housing moves back (body section longer)
head_split = -0.40                                      # arm mesh behind this is the head housing
print(f'brace length {L*1000:.0f} mm, rest rise {rest_rise:.1f} deg, hinge spacing {np.linalg.norm(hinge_off)*1000:.0f} mm')
print(f'P1 {P1.round(4)} P2 {P2.round(4)} Q1 {Q1.round(4)} Q2 {Q2.round(4)}; head back {-dh*100:.1f} cm; body section longer {-db*100:.1f} cm')

HEAD = ('body_caster_head', 'body_caster_top_plate', 'body_caster_collar', 'body_caster_spring', 'body_caster_yoke', 'body_caster_fork',
        'body_caster_axle', 'body_caster_wheel', 'body_caster_hub', 'body_caster_detail_collar_pin')
HINGE_AREA = ('body_caster_detail_lower_arm', 'body_caster_detail_hinge', 'body_caster_detail_side_', 'body_caster_detail_hydraulic_eye',
              'body_caster_detail_hydraulic_pin', 'body_caster_detail_hydraulic_body', 'body_caster_detail_hydraulic_gland',
              'body_caster_detail_upper_cylinder', 'body_caster_detail_cylinder_', 'body_rear_housing_')
def group(link):
    if link.startswith(('body_track_', 'body_tread_', 'body_shroud_')): return 'track'
    if link == 'body_caster_arm_link': return 'arm'
    if link == 'body_caster_arm_rib_link': return 'hinge'
    if link.startswith('body_caster_detail_collar_brace'): return 'lower'
    if link.startswith('body_caster_detail_hydraulic_shaft'): return 'shaft'
    if link.startswith(HEAD): return 'head'
    if link.startswith(HINGE_AREA): return 'hinge'
    return 'body'

def roty(v, a, c):  # rotate x/z of points v about centre c (x,z) by a about +Y (row convention: +a moves +X down)
    x, z = v[..., 0] - c[0], v[..., 2] - c[1]; ca, sa = math.cos(a), math.sin(a)
    r = v.copy(); r[..., 0] = c[0] + x*ca + z*sa; r[..., 2] = c[1] - x*sa + z*ca; return r

def pose(tris, groups, theta=0.0, phi=0.0, lift=0.0, ahead=0.0, proposed=True):
    t = tris.copy()
    if proposed:
        for g in ('head', 'hinge', 'arm', 'armbrace', 'armhead', 'lower', 'shaft'):
            sel = groups == g
            if not sel.any(): continue
            v = t[sel]
            if g == 'head': v[..., 0] += dh
            elif g == 'hinge': v[..., 0] += db
            elif g == 'shaft':  # rear end follows the hinge housing, front end stays
                x = v[..., 0]; front = x.max(); back = x.min(); k = (front - x) / max(1e-9, front - back); v[..., 0] = x + k*db
            elif g == 'arm': v[..., 0] = Dx + (v[..., 0] - Dx)*(Dx - P1[0])/(Dx - old_P1x)
            elif g == 'armbrace': v[..., 0] = P1[0] + (v[..., 0] - old_P1x)*((P1[0] - (head_split + dh))/(old_P1x - head_split))
            elif g == 'armhead': v[..., 0] += dh
            elif g == 'lower':
                a, b = old_Q2, old_P2; ax = b - a; n = np.linalg.norm(ax); u = ax/n; w = np.array([-u[1], u[0]])
                rel = np.stack([v[..., 0] - a[0], v[..., 2] - a[1]], -1); s = rel @ u / n; o = rel @ w
                na = Q2; nb = P2; nax = nb - na; nn = np.linalg.norm(nax); nu = nax/nn; nw = np.array([-nu[1], nu[0]])
                v[..., 0] = na[0] + s*nax[0] + o*nw[0]; v[..., 2] = na[1] + s*nax[1] + o*nw[1]
            t[sel] = v
    # TipToe
    if theta:
        sel = groups == 'track'; t[sel] = roty(t[sel], theta, (Dx, Dz))
    if phi:
        swing = -phi
        disp = roty(np.array([[Q2[0], 0, Q2[1]]]), swing, P2)[0] - np.array([Q2[0], 0, Q2[1]])
        sel = groups == 'head'; t[sel] += disp
        sel = groups == 'lower'; t[sel] = roty(t[sel], swing, P2)
        sel = (groups == 'armbrace') | (groups == 'armbrace_r'); t[sel] = roty(t[sel], swing, P1)
        sel = groups == 'armhead'; t[sel] += disp
    t[..., 2] += lift; t[..., 0] += ahead
    return t

tris, cols, links = M.triangles(lambda l: True)
groups = np.array([group(l) for l in links])
cur_tris, cur_cols = tris.copy(), cols.copy()
def clip(tri, c, keep_less):
    # Sutherland-Hodgman against x = c; returns triangles (fan) of the kept side
    poly = []
    for i in range(3):
        a, b = tri[i], tri[(i+1) % 3]; ina = (a[0] < c) == keep_less; inb = (b[0] < c) == keep_less
        if ina: poly.append(a)
        if ina != inb:
            t = (c - a[0])/(b[0] - a[0]); poly.append(a + t*(b - a))
    return [np.array([poly[0], poly[k], poly[k+1]]) for k in range(1, len(poly) - 1)]
arm = np.where(groups == 'arm')[0]; new_t, new_c, new_l, new_g = [], [], [], []
for i in arm:
    for part, keep in (('arm', False), ('rest', True)):
        for piece in clip(tris[i], old_P1x, keep):
            if part == 'arm': new_t.append(piece); new_g.append('arm')
            else:
                for sub, keep2 in (('armhead', True), ('armbrace', False)):
                    for q in clip(piece, head_split, keep2): new_t.append(q); new_g.append(sub)
            n = len(new_t) - len(new_c); new_c += [cols[i]]*n; new_l += [links[i]]*n
others = groups != 'arm'
CHAMFER = .06   # leg length along the brace's underside and up its head end, metres
tris = np.concatenate([tris[others], np.array(new_t, dtype=np.float32)]); cols = np.concatenate([cols[others], np.array(new_c)])
links = np.concatenate([links[others], np.array(new_l)]); groups = np.concatenate([groups[others], np.array(new_g)])
front_roller = M.world('body_track_front_roller_left_link')[:3, 3]
ground = tris[groups == 'track'][..., 2].min()
theta = math.radians(61.27)
rest = pose(tris, groups)
# Chamfer the upper brace (clip its triangles in rest pose, brace frame: u from the head end toward the body, v up)
rise = math.atan2(brace_vec[1], -brace_vec[0])
def brace_frame(v):
    x, z = v[..., 0] - Q1[0], v[..., 2] - Q1[1]
    return x*math.cos(rise) - z*math.sin(rise), x*math.sin(rise) + z*math.cos(rise)
bsel = np.where(groups == 'armbrace')[0]
bu, bv = brace_frame(rest[bsel]); u_end = bu.min(); v_bottom = bv.min()
def keep_side(p):  # True above the chamfer line (u - u_end) + (v - v_bottom) >= CHAMFER
    u, v = brace_frame(p[None, :])
    return (u[0] - u_end) + (v[0] - v_bottom) >= CHAMFER
def clip_plane(tri):
    poly = []
    f = [(brace_frame(p[None, :])[0][0] - u_end) + (brace_frame(p[None, :])[1][0] - v_bottom) - CHAMFER for p in tri]
    for i in range(3):
        a, b, fa, fb = tri[i], tri[(i+1) % 3], f[i], f[(i+1) % 3]
        if fa >= 0: poly.append(a)
        if (fa >= 0) != (fb >= 0): poly.append(a + (fa/(fa - fb))*(b - a))
    return [np.array([poly[0], poly[k], poly[k+1]]) for k in range(1, len(poly) - 1)]
keep_idx = [i for i in range(len(rest)) if i not in set(bsel)]
nt, nr, nc, nl, ng = [tris[i] for i in keep_idx], [rest[i] for i in keep_idx], [cols[i] for i in keep_idx], [links[i] for i in keep_idx], [groups[i] for i in keep_idx]
for i in bsel:
    pieces = clip_plane(rest[i])
    for piece in pieces:
        nr.append(piece); nt.append(piece); nc.append(cols[i]); nl.append(links[i]); ng.append('armbrace_r')
tris = np.array(nt, dtype=np.float32); rest = np.array(nr, dtype=np.float32); cols = np.array(nc); links = np.array(nl); groups = np.array(ng)
# 'armbrace_r' triangles are already in the proposed rest pose: pose() must not re-map them
print(f'upper brace chamfer {CHAMFER*1000:.0f} mm at 45 deg; brace length {np.linalg.norm(brace_vec)*1000:.0f} mm, rise {math.degrees(rise):.1f} deg')
tp = pose(tris, groups, theta)
lift = ground - tp[groups == 'track'][..., 2].min()
fr = roty(front_roller[None, :], theta, (Dx, Dz))[0]; ahead = front_roller[0] - fr[0]
wheel = np.isin(links, [l for l in set(links) if l.startswith(('body_caster_wheel', 'body_caster_hub'))])
rest_off = rest[wheel][..., 2].min() - ground
lo, hi = 0.0, math.radians(80)
for _ in range(36):
    mid = (lo + hi)/2
    w = pose(tris[wheel], groups[wheel], theta, mid, lift, 0)[..., 2].min()
    if w > ground + rest_off: lo = mid
    else: hi = mid
phi = (lo + hi)/2
full = pose(tris, groups, theta, phi, lift, ahead)
phi_cad = math.radians(37.8); full_cad = pose(tris, groups, theta, phi_cad, lift, ahead)
gap = full_cad[wheel][..., 2].min() - (ground + rest_off)
cur = cur_tris
print(f'TipToe 61.3°: body rises {lift*100:.1f} cm, forward {ahead*100:.1f} cm, braces swing {math.degrees(phi):.1f}° (CAD 37.8°)')
wc0 = rest[wheel][..., 0].mean(); wc1 = full[wheel][..., 0].mean(); print(f'caster wheel rolls in {(wc1 - wc0)*100:.1f} cm; footprint front contact to caster {(front_roller[0] - wc0)*100:.0f} -> {(front_roller[0] - wc1)*100:.0f} cm')

# ---------- rendering ----------
tint = {'track': (1.0, .55, .15), 'arm': (.55, .35, .85), 'armbrace': (.95, .25, .25), 'armbrace_r': (.95, .25, .25), 'armhead': (.2, .55, .95), 'lower': (.1, .8, .8), 'head': (.2, .55, .95), 'hinge': (.55, .35, .85), 'shaft': (.55, .35, .85)}
def colours(highlight, base=None):
    c = (cols if base is None else base).copy()/255.0
    if highlight:
        for g, rgb in tint.items():
            s = groups == g; c[s] = c[s]*.45 + np.array(rgb)*.55
    return c
def draw(t, c, size, scale, centre, ground_line=None, base=None, alpha=255, cull=None):
    p = t; sx = size[0]/2 + (p[..., 0] - centre[0])*scale; sy = size[1]*.62 - (p[..., 2] - centre[1])*scale
    n = np.cross(t[:, 1] - t[:, 0], t[:, 2] - t[:, 0]); n /= np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-9)
    light = np.array([.35, .6, .72]); light /= np.linalg.norm(light)
    rgb = np.clip(c*(.4 + .6*np.abs(n @ light))[:, None], 0, 1)*255
    img = base.copy() if base is not None else Image.new('RGBA', size, (240, 241, 245, 255))
    layer = Image.new('RGBA', size, (0, 0, 0, 0)); d = ImageDraw.Draw(layer)
    keep = np.ones(len(t), bool) if cull is None else cull
    for i in np.argsort(t[:, :, 1].mean(1)):   # camera on the robot's left (+Y): far side first
        if keep[i]: d.polygon([(sx[i, k], sy[i, k]) for k in range(3)], fill=tuple(int(v) for v in rgb[i]) + (alpha,))
    img.alpha_composite(layer); d = ImageDraw.Draw(img)
    if ground_line is not None:
        gy = size[1]*.62 - (ground_line - centre[1])*scale; d.line([(0, gy), (size[0], gy)], fill=(80, 80, 80, 255), width=2)
    return img
font = ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf', 24); small = ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf', 18)
size = (820, 900); scale = 560; centre = (-0.05, -0.55)
panels = []
for label, t, hl in (("Current model · rest", cur, False), ("Proposed · TipToe 0", rest, True), ("Proposed · TipToe 100 (61.3°), wheel on floor", full, True)):
    im = draw(t, colours(hl, cur_cols if t is cur else None), size, scale, centre, ground); d = ImageDraw.Draw(im); d.text((18, 14), label, fill=(20, 20, 20), font=font); panels.append(im)
# CAD overlays (body frame: no lift / forward, drum on the hub)
lower_only = lambda t: t[..., 2].max(1) < Dz + .32
def overlay(cad, t, hubp, title):
    base = Image.open(cad).convert('RGBA'); W, H = base.size
    sx = hubp[0] + (t[..., 0] - Dx)*PX; sy = hubp[1] - (t[..., 2] - Dz)*PX
    layer = Image.new('RGBA', (W, H), (0, 0, 0, 0)); d = ImageDraw.Draw(layer); c = colours(True)*255
    keep = lower_only(t) & np.isin(groups, ['track', 'arm', 'armbrace', 'armbrace_r', 'armhead', 'lower', 'head', 'hinge', 'shaft'])
    for i in np.argsort(t[:, :, 1].mean(1)):
        if keep[i]: d.polygon([(sx[i, k], sy[i, k]) for k in range(3)], fill=tuple(int(v) for v in c[i]) + (105,))
    base.alpha_composite(layer); d = ImageDraw.Draw(base); d.rectangle([0, 0, W, 40], fill=(255, 255, 255, 220)); d.text((12, 6), title, fill=(0, 0, 0), font=font)
    return base.convert('RGB')
ov1 = overlay(cad1, rest, (927, 497), 'Proposed TipToe 0 over CAD view 1 (962 px/m, drum on hub)')
ov2 = overlay(cad2, pose(tris, groups, theta, phi, 0, 0), (927 - 68, 497 + 1), 'Proposed TipToe 100 over CAD view 2 (body frame)')
sheet = Image.new('RGB', (2460, 900 + 175), (255, 255, 255))
for i, p in enumerate(panels): sheet.paste(p.convert('RGB'), (i*820, 0))
d = ImageDraw.Draw(sheet)
lines = [f'Orange tracks rotate 61.3° about the pelvis drum (front tread vertical); front rollers planted. Body rises {lift*100:.1f} cm and moves forward {ahead*100:.1f} cm.',
         f'Red upper brace (rear of the caster arm) and cyan lower brace (collar braces) are both {L*1000:.0f} mm, hinged at both ends, 88 mm apart: a parallelogram, so the blue head, bellows, fork and wheel stay vertical.',
         f'Body section lengthened {-db*100:.1f} cm (purple: hinge housing, cheeks, panels, rear housing move back with it); head assembly {-dh*100:.1f} cm further back.',
         f'Upper brace hinges: body caster arm grey circle and head column top (view 3 red / purple arrows); deep 45° chamfer under its head end ({CHAMFER*1000:.0f} mm legs). Braces swing {math.degrees(phi):.1f}° so the wheel stays on the floor.']
for i, s in enumerate(lines): d.text((18, 912 + i*36), s, fill=(30, 30, 30), font=small)
sheet.save(out); print(out)
ov1.save(out.replace('.png', '-cad1.png')); ov2.save(out.replace('.png', '-cad2.png'))
