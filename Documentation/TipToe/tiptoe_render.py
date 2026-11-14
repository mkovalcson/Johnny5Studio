"""Render the Johnny 5 full-body URDF at rest and in the proposed TipToe pose.

TipToe (as understood): both track assemblies rotate about the pelvis drum axis, front down /
rear up, up to 45 degrees. The robot rises until the treads touch the floor again, and the caster
arm (which also hinges on the drum axis) swings down until its wheel touches the floor, which
pulls the wheel in toward the body. Nothing in the project is changed; output goes to argv[2].
"""
import sys, struct, re, math, os, xml.etree.ElementTree as ET
import numpy as np
from PIL import Image, ImageDraw, ImageFont

urdf_path, out_dir = sys.argv[1], sys.argv[2]
base = os.path.dirname(urdf_path)
root = ET.parse(urdf_path).getroot()

def rpy_matrix(r, p, y):
    cr, sr, cp, sp, cy, sy = math.cos(r), math.sin(r), math.cos(p), math.sin(p), math.cos(y), math.sin(y)
    return np.array([[cy*cp, cy*sp*sr - sy*cr, cy*sp*cr + sy*sr],
                     [sy*cp, sy*sp*sr + cy*cr, sy*sp*cr - cy*sr],
                     [-sp, cp*sr, cp*cr]])
def origin_matrix(el):
    m = np.eye(4)
    if el is None: return m
    xyz = [float(v) for v in el.get('xyz', '0 0 0').split()]
    rpy = [float(v) for v in el.get('rpy', '0 0 0').split()]
    m[:3, :3] = rpy_matrix(*rpy); m[:3, 3] = xyz
    return m

materials = {}
for mat in root.findall('material'):
    c = mat.find('color')
    if c is not None: materials[mat.get('name')] = [float(v) for v in c.get('rgba').split()]

def read_stl(path):
    data = open(path, 'rb').read()
    if data[:5] == b'solid' and b'facet' in data[:400]:
        v = np.array([list(map(float, m.groups())) for m in re.finditer(rb'vertex\s+(\S+)\s+(\S+)\s+(\S+)', data)], dtype=np.float32)
        return v.reshape(-1, 3, 3)
    n = struct.unpack('<I', data[80:84])[0]
    arr = np.frombuffer(data[84:84 + n*50], dtype=np.dtype([('n', '<f4', 3), ('v', '<f4', (3, 3)), ('a', '<u2')]))
    return arr['v'].astype(np.float32)

def cylinder(r, length, seg=24):
    a = np.linspace(0, 2*math.pi, seg, endpoint=False); tris = []
    for i in range(seg):
        a0, a1 = a[i], a[(i+1) % seg]
        p0 = [r*math.cos(a0), r*math.sin(a0)]; p1 = [r*math.cos(a1), r*math.sin(a1)]
        top, bot = length/2, -length/2
        tris += [[[*p0, bot], [*p1, bot], [*p1, top]], [[*p0, bot], [*p1, top], [*p0, top]],
                 [[0, 0, top], [*p0, top], [*p1, top]], [[0, 0, bot], [*p1, bot], [*p0, bot]]]
    return np.array(tris, dtype=np.float32)

def box(sx, sy, sz):
    x, y, z = sx/2, sy/2, sz/2
    c = np.array([[-x,-y,-z],[x,-y,-z],[x,y,-z],[-x,y,-z],[-x,-y,z],[x,-y,z],[x,y,z],[-x,y,z]], dtype=np.float32)
    f = [(0,1,2),(0,2,3),(4,6,5),(4,7,6),(0,4,5),(0,5,1),(1,5,6),(1,6,2),(2,6,7),(2,7,3),(3,7,4),(3,4,0)]
    return c[np.array(f)]

def sphere(r, seg=16):
    tris = []
    for i in range(seg):
        t0, t1 = math.pi*i/seg, math.pi*(i+1)/seg
        for j in range(seg*2):
            p0, p1 = 2*math.pi*j/(seg*2), 2*math.pi*(j+1)/(seg*2)
            P = lambda t, p: [r*math.sin(t)*math.cos(p), r*math.sin(t)*math.sin(p), r*math.cos(t)]
            tris += [[P(t0,p0), P(t1,p0), P(t1,p1)], [P(t0,p0), P(t1,p1), P(t0,p1)]]
    return np.array(tris, dtype=np.float32)

# Link visuals in link frames
link_meshes = {}
for link in root.findall('link'):
    parts = []
    for vis in link.findall('visual'):
        geo = vis.find('geometry')
        if geo is None or len(geo) == 0: continue
        g = geo[0]
        if g.tag == 'mesh':
            path = os.path.join(base, g.get('filename'))
            if not os.path.exists(path): continue
            tris = read_stl(path) * np.array([float(s) for s in g.get('scale', '1 1 1').split()], dtype=np.float32)
        elif g.tag == 'cylinder': tris = cylinder(float(g.get('radius')), float(g.get('length')))
        elif g.tag == 'box': tris = box(*[float(s) for s in g.get('size').split()])
        elif g.tag == 'sphere': tris = sphere(float(g.get('radius')))
        else: continue
        m = vis.find('material'); rgba = [.7, .7, .72, 1]
        if m is not None:
            c = m.find('color')
            rgba = [float(v) for v in c.get('rgba').split()] if c is not None else materials.get(m.get('name'), rgba)
        parts.append((origin_matrix(vis.find('origin')), tris, rgba))
    if parts: link_meshes[link.get('name')] = parts

