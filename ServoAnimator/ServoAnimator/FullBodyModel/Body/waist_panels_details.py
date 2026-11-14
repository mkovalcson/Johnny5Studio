"""Hinged waist panels and the LCD behind them.

The two blue waist panels become doors hinged on their outer edges. PanelsOpen
(0 = closed) swings the left door forward and outward about its outer edge;
PanelsOpenRight mirrors it through URDF mimic metadata, so one control opens and
closes both together. Between the hinges, an LCD screen skin wraps the waist
column: while the doors are shut it lies hidden inside their thickness, and as
they open it is revealed. The editor draws pictures or text on any visual named
johnny5_lcd_screen (it wraps the image around the screen's curve, local z up and
+x out); other URDF viewers just see a black screen.

Focused updater: it edits the existing URDF in place and running it again
changes nothing.
"""
import copy
import math
from pathlib import Path
import struct
import xml.etree.ElementTree as E
import numpy as np
from hand_cad_details import transform, angles, fmt, update
from arm_cad_details import mesh, quad

BLACK = 'cad_020202'; LIGHT = 'cad_b3b3b3'
OPEN = 1.75                 # radians the doors open (about 100 degrees)
SCREEN_R = .0683            # just outside the waist column (r 68 mm), inside the doors' 67-70 mm shell


def curved_strip(r, a0, a1, z0, z1, n=24):
    """Closed thin cylindrical strip (outer face at r) about local z, from angle a0 to a1."""
    t = .0004
    def p(a, z, rr): return (rr * math.cos(a), rr * math.sin(a), z)
    tris = []
    angs = [a0 + (a1 - a0) * k / n for k in range(n + 1)]
    for a, b in zip(angs, angs[1:]):
        quad(tris, p(a, z0, r), p(b, z0, r), p(b, z1, r), p(a, z1, r))               # outer face (+r)
        quad(tris, p(a, z1, r - t), p(b, z1, r - t), p(b, z0, r - t), p(a, z0, r - t))
        quad(tris, p(a, z1, r), p(b, z1, r), p(b, z1, r - t), p(a, z1, r - t))
        quad(tris, p(a, z0, r - t), p(b, z0, r - t), p(b, z0, r), p(a, z0, r))
    for a, w in ((a0, -1), (a1, 1)):
        side = [p(a, z0, r), p(a, z1, r), p(a, z1, r - t), p(a, z0, r - t)]
        quad(tris, *(side if w > 0 else side[::-1]))
    return tris


def apply(root):
    links = {l.get('name'): l for l in root.findall('link')}
    joints = {j.find('child').get('link'): j for j in root.findall('joint')}
    def world(n):
        j = joints.get(n)
        return np.eye(4) if j is None else world(j.find('parent').get('link')) @ transform(j)
    base = Path(__file__).resolve().parent.parent
    def stl_points(rel):
        d = (base / rel).read_bytes(); n = struct.unpack('<I', d[80:84])[0]
        return np.frombuffer(d[84:84 + 50 * n], dtype=np.uint8).reshape(n, 50)[:, 12:48].copy().view('<f4').reshape(-1, 3).astype(float)
    frames = {}
    for s, sy in (('l', 1), ('r', -1)):
        link = links[f'body_waist_blue_panel_{s}_link']
        joint = joints[link.get('name')]
        saved = link.find('johnny5_panel_source')
        if saved is None:                                           # remember the built (fixed) placement
            saved = E.SubElement(link, 'johnny5_panel_source')
            saved.append(copy.deepcopy(joint.find('origin'))); saved.append(copy.deepcopy(link.find('visual')))
        visual = link.find('visual'); original = saved.find('visual')
        V = transform(saved) @ transform(original)                  # panel (column) frame in the parent (saved joint origin)
        frames[s] = V
        pts = stl_points(original.find('geometry/mesh').get('filename'))
        edge = pts[np.argmax(sy * pts[:, 1])]                       # outer edge, outer face
        hinge = np.array([edge[0], edge[1], (pts[:, 2].min() + pts[:, 2].max()) / 2])
        H = V.copy(); H[:3, 3] = V[:3, :3] @ hinge + V[:3, 3]
        joint.set('name', 'PanelsOpen' if s == 'l' else 'PanelsOpenRight'); joint.set('type', 'revolute')
        joint.find('origin').set('xyz', fmt(H[:3, 3])); joint.find('origin').set('rpy', fmt(angles(H[:3, :3])))
        for tag in ('axis', 'limit', 'mimic'):
            for e in list(joint.findall(tag)): joint.remove(e)
        E.SubElement(joint, 'axis', xyz=fmt((0, 0, sy)))             # both doors swing forward and outward
        E.SubElement(joint, 'limit', lower='0', upper=f'{OPEN:.6f}', effort='5', velocity='2')
        if s == 'r': E.SubElement(joint, 'mimic', joint='PanelsOpen', multiplier='1', offset='0')
        visual.find('origin').set('xyz', fmt(-hinge)); visual.find('origin').set('rpy', '0 0 0')
    # The LCD and the hinge knuckles, fixed to the waist in the panels' column frame.
    V = frames['l']
    lcd = links.get('body_waist_lcd_link')
    if lcd is None:
        lcd = E.SubElement(root, 'link', name='body_waist_lcd_link')
        j = E.SubElement(root, 'joint', name='body_waist_lcd_fixed', type='fixed')
        E.SubElement(j, 'parent', link=joints['body_waist_blue_panel_l_link'].find('parent').get('link'))
        E.SubElement(j, 'child', link='body_waist_lcd_link'); E.SubElement(j, 'origin', xyz='0 0 0', rpy='0 0 0')
        joints['body_waist_lcd_link'] = j
    joints['body_waist_lcd_link'].find('origin').set('xyz', fmt(V[:3, 3]))
    joints['body_waist_lcd_link'].find('origin').set('rpy', fmt(angles(V[:3, :3])))
    for v in list(lcd.findall('visual')): lcd.remove(v)
    pts = stl_points(links['body_waist_blue_panel_l_link'].find('johnny5_panel_source/visual/geometry/mesh').get('filename'))
    z0, z1 = pts[:, 2].min(), pts[:, 2].max()
    edge_angle = math.atan2(pts[:, 1].max(), pts[np.argmax(pts[:, 1]), 0])
    def add(name, shape, material, xyz=(0, 0, 0)):
        v = E.SubElement(lcd, 'visual', name=name)
        E.SubElement(v, 'origin', xyz=fmt(xyz), rpy='0 0 0')
        E.SubElement(v, 'geometry').append(shape); E.SubElement(v, 'material', name=material)
    bezel = edge_angle - .03
    add('waist_lcd_bezel', mesh('waist_lcd_bezel', curved_strip(SCREEN_R - .0002, -bezel, bezel, z0 + .004, z1 - .004)), BLACK)
    screen = bezel - .05
    add('johnny5_lcd_screen', mesh('waist_lcd_screen', curved_strip(SCREEN_R, -screen, screen, z0 + .011, z1 - .011, 32)), BLACK)
    for sy in (1, -1):                                              # hinge knuckles on each outer edge
        edge = pts[np.argmax(pts[:, 1])] * np.array([1, sy, 1])
        for k, zc in enumerate((z0 + .020, z1 - .020)):
            add(f'waist_panel_hinge_{"l" if sy > 0 else "r"}_{k}', E.Element('cylinder', radius='.0022', length='.024'), LIGHT,
                (edge[0], edge[1], zc))


if __name__ == '__main__':
    update(Path(__file__).resolve().parent.parent / 'johnny5_head.urdf', apply)
