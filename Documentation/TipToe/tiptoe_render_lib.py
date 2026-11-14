"""Minimal URDF loader shared by the TipToe proposal scripts: link world matrices and visual triangles (link frame → model frame)."""
import os, re, struct, math, xml.etree.ElementTree as ET
import numpy as np

def rpy_matrix(r, p, y):
    cr, sr, cp, sp, cy, sy = math.cos(r), math.sin(r), math.cos(p), math.sin(p), math.cos(y), math.sin(y)
    return np.array([[cy*cp, cy*sp*sr - sy*cr, cy*sp*cr + sy*sr], [sy*cp, sy*sp*sr + cy*cr, sy*sp*cr - cy*sr], [-sp, cp*sr, cp*cr]])
def origin_matrix(el):
    m = np.eye(4)
    if el is None: return m
    m[:3, :3] = rpy_matrix(*[float(v) for v in el.get('rpy', '0 0 0').split()]); m[:3, 3] = [float(v) for v in el.get('xyz', '0 0 0').split()]
    return m
def read_stl(path):
    data = open(path, 'rb').read()
    if data[:5] == b'solid' and b'facet' in data[:400]:
        v = np.array([list(map(float, m.groups())) for m in re.finditer(rb'vertex\s+(\S+)\s+(\S+)\s+(\S+)', data)], dtype=np.float32)
        return v.reshape(-1, 3, 3)
    n = struct.unpack('<I', data[80:84])[0]
    arr = np.frombuffer(data[84:84 + n*50], dtype=np.dtype([('n', '<f4', 3), ('v', '<f4', (3, 3)), ('a', '<u2')]))
    return arr['v'].astype(np.float32)
def cylinder(r, length, seg=24):
    a = np.linspace(0, 2*math.pi, seg, endpoint=False); t = []
    for i in range(seg):
        a0, a1 = a[i], a[(i+1) % seg]; p0 = [r*math.cos(a0), r*math.sin(a0)]; p1 = [r*math.cos(a1), r*math.sin(a1)]; h = length/2
        t += [[[*p0, -h], [*p1, -h], [*p1, h]], [[*p0, -h], [*p1, h], [*p0, h]], [[0, 0, h], [*p0, h], [*p1, h]], [[0, 0, -h], [*p1, -h], [*p0, -h]]]
    return np.array(t, dtype=np.float32)
def box(sx, sy, sz):
    x, y, z = sx/2, sy/2, sz/2
    c = np.array([[-x,-y,-z],[x,-y,-z],[x,y,-z],[-x,y,-z],[-x,-y,z],[x,-y,z],[x,y,z],[-x,y,z]], dtype=np.float32)
    f = [(0,1,2),(0,2,3),(4,6,5),(4,7,6),(0,4,5),(0,5,1),(1,5,6),(1,6,2),(2,6,7),(2,7,3),(3,7,4),(3,4,0)]
    return c[np.array(f)]

class Model:
    def __init__(self, urdf):
        self.base = os.path.dirname(urdf); self.root = ET.parse(urdf).getroot()
        self.materials = {m.get('name'): [float(v) for v in m.find('color').get('rgba').split()] for m in self.root.findall('material') if m.find('color') is not None}
        self.joints = {j.find('child').get('link'): (j.find('parent').get('link'), origin_matrix(j.find('origin'))) for j in self.root.findall('joint')}
        self._world = {}; self._mesh = {}
        self.links = {l.get('name'): l for l in self.root.findall('link')}
    def world(self, link):
        if link in self._world: return self._world[link]
        w = self.world(self.joints[link][0]) @ self.joints[link][1] if link in self.joints else np.eye(4)
        self._world[link] = w; return w
    def visuals(self, link):
        if link in self._mesh: return self._mesh[link]
        out = []
        for vis in self.links[link].findall('visual'):
            geo = vis.find('geometry')
            if geo is None or len(geo) == 0: continue
            g = geo[0]
            if g.tag == 'mesh':
                path = os.path.join(self.base, g.get('filename'))
                if not os.path.exists(path): continue
                t = read_stl(path) * np.array([float(s) for s in g.get('scale', '1 1 1').split()], dtype=np.float32)
            elif g.tag == 'cylinder': t = cylinder(float(g.get('radius')), float(g.get('length')))
            elif g.tag == 'box': t = box(*[float(s) for s in g.get('size').split()])
            else: continue
            m = vis.find('material'); rgba = [.7, .7, .72, 1]
            if m is not None:
                c = m.find('color'); rgba = [float(v) for v in c.get('rgba').split()] if c is not None else self.materials.get(m.get('name'), rgba)
            out.append((origin_matrix(vis.find('origin')), t, rgba))
        self._mesh[link] = out; return out
    def triangles(self, include, colour=None, extra=lambda link: np.eye(4)):
        tris, cols, links = [], [], []
        for link in self.links:
            if not include(link): continue
            w = extra(link) @ self.world(link)
            for local, t, rgba in self.visuals(link):
                m = w @ local; flat = t.reshape(-1, 3) @ m[:3, :3].T.astype(np.float32) + m[:3, 3].astype(np.float32)
                tris.append(flat.reshape(-1, 3, 3)); c = colour(link) if colour else tuple(int(v*255) for v in rgba[:3])
                cols.append(np.tile(np.array(c, dtype=np.float32), (len(t), 1))); links += [link]*len(t)
        return np.concatenate(tris), np.concatenate(cols), np.array(links)