joints = {}
for j in root.findall('joint'):
    joints[j.find('child').get('link')] = (j.find('parent').get('link'), origin_matrix(j.find('origin')))
world = {}
def world_of(name):
    if name in world: return world[name]
    if name not in joints: world[name] = np.eye(4); return world[name]
    parent, local = joints[name]
    world[name] = world_of(parent) @ local; return world[name]

def rot_y(a, pivot):
    m = np.eye(4); c, s = math.cos(a), math.sin(a)
    m[:3, :3] = [[c, 0, s], [0, 1, 0], [-s, 0, c]]
    t1 = np.eye(4); t1[:3, 3] = -np.asarray(pivot); t2 = np.eye(4); t2[:3, 3] = pivot
    return t2 @ m @ t1

pivot = world_of('body_pelvis_drum_link')[:3, 3].copy()
def group(name):
    if name.startswith(('body_track_', 'body_tread_', 'body_shroud_')): return 'track'
    if name.startswith('body_caster_'): return 'caster'
    return 'body'

def gather(track_angle, caster_angle, lift):
    tris, cols, groups = [], [], []
    lift_m = np.eye(4); lift_m[2, 3] = lift
    for name, parts in link_meshes.items():
        w = world_of(name); g = group(name)
        extra = rot_y(track_angle, pivot) if g == 'track' else rot_y(caster_angle, pivot) if g == 'caster' else np.eye(4)
        for local, t, rgba in parts:
            m = lift_m @ extra @ w @ local
            flat = t.reshape(-1, 3) @ m[:3, :3].T.astype(np.float32) + m[:3, 3].astype(np.float32)
            tris.append(flat.reshape(-1, 3, 3)); cols.append(np.tile(np.array(rgba[:3], dtype=np.float32), (len(t), 1))); groups += [g]*len(t)
    return np.concatenate(tris), np.concatenate(cols), np.array(groups)

rest_tris, rest_cols, rest_groups = gather(0, 0, 0)
ground = rest_tris[rest_groups == 'track'][:, :, 2].min()
caster_offset = rest_tris[rest_groups == 'caster'][:, :, 2].min() - ground  # spring preload / slack at rest

def track_min(angle):
    t, _, g = gather(angle, 0, 0); return t[g == 'track'][:, :, 2].min()

results = {}
for degrees in (45,):
    a = math.radians(degrees)
    lift = ground - track_min(a)
    # Caster: swing the arm (rear end down = negative rotation) until its lowest point is on the floor.
    lo, hi = 0.0, math.radians(80)
    def caster_min(phi):
        t, _, g = gather(a, -phi, lift); return t[g == 'caster'][:, :, 2].min()
    for _ in range(40):
        mid = (lo + hi)/2
        if caster_min(mid) > ground + caster_offset: lo = mid
        else: hi = mid
    phi = (lo + hi)/2
    results[degrees] = (lift, phi)

def wheel_center(track_angle, caster_angle, lift):
    w = world_of('body_caster_axle_link'); lm = np.eye(4); lm[2, 3] = lift
    return (lm @ rot_y(caster_angle, pivot) @ w)[:3, 3]
def front_roller(track_angle, lift):
    w = world_of('body_track_front_roller_left_link'); lm = np.eye(4); lm[2, 3] = lift
    return (lm @ rot_y(track_angle, pivot) @ w)[:3, 3]

lift, phi = results[45]
tip_tris, tip_cols, tip_groups = gather(math.radians(45), -phi, lift)
w0 = wheel_center(0, 0, 0); w1 = wheel_center(math.radians(45), -phi, lift)
f0 = front_roller(0, 0); f1 = front_roller(math.radians(45), lift)
summary = (f"Pivot (pelvis drum centre): x {pivot[0]:.3f} z {pivot[2]:.3f} m\n"
           f"TipToe 45 deg: body rises {lift*100:.1f} cm; caster arm swings down {math.degrees(phi):.1f} deg\n"
           f"Caster wheel moves in {abs(w1[0]-w0[0])*100:.1f} cm (x {w0[0]:.3f} -> {w1[0]:.3f}); "
           f"front roller x {f0[0]:.3f} -> {f1[0]:.3f}, z {f0[2]:.3f} -> {f1[2]:.3f}\n"
           f"Footprint (front contact to caster) {abs(f0[0]-w0[0])*100:.0f} cm -> {abs(f1[0]-w1[0])*100:.0f} cm")
