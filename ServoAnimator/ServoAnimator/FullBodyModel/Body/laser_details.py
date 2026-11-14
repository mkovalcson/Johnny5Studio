"""Optional shoulder laser (from the first film), hinged behind the left shoulder.

Every laser part is a link named body_laser_*, so the editor's Hide Laser command can
hide the whole assembly (it detaches those visuals from the scene, so a hidden laser
costs nothing to draw).

* The mount: a cylinder as wide as the body hinge pin's left cap, running from the
  cap's outer face out to 90% of the way to the left shoulder hub's inner face (short of
  it, so the elbow clears the support).
* The support, which moves with the chest (the chest tilts about the hinge pin, the
  mount's axis, so the support stays clamped on the mount) but stands vertical in the
  built pose: a box bolted to the mount's outer end face, a rectangular box above it, a
  rod in a grey bellows coil, a collar, then a link angled back 45 degrees to the laser
  hinge, behind the left shoulder.
* The laser on that hinge, normally angled up 70 degrees. The LaserLower joint
  (0 = up) swings its front forward and down; fully lowered (70 degrees) it lies level
  with its underside resting on top of the shoulder, which sets the hinge height.
  From the reference photos (side and back), back to front: a blue box, then a grey
  top spine over a black coil, then the front section - blue on top, a blue panel on
  each side, a rounded slot - tapering to a small flat tip with the red firing window.
* The red toolbox he normally carries instead (body_laser_toolbox_link), on the same hinge,
  with its black handle hinged and folded over sideways.
  The editor's Toolbox Swap shows the toolbox or the laser; Hide Laser hides everything.

Focused updater: it edits the existing URDF in place and running it again changes
nothing.
"""
import math
from pathlib import Path
import xml.etree.ElementTree as E
import numpy as np
from hand_cad_details import transform, angles, fmt, update
from arm_cad_details import mesh, side_prism
from neck_cad_details import ribbed_cover, rounded_box

GRAY = 'step_steel_satin'; LIGHT = 'cad_b3b3b3'; DARK = 'cad_404040'; BLACK = 'cad_020202'; GOLD = 'cad_e7c863'; RED = 'mouth_red'; TOOL_RED = 'toolbox_red'
LASER_BLUE = 'iris_blue'   # the same blue as the waist panels
UP = math.radians(70)        # the laser's usual angle above the chest's horizontal; lowering this far makes it level
# Laser dimensions (head frame: x along the laser from the hinge, y across, z up when level),
# scaled from the reference photos: 430 mm long, 86 mm wide, 76 mm deep (the blue box 87).
BACK, FRONT, WIDTH, DEPTH = -.095, .335, .086, .076
COIL_END, SPINE = .140, .046      # coil/spine section ends; the spine is the top 30 mm


