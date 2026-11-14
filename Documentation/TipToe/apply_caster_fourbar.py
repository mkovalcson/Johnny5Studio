"""Rebuild the full-body caster as the CAD four-bar linkage (TipToe, 2026-11-13).

Usage:  py -3 apply_caster_fourbar.py <original johnny5_head.urdf> <output johnny5_head.urdf>
Run it on the ORIGINAL URDF (backup: Archive/DesktopChanges/tiptoe-fourbar-20261113/johnny5_head.urdf);
it is not meant to be applied twice. Writes Meshes/Body/caster_upper_brace.stl next to the output.

Hinges, fitted to the CAD side views (962 px/m, view-1 track hub (927,497) = pelvis drum centre):
  A  upper brace body hinge      CAD (476, 433.75)   on the hinge housing (grey circle)
  B  upper brace head hinge      CAD (282.1, 424.9)  centre of the 45 degree chamfer, over the column axis
  C  lower brace body hinge      CAD (500, 515)
  D  lower brace head hinge      CAD (285.9, 502.3)  collar ring between the bellows
Changes (all in the body_root_link frame, +X forward, +Z up, metres):
  * body_caster_head_link: its visual becomes the one-piece upper brace (rear brace + swivel top housing),
    an extruded CAD side profile with the 45 degree chamfer centred on B and a rounded end around A.
  * caster column (collar, bellows rings, top plate, yoke, fork, axle, wheels, hubs) moves back onto the
    CAD column axis; the bellows are lengthened so the collar sits at D and the top plate just under B.
  * hinge housing (lower arm body/edge, hinge, side cheeks/panels, hydraulic/cylinder parts, rear housing)
    moves back so the hinge pin sits at A (the body section is longer); the hydraulic shaft lengthens.
  * collar braces (lower brace) are re-parented to body_root_link and run from D to C.
  * marker links body_caster_linkage_{upper_body,upper_head,lower_body,lower_head}_link at A, B, C, D;
    Shared/Pose/TipToeGeometry reads them.
"""
import sys, os, re, math, struct
import numpy as np

src, dst = sys.argv[1], sys.argv[2]
text = open(src, encoding='utf-8-sig').read()
PX = 962.0

def joint_block(child):
    m = re.search(r'  <joint name="[^"]+" type="[^"]+">\s*<parent link="([^"]+)" />\s*<child link="' + re.escape(child) + r'" />\s*<origin xyz="([^"]+)" rpy="([^"]+)" />\s*</joint>', text)
    if not m: raise SystemExit('joint not found for ' + child)
    return m
def joint(child):
    m = joint_block(child); return m.group(1), np.array([float(v) for v in m.group(2).split()]), [float(v) for v in m.group(3).split()]
def set_joint(child, xyz, rpy=None, parent=None):
    global text
    m = joint_block(child); old_parent, _, old_rpy = joint(child)
    rpy = rpy if rpy is not None else old_rpy; parent = parent or old_parent
    block = m.group(0)
    block = re.sub(r'<parent link="[^"]+" />', f'<parent link="{parent}" />', block)
    block = re.sub(r'<origin xyz="[^"]+" rpy="[^"]+" />', f'<origin xyz="{xyz[0]:.6f} {xyz[1]:.6f} {xyz[2]:.6f}" rpy="{" ".join(f"{v:.6f}" if isinstance(v, float) else str(v) for v in rpy)}" />', block)
    text = text[:m.start()] + block + text[m.end():]
def link_block(name):
    m = re.search(r'  <link name="' + re.escape(name) + r'">.*?</link>', text, flags=re.S)
    if not m: raise SystemExit('link not found ' + name)
    return m
def edit_link(name, fn):
    global text
    m = link_block(name); text = text[:m.start()] + fn(m.group(0)) + text[m.end():]
def world(child):
    p, xyz, _ = joint(child)
    return xyz if p == 'body_root_link' else world(p) + xyz
root_children = re.findall(r'<parent link="body_root_link" />\s*<child link="([^"]+)" />', text)

drum = world('body_pelvis_drum_link'); Dx, Dz = drum[0], drum[2]
def cad(px, py): return np.array([Dx + (px - 927) / PX, Dz + (497 - py) / PX])
A, B, C, D = cad(476, 433.75), cad(282.1, 424.9), cad(500, 515), cad(285.9, 502.3)
NOSE_BACK, TOP, NOSE_BOTTOM, BOX_BOTTOM = cad(245, 0)[0], cad(0, 375)[1], cad(0, 400)[1], cad(0, 450)[1]
CH = (NOSE_BOTTOM - BOX_BOTTOM) / 2