print(summary)

def render(tris, cols, groups, view, size, scale, centre, title, highlight=True):
    yaw, pitch = view
    cy, sy, cp, sp = math.cos(yaw), math.sin(yaw), math.cos(pitch), math.sin(pitch)
    # camera: look along -forward; screen x = right, y = up
    Rz = np.array([[cy, -sy, 0], [sy, cy, 0], [0, 0, 1]]); Rx = np.array([[1, 0, 0], [0, cp, -sp], [0, sp, cp]])
    # map URDF (x fwd, y left, z up) so that the camera looks along +y (from the robot's left) at yaw 0
    R = Rx @ Rz
    p = (tris.reshape(-1, 3) - centre) @ R.T
    p = p.reshape(-1, 3, 3)
    sx = size[0]/2 + p[:, :, 0]*scale; sy_ = size[1]*.55 - p[:, :, 2]*scale; depth = p[:, :, 1].mean(1)
    e1 = tris[:, 1] - tris[:, 0]; e2 = tris[:, 2] - tris[:, 0]; n = np.cross(e1, e2)
    n /= np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-9)
    light = np.array([.35, .6, .72]); light /= np.linalg.norm(light)
    shade = .35 + .65*np.abs(n @ light)
    c = cols.copy()
    if highlight:
        c[groups == 'track'] = c[groups == 'track']*.55 + np.array([.95, .55, .15])*.45
        c[groups == 'caster'] = c[groups == 'caster']*.55 + np.array([.2, .6, .95])*.45
    rgb = np.clip(c*shade[:, None], 0, 1)*255
    order = np.argsort(-depth)  # far first
    img = Image.new('RGB', size, (238, 240, 244)); d = ImageDraw.Draw(img)
    gz = size[1]*.55 - (ground - centre[2])*scale if False else None
    for i in order:
        pts = [(sx[i, 0], sy_[i, 0]), (sx[i, 1], sy_[i, 1]), (sx[i, 2], sy_[i, 2])]
        col = tuple(int(v) for v in rgb[i]); d.polygon(pts, fill=col)
    return img, d

def floor_line(img, d, view, scale, centre, size):
    yaw, pitch = view
    # for side view the floor is a horizontal line at ground height
    y = size[1]*.55 - (ground - centre[2])*scale*math.cos(pitch)
    d.line([(0, y), (size[0], y)], fill=(90, 90, 90), width=2)

os.makedirs(out_dir, exist_ok=True)
size = (900, 1000); centre = np.array([0.0, 0.0, -0.35])
font = ImageFont.truetype('segoeui.ttf', 26) if os.path.exists('C:/Windows/Fonts/segoeui.ttf') else ImageFont.load_default()
small = ImageFont.truetype('segoeui.ttf', 20) if os.path.exists('C:/Windows/Fonts/segoeui.ttf') else ImageFont.load_default()
panels = []
# Side view from the robot's left: camera looks along -y. yaw rotates the robot about z.
side = (0.0, 0.0)
for label, (t, c, g) in (("Rest", (rest_tris, rest_cols, rest_groups)), ("TipToe 45°", (tip_tris, tip_cols, tip_groups))):
    # Side: project x to screen x, z to screen y, depth = y.
    img, d = render(t, c, g, (0.0, 0.0), size, 430, centre, label)
    floor_line(img, d, (0.0, 0.0), 430, centre, size)
    d.text((24, 18), label + " · left side view (front →)", fill=(20, 20, 20), font=font)
    panels.append(img)
# Three-quarter close view of the lower body for both poses
size2 = (900, 700); centre2 = np.array([0.0, 0.0, -0.95])
for label, (t, c, g) in (("Rest", (rest_tris, rest_cols, rest_groups)), ("TipToe 45°", (tip_tris, tip_cols, tip_groups))):
    keep = t.reshape(-1, 9)[:, 2::3].max(1) < -0.45  # lower body only for the close-up
    img, d = render(t[keep], c[keep], g[keep], (math.radians(-35), math.radians(-12)), size2, 640, centre2, label)
    d.text((24, 18), label + " · lower body, 3/4 front-left", fill=(20, 20, 20), font=font)
    panels.append(img)
sheet = Image.new('RGB', (1800, 1000 + 700 + 150), (255, 255, 255))
sheet.paste(panels[0], (0, 0)); sheet.paste(panels[1], (900, 0)); sheet.paste(panels[2], (0, 1000)); sheet.paste(panels[3], (900, 1000))
d = ImageDraw.Draw(sheet)
d.text((24, 1712), "Orange = track assemblies (rotate about the pelvis drum axis) · Blue = caster arm, head, fork and wheel (rotate about the same axis)", fill=(30, 30, 30), font=small)
for i, line in enumerate(summary.split('\n')): d.text((24, 1740 + i*26), line, fill=(30, 30, 30), font=small)
path = os.path.join(out_dir, 'tiptoe-render.png'); sheet.save(path); print(path)