def apply(root):
    links = {l.get('name'): l for l in root.findall('link')}
    joints = {j.find('child').get('link'): j for j in root.findall('joint')}
    def world(n):
        j = joints.get(n)
        return np.eye(4) if j is None else world(j.find('parent').get('link')) @ transform(j)
    for old in root.findall("material[@name='laser_blue']"): root.remove(old)   # an earlier, lighter laser blue
    # The mount.
    cap = 'body_hinge_pin_cap_l_link'
    cap_visual = links[cap].find('visual'); C = world(cap) @ transform(cap_visual)
    radius = float(cap_visual.find('geometry/cylinder').get('radius'))
    half = float(cap_visual.find('geometry/cylinder').get('length')) / 2
    out = -C[:3, 2] if C[1, 2] < 0 else C[:3, 2]            # the cap's axis, pointing out to the left
    start = C[:3, 3] + out * half                             # the cap's outer face
    hub = links['body_shoulder_hub_left_link'].find("visual[@name='cad_shoulder_left_inner_hub']")
    H = world('body_shoulder_hub_left_link') @ transform(hub)
    hub_end = H[:3, 3] - out * float(hub.find('geometry/cylinder').get('length')) / 2   # the hub's inner face
    REACH = .90                                               # shortened so the elbow clears the support
    length = REACH * float((hub_end - start) @ out)
    centre = start + out * length / 2
    def fixed_link(name, parent, T):
        """A laser link fixed to `parent` with its frame at world pose T; visuals cleared."""
        if name not in links:
            links[name] = E.SubElement(root, 'link', name=name)
            j = E.SubElement(root, 'joint', name=name.replace('_link', '_fixed'), type='fixed')
            E.SubElement(j, 'parent', link=parent); E.SubElement(j, 'child', link=name); E.SubElement(j, 'origin', xyz='0 0 0', rpy='0 0 0')
            joints[name] = j
        P = np.linalg.inv(world(joints[name].find('parent').get('link'))) @ T
        joints[name].find('origin').set('xyz', fmt(P[:3, 3])); joints[name].find('origin').set('rpy', fmt(angles(P[:3, :3])))
        for v in list(links[name].findall('visual')): links[name].remove(v)
        return links[name]
    def add(link, label, shape, material, xyz=(0, 0, 0), rpy=(0, 0, 0)):
        v = E.SubElement(link, 'visual', name='body_laser_' + label)
        E.SubElement(v, 'origin', xyz=fmt(xyz), rpy=fmt(rpy))
        E.SubElement(v, 'geometry').append(shape); E.SubElement(v, 'material', name=material)
    M = C.copy(); M[:3, 3] = centre                           # same orientation as the cap: axis along the pin
    mount = fixed_link('body_laser_mount_link', joints[cap].find('parent').get('link'), M)
    add(mount, 'mount_visual', E.Element('cylinder', radius=f'{radius:.6f}', length=f'{length:.6f}'), GRAY)

    # The support and laser are fixed to the chest but laid out upright: in the built pose,
    # with the chest leaning forward, the column stands vertical and the laser's 70 degrees
    # and its lowered "level" are measured from the true horizontal. Frame F: world axes
    # (x forward, y left, z up) at the built pose.
    pin = world('body_hinge_pin_link')[:3, 3]; shoulder = world('body_seg_shoulder_left_link')[:3, 3]
    hub_r = float(hub.find('geometry/cylinder').get('radius'))
    end_face = float((start + out * length)[1])               # the mount's outer end face
    box_depth = .026
    y = end_face + box_depth / 2                              # bolted to the mount's outer end face
    x = pin[0] + .030                                         # a little ahead of the pin axis, as in the photo
    rise = .043                                               # the 45-degree link
    # The hinge sits on the laser's underside, so it goes just above the top of the shoulder
    # hub: lowered level, the laser rests on it.
    hinge = np.array([x - rise, y, shoulder[2] + hub_r + .0005])
    z0 = pin[2] - .036
    # The column's parts keep the reference photo's proportions (box 74, rectangular box 101,
    # coil 125, collar 15 mm); all but the box are scaled to reach the hinge.
    stack = [('box', .074, (.055, box_depth)), ('upper_box', .101, (.039, .026)), ('coil', .125, None), ('collar', .015, None)]
    scale = (hinge[2] - rise - z0 - stack[0][1]) / sum(h for _, h, _ in stack[1:])
    support = fixed_link('body_laser_support_link', 'body_seg_chest_link', np.eye(4))
    z = z0
    for label, h, size in stack:
        if label != 'box': h *= scale
        if size:
            add(support, label, mesh('laser_' + label, rounded_box((size[0], size[1], h), .003)), LIGHT, (x, y, z + h / 2))
        elif label == 'coil':
            add(support, 'rod', E.Element('cylinder', radius='.0050', length=f'{h + .010:.6f}'), LIGHT, (x, y, z + h / 2))
            add(support, 'coil', mesh('laser_coil', ribbed_cover(h, .0135, 18)), DARK, (x, y, z + h / 2))
        else:
            add(support, 'collar', E.Element('cylinder', radius='.0160', length=f'{h:.6f}'), LIGHT, (x, y, z + h / 2))
        z += h
    top = np.array([x, y, z])
    add(support, 'arm', mesh('laser_arm', rounded_box((.016, .022, rise * math.sqrt(2) + .012), .003)), LIGHT,
        (top + hinge) / 2, (0, -math.pi / 4, 0))
    add(support, 'hinge_knuckle', E.Element('cylinder', radius='.0110', length='.034'), DARK, hinge, (math.pi / 2, 0, 0))
    add(support, 'hinge_pin', E.Element('cylinder', radius='.0040', length='.042'), LIGHT, hinge, (math.pi / 2, 0, 0))

    # LaserLower: a hinge across the body (local y). 0 = the laser up at 70 degrees;
    # positive swings its front forward and down, level at 70 degrees.
    head = 'body_laser_head_link'
    if head not in links:
        links[head] = E.SubElement(root, 'link', name=head)
        j = E.SubElement(root, 'joint', name='LaserLower', type='revolute')
        E.SubElement(j, 'parent', link='body_laser_support_link'); E.SubElement(j, 'child', link=head)
        E.SubElement(j, 'origin', xyz='0 0 0', rpy='0 0 0'); E.SubElement(j, 'axis', xyz='0 1 0')
        E.SubElement(j, 'limit', lower='0', upper='1', effort='5', velocity='1')
        joints[head] = j
    laser = links[head]
    for v in list(laser.findall('visual')): laser.remove(v)
    joints[head].find('origin').set('xyz', fmt(hinge)); joints[head].find('origin').set('rpy', fmt((0, -UP, 0)))
    joints[head].find('limit').set('upper', f'{UP:.6f}')

    w = WIDTH / 2
    # Blue box at the back end (slightly deeper than the rest).
    add(laser, 'blue_box', mesh('laser_blue_box', rounded_box((-BACK, WIDTH + .002, DEPTH + .011), .006)), LASER_BLUE,
        (BACK / 2, 0, (DEPTH + .011) / 2 - .006))
    # Grey top spine over the black coil.
    add(laser, 'spine', mesh('laser_spine', rounded_box((COIL_END, WIDTH, DEPTH - SPINE), .003)), LIGHT,
        (COIL_END / 2, 0, (SPINE + DEPTH) / 2))
    coil_r = (SPINE - .001) / 2
    add(laser, 'barrel_coil', mesh('laser_barrel_coil', ribbed_cover(COIL_END, coil_r, 22)), BLACK,
        (COIL_END / 2, 0, coil_r + .001), (0, math.pi / 2, 0))
    add(laser, 'back_panel', mesh('laser_back_panel', rounded_box((.025, .025, .002), .003)), LIGHT,
        (COIL_END - .030, -.012, DEPTH + .0005))
    # Front section (CAD/photo profile): the top slopes down to the tip at 20 degrees; the
    # bottom angles up at about 15 degrees, then breaks to a steeper 35-degree rise; the tip
    # is a small flat face with the red firing window in its centre. Grey below the spine
    # line, blue above it.
    TIP = (.049, .060)                                        # tip face, bottom and top
    top_start = FRONT - (DEPTH - TIP[1]) / math.tan(math.radians(20))
    red, brk = FRONT - .095, (FRONT - .055, .011)
    def bottom(xx):                                           # underside height along the front
        if xx <= red: return 0.0
        if xx <= brk[0]: return brk[1] * (xx - red) / (brk[0] - red)
        return brk[1] + (TIP[0] - brk[1]) * (xx - brk[0]) / (FRONT - brk[0])
    x_split = brk[0] + (SPINE - brk[1]) * (FRONT - brk[0]) / (TIP[0] - brk[1])
    lower = [(COIL_END, 0), (red, 0), brk, (x_split, SPINE), (COIL_END, SPINE)]
    upper = [(COIL_END, SPINE), (x_split, SPINE), (FRONT, TIP[0]), (FRONT, TIP[1]), (top_start, DEPTH), (COIL_END, DEPTH)]
    add(laser, 'front', mesh('laser_front', side_prism(lower, -w, w)), LIGHT)
    add(laser, 'front_top', mesh('laser_front_top', side_prism(upper, -w, w)), LASER_BLUE)
    # A blue panel on each side of the front, following the underside, and a rounded slot behind it.
    edge = SPINE - .003
    x_edge = brk[0] + (edge - .004 - brk[1]) * (FRONT - brk[0]) / (TIP[0] - brk[1])
    panel = [(FRONT - .125, .004), (red - .002, .004), (brk[0] - .002, brk[1] + .004), (x_edge, edge), (FRONT - .100, edge)]
    slot = [(COIL_END + .012 + .007 * math.cos(a), .022 + .007 * math.sin(a)) for a in np.linspace(math.pi / 2, 3 * math.pi / 2, 9)] +            [(COIL_END + .062 + .007 * math.cos(a), .022 + .007 * math.sin(a)) for a in np.linspace(-math.pi / 2, math.pi / 2, 9)]
    tris_p, tris_s = [], []
    for s in (1, -1):
        y0, y1 = sorted((s * w, s * (w + .0008)))
        tris_p += side_prism(panel, y0, y1); tris_s += side_prism(slot, y0, y1)
    add(laser, 'side_panels', mesh('laser_side_panels', tris_p), LASER_BLUE)
    add(laser, 'side_slots', mesh('laser_side_slots', tris_s), DARK)
    add(laser, 'firing_window', E.Element('box', size='.0015 .016 .006'), RED, (FRONT + .0005, 0, sum(TIP) / 2))

    # The red toolbox he normally carries instead of the laser (the editor's Toolbox Swap shows
    # one or the other). It hangs on the same hinge, the middle of its underside on the hinge
    # (both ways), so Laser Lower moves it the same way: 70 degrees up, or level on the shoulder.
    if root.find(f"material[@name='{TOOL_RED}']") is None:
        m = E.SubElement(root, 'material', name=TOOL_RED); E.SubElement(m, 'color', rgba='0.80 0.06 0.06 1')
    tool = fixed_link('body_laser_toolbox_link', head, world(head))
    length, depth, width = .400, .100, .170
    inward = 0                                              # centred across the hinge
    back = -length / 2; mid = 0                             # and along it
    add(tool, 'toolbox', mesh('laser_toolbox', rounded_box((length, width, depth), .010)), TOOL_RED, (mid, inward, depth / 2))
    for s in (1, -1):                                        # the lid line on each side
        add(tool, f'toolbox_lid_line_{"l" if s > 0 else "r"}', E.Element('box', size=f'{length - .004:.4f} .0006 .0015'), BLACK,
            (mid, inward + s * (width / 2 + .0002), depth * .72))
    # The black handle on the top face: a U on two hinges (bracket and pin, along the box),
    # folded over sideways, outward, to lie 10 degrees up from flush with the top.
    FOLD, arm = math.radians(10), .026
    pivot = np.array([mid, inward, depth + .006])
    d = np.array([0, math.cos(FOLD), math.sin(FOLD)])       # from the hinge pins out to the grip
    for k, dx in enumerate((-.060, .060)):
        at = pivot + (dx, 0, 0)
        add(tool, f'toolbox_handle_hinge_{k}', mesh('laser_toolbox_handle_hinge', rounded_box((.022, .016, .006), .002)), BLACK,
            (at[0], inward, depth + .003))
        add(tool, f'toolbox_handle_pin_{k}', E.Element('cylinder', radius='.0035', length='.024'), LIGHT, at, (0, math.pi / 2, 0))
        add(tool, f'toolbox_handle_arm_{k}', mesh('laser_toolbox_handle_arm', rounded_box((.010, arm, .008), .003)), BLACK,
            at + d * arm / 2, (FOLD, 0, 0))
    add(tool, 'toolbox_handle_grip', mesh('laser_toolbox_handle_grip', rounded_box((.130, .012, .010), .004)), BLACK,
        pivot + d * arm, (FOLD, 0, 0))
    add(tool, 'toolbox_latch', mesh('laser_toolbox_latch', rounded_box((.020, .026, .010), .002)), TOOL_RED,
        (back + length - .022, inward, depth + .004))

if __name__ == '__main__':
    update(Path(__file__).resolve().parent.parent / 'johnny5_head.urdf', apply)
