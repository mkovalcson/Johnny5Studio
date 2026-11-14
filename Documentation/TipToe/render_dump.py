"""Renders the TipToe scene dumps written by the VR Editor's pose checks (rest and full), side by side."""
import sys, struct, math, os
import numpy as np
from PIL import Image, ImageDraw, ImageFont

def load(path):
    tris, cols = [], []
    data = open(path, 'rb').read(); o = 0
    while o < len(data):
        r, g, b = struct.unpack_from('<3f', data, o); n = struct.unpack_from('<i', data, o+12)[0]; o += 16
        v = np.frombuffer(data, dtype='<f4', count=n*9, offset=o).reshape(n, 3, 3); o += n*36
        tris.append(v); cols.append(np.tile(np.array([r, g, b], dtype=np.float32), (n, 1)))
    return np.concatenate(tris), np.concatenate(cols)

def render(tris, cols, size, scale, centre, yaw, pitch):
    cy, sy, cp, sp = math.cos(yaw), math.sin(yaw), math.cos(pitch), math.sin(pitch)
    Rz = np.array([[cy, -sy, 0], [sy, cy, 0], [0, 0, 1]]); Rx = np.array([[1, 0, 0], [0, cp, -sp], [0, sp, cp]]); R = Rx @ Rz
    p = ((tris.reshape(-1, 3) - centre) @ R.T).reshape(-1, 3, 3)
    sx = size[0]/2 + p[:, :, 0]*scale; syy = size[1]*.6 - p[:, :, 2]*scale; depth = p[:, :, 1].mean(1)
    n = np.cross(tris[:, 1]-tris[:, 0], tris[:, 2]-tris[:, 0]); n /= np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-9)
    light = np.array([.35, .6, .72]); light /= np.linalg.norm(light)
    rgb = np.clip(cols*(.35+.65*np.abs(n @ light))[:, None], 0, 1)*255
    img = Image.new('RGB', size, (238, 240, 244)); d = ImageDraw.Draw(img)
    for i in np.argsort(-depth):
        d.polygon([(sx[i, 0], syy[i, 0]), (sx[i, 1], syy[i, 1]), (sx[i, 2], syy[i, 2])], fill=tuple(int(v) for v in rgb[i]))
    return img, d

folder, out = sys.argv[1], sys.argv[2]
rest, rc = load(os.path.join(folder, 'tiptoe-scene-rest.bin')); full, fc = load(os.path.join(folder, 'tiptoe-scene-full.bin'))
# The scene frame's up axis: find it from the extent (largest span is height).
ext = rest.reshape(-1, 3).max(0) - rest.reshape(-1, 3).min(0)
up = int(np.argmax(ext)); print('extent', ext, 'up axis', up)
def to_zup(t):
    if up == 2: return t
    a = t.copy(); a[..., [2, up]] = t[..., [up, 2]]; return a
rest, full = to_zup(rest), to_zup(full)
lo = rest.reshape(-1, 3).min(0); hi = rest.reshape(-1, 3).max(0); h = hi[2] - lo[2]
ground = lo[2]; centre = np.array([(lo[0]+hi[0])/2, (lo[1]+hi[1])/2, lo[2]+h*.45])
size = (900, 1050); scale = 900/h*.95
font = ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf', 26)
sheet = Image.new('RGB', (1800, 1050), (255, 255, 255))
for k, (label, t, c) in enumerate((("VR Editor scene · TipToe 0", rest, rc), ("VR Editor scene · TipToe 100 (61.3°, tipped back onto the caster)", full, fc))):
    for yaw in (0.0, math.pi/2):  # try both horizontal axes, keep the one that shows a side view (wider)
        pass
    span_x = t.reshape(-1, 3)[:, 0].ptp(); span_y = t.reshape(-1, 3)[:, 1].ptp()
    yaw = 0.0 if span_x >= span_y else math.pi/2
    img, d = render(t, c, size, scale, centre, yaw, 0.0)
    gy = size[1]*.6 - (ground - centre[2])*scale; d.line([(0, gy), (size[0], gy)], fill=(90, 90, 90), width=2)
    d.text((24, 18), label, fill=(20, 20, 20), font=font)
    sheet.paste(img, (k*900, 0))
sheet.save(out); print(out)