collar = world('body_caster_collar_link'); head = world('body_caster_head_link')
hinge_pin = world('body_caster_detail_hinge_pin_link')
dh = D[0] - head[0]                 # column back onto the CAD axis
db = A[0] - hinge_pin[0]            # hinge housing back: pin at A
print(f'A {A.round(4)} B {B.round(4)} C {C.round(4)} D {D.round(4)}; column {dh*100:+.1f} cm, hinge housing {db*100:+.1f} cm')

# ---- column ----
COLUMN = ('body_caster_collar', 'body_caster_spring', 'body_caster_yoke', 'body_caster_fork', 'body_caster_axle', 'body_caster_wheel',
          'body_caster_hub', 'body_caster_top_plate')
YOKE_TOP, SPRING_TOP = -0.907, -0.838
BELLOWS_TOP = B[1] - .004
kn_old = [YOKE_TOP, collar[2], SPRING_TOP]; kn_new = [YOKE_TOP, D[1], BELLOWS_TOP]
lower_scale = (kn_new[1] - kn_new[0]) / (kn_old[1] - kn_old[0]); upper_scale = (kn_new[2] - kn_new[1]) / (kn_old[2] - kn_old[1])
def zmap(z):
    if z <= YOKE_TOP: return z
    if z >= SPRING_TOP: return z + BELLOWS_TOP - SPRING_TOP
    return float(np.interp(z, kn_old, kn_new))
for child in root_children:
    if not child.startswith(COLUMN): continue
    _, xyz, rpy = joint(child)
    if child == 'body_caster_top_plate_link':
        # mesh sits 29.6 mm above its link origin; put its underside on the bellows top
        new = np.array([xyz[0] + dh, xyz[1], BELLOWS_TOP - (-0.754 - xyz[2])])
    else:
        new = np.array([xyz[0] + dh, xyz[1], zmap(xyz[2])])
    set_joint(child, new)
    if child.startswith('body_caster_spring_'):
        s = upper_scale if xyz[2] > collar[2] else lower_scale
        edit_link(child, lambda b: re.sub(r'length="([0-9.]+)"', lambda m: f'length="{float(m.group(1))*s:.6f}"', b))
print(f'bellows: upper rings x{upper_scale:.2f}, lower rings x{lower_scale:.2f}')

# ---- hinge housing (body section longer) ----
HINGE_AREA = ('body_caster_detail_lower_arm', 'body_caster_detail_hinge', 'body_caster_detail_side_', 'body_caster_detail_hydraulic_eye',
              'body_caster_detail_hydraulic_pin', 'body_caster_detail_hydraulic_body', 'body_caster_detail_hydraulic_gland',
              'body_caster_detail_upper_cylinder', 'body_caster_detail_cylinder_', 'body_rear_housing_')
for child in root_children:
    if child.startswith(HINGE_AREA):
        _, xyz, _ = joint(child); set_joint(child, xyz + np.array([db, 0, 0]))
_, xyz, _ = joint('body_caster_detail_hydraulic_shaft_link')
set_joint('body_caster_detail_hydraulic_shaft_link', xyz + np.array([db/2, 0, 0]))
pitch = 1.495463
edit_link('body_caster_detail_hydraulic_shaft_link', lambda b: re.sub(r'length="([0-9.]+)"', lambda m: f'length="{float(m.group(1)) - db/math.sin(pitch):.6f}"', b))

# ---- lower brace: collar braces re-parented to the root, D -> C ----
for side in ('left', 'right'):
    name = f'body_caster_detail_collar_brace_{side}_link'
    _, rel, _ = joint(name); centre = collar + rel
    vis = re.search(r'<origin xyz="0 0 0" rpy="([^"]+)" />\s*<geometry>\s*<box size="([^"]+)" />', link_block(name).group(0))
    r, p, _ = [float(v) for v in vis.group(1).split()]; size = [float(v) for v in vis.group(2).split()]
    u = np.array([math.sin(p)*math.cos(r), -math.sin(r), math.cos(p)*math.cos(r)])
    e0, e1 = centre - u*size[2]/2, centre + u*size[2]/2           # e0 at the collar pin, e1 at the body
    if e0[0] > e1[0]: e0, e1 = e1, e0
    e1 = np.array([e1[0], e1[1], e1[2]])
    n0 = np.array([e0[0] + (D[0] - collar[0]), e0[1], e0[2] + (D[1] - collar[2])])
    n1 = np.array([C[0], e1[1], C[1]])
    d = n1 - n0; L = float(np.linalg.norm(d)); u = d / L
    nr, np_ = -math.asin(u[1]), math.atan2(u[0], u[2])
    set_joint(name, (n0 + n1)/2, [0.0, 0.0, 0.0], 'body_root_link')
    edit_link(name, lambda b: b.replace(vis.group(0), f'<origin xyz="0 0 0" rpy="{nr:.6f} {np_:.6f} 0.000000" />\n      <geometry>\n        <box size="{size[0]:.6f} {size[1]:.6f} {L:.6f}" />'))
    print(f'{side} lower brace {L*1000:.0f} mm')

# ---- one-piece upper brace (visual of body_caster_head_link) ----
R = .035; W = .068
upper = [(NOSE_BACK, TOP), (A[0] - .06, TOP)] + [(A[0] + R*math.cos(math.radians(a)), A[1] + R*math.sin(math.radians(a))) for a in range(120, -1, -15)]
lower = [(NOSE_BACK, NOSE_BOTTOM), (B[0] - CH, NOSE_BOTTOM), (B[0] + CH, BOX_BOTTOM), (A[0] - .03, BOX_BOTTOM)] + \
        [(A[0] + R*math.cos(math.radians(a)), A[1] + R*math.sin(math.radians(a))) for a in range(240, 361, 15)]
xs = sorted(set(round(x, 7) for x, _ in upper + lower))
def chain(c, x): return float(np.interp(x, [p[0] for p in c], [p[1] for p in c]))
tris = []
def add(a, b, c, out):
    a, b, c = map(np.array, (a, b, c)); n = np.cross(b - a, c - a)
    if np.dot(n, out) < 0: b, c = c, b
    tris.append((a, b, c))
for x0, x1 in zip(xs, xs[1:]):
    t0, t1, b0, b1 = chain(upper, x0), chain(upper, x1), chain(lower, x0), chain(lower, x1)
    for y, s in ((W, 1), (-W, -1)):   # sides
        add((x0, y, b0), (x1, y, b1), (x1, y, t1), (0, s, 0)); add((x0, y, b0), (x1, y, t1), (x0, y, t0), (0, s, 0))
    up_out = (-(t1 - t0), 0, x1 - x0); dn_out = (b1 - b0, 0, -(x1 - x0))
    add((x0, -W, t0), (x1, -W, t1), (x1, W, t1), up_out); add((x0, -W, t0), (x1, W, t1), (x0, W, t0), up_out)
    add((x0, -W, b0), (x1, W, b1), (x1, -W, b1), dn_out); add((x0, -W, b0), (x0, W, b0), (x1, W, b1), dn_out)
x0 = xs[0]; t0, b0 = chain(upper, x0), chain(lower, x0)
add((x0, -W, b0), (x0, W, t0), (x0, W, b0), (-1, 0, 0)); add((x0, -W, b0), (x0, -W, t0), (x0, W, t0), (-1, 0, 0))
mesh_rel = 'Meshes/Body/caster_upper_brace.stl'
with open(os.path.join(os.path.dirname(dst), mesh_rel), 'wb') as f:
    f.write(b'caster_upper_brace: one-piece upper brace + swivel top housing'.ljust(80, b' ')); f.write(struct.pack('<I', len(tris)))
    for a, b, c in tris:
        a, b, c = a - head, b - head, c - head; n = np.cross(b - a, c - a); n = n / (np.linalg.norm(n) or 1)
        f.write(struct.pack('<12fH', *n, *a, *b, *c, 0))
edit_link('body_caster_head_link', lambda b: b.replace('Meshes/Body/caster_head.stl', mesh_rel))
print(f'upper brace mesh: {len(tris)} triangles, {(A[0] + R - NOSE_BACK)*1000:.0f} mm long, chamfer {2*CH*1000*math.sqrt(2):.0f} mm face')

# ---- hinge markers ----
markers = ''
for name, p in (('upper_body', A), ('upper_head', B), ('lower_body', C), ('lower_head', D)):
    markers += (f'  <link name="body_caster_linkage_{name}_link" />\n  <joint name="body_caster_linkage_{name}_fixed" type="fixed">\n'
                f'    <parent link="body_root_link" />\n    <child link="body_caster_linkage_{name}_link" />\n'
                f'    <origin xyz="{p[0]:.6f} 0.000000 {p[1]:.6f}" rpy="0 0 0" />\n  </joint>\n')
i = text.rindex('</robot>'); text = text[:i] + markers + text[i:]
open(dst, 'w', encoding='utf-8').write(text); print('wrote', dst)
