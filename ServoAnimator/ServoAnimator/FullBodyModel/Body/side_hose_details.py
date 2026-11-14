"""Right side: the hinge pin cap runs out to the side unit, and the side hose drops straight onto it.

* The right hinge pin cap (body_hinge_pin_cap_r_link) becomes a cylinder from its inner face out
  along the pin to the outer face of the side unit (body_side_unit_link), mirroring the laser
  mount on the left. Its outer rim is chamfered at 45 degrees, half way back to the hose.
* The side hose (body_side_hose_*) runs from the middle of the side unit's underside down and
  back to that cylinder, staying in the side unit's plane instead of angling in toward the chest.
  Its discs keep their sizes and count; the hose ends on the cylinder's surface.

Focused updater: it edits the existing URDF in place and running it again changes nothing.
"""
import math
from pathlib import Path
import numpy as np
from hand_cad_details import transform, angles, fmt, update
from arm_cad_details import mesh

CAP_R = .040                                                         # build_robot.py: the hinge pin caps' radius


def lathe(profile, out=1, sides=48):
    """A closed solid of revolution about z: `profile` lists (z, r) from the inner end to the outer end
    (z flipped when out = -1); both ends are capped flat."""
    pts = [(out * z, r) for z, r in profile]
    ring = lambda z, r: [np.array((r * math.cos(2 * math.pi * i / sides), r * math.sin(2 * math.pi * i / sides), z)) for i in range(sides)]
    rings = [ring(z, r) for z, r in pts]
    tris = []
    for a, b in zip(rings, rings[1:]):
        for i in range(sides):
            j = (i + 1) % sides
            tris += [(a[i], a[j], b[j]), (a[i], b[j], b[i])] if out > 0 else [(a[i], b[j], a[j]), (a[i], b[i], b[j])]
    for (z, _), r, flip in ((pts[0], rings[0], out > 0), (pts[-1], rings[-1], out < 0)):
        c = np.array((0, 0, z))
        for i in range(sides):
            j = (i + 1) % sides
            tris.append((c, r[j], r[i]) if flip else (c, r[i], r[j]))
    return tris


def apply(root):
    links = {l.get('name'): l for l in root.findall('link')}
    joints = {j.find('child').get('link'): j for j in root.findall('joint')}
    def world(n):
        j = joints.get(n)
        return np.eye(4) if j is None else world(j.find('parent').get('link')) @ transform(j)
    def place(link, element, T):
        """Set a visual/collision origin so it sits at world pose T."""
        P = np.linalg.inv(world(link)) @ T
        o = element.find('origin')
        o.set('xyz', fmt(P[:3, 3])); o.set('rpy', fmt(angles(P[:3, :3])))

    # The side unit's outer face and the middle of its underside.
    unit = links['body_side_unit_link'].find('visual')
    U = world('body_side_unit_link') @ transform(unit)
    size = [float(v) for v in unit.find('geometry/box').get('size').split()]
    outer_y = U[1, 3] - abs(U[1, 1]) * size[1] / 2                  # the robot's right is -y
    top = U[:3, 3] - U[:3, 2] * size[2] / 2                          # where the hose leaves the unit

    # The cap: from its inner face out to the side unit's outer face, its outer rim chamfered at 45 degrees
    # half way back to where the hose meets it.
    cap = 'body_hinge_pin_cap_r_link'
    visual = links[cap].find('visual')
    C = world(cap) @ transform(visual)
    axis = C[:3, 3]
    inner_y = -.074                                                  # build_robot.py: cap centre -0.078, 8 mm long
    length = inner_y - outer_y
    chamfer = (U[1, 3] - outer_y) / 2                                # the hose meets it at the side unit's centre plane
    T = C.copy(); T[:3, 3] = (axis[0], (inner_y + outer_y) / 2, axis[2])
    out = 1 if C[1, 2] < 0 else -1                                   # which way the cap's local z points: out is -y
    geometry = visual.find('geometry')
    for child in list(geometry): geometry.remove(child)
    geometry.append(mesh('hinge_pin_cap_r_chamfered', lathe([(-length / 2, CAP_R), (length / 2 - chamfer, CAP_R), (length / 2, CAP_R - chamfer)], out)))
    place(cap, visual, T)

    # The hose: in the plane y = side unit centre, from the underside down to the cylinder, keeping its
    # original fall (down and back), ending where its centre line meets the cylinder's surface.
    hose = sorted(n for n in links if n.startswith('body_side_hose_'))
    first = world(hose[0]) @ transform(links[hose[0]].find('visual'))
    d = -first[:3, 2] if first[2, 2] > 0 else first[:3, 2]           # current hose direction (downward)
    d = np.array([d[0], 0, d[2]]); d /= np.linalg.norm(d)            # no sideways lean
    start = np.array([top[0], U[1, 3], top[2]])
    # Solve |start + t d - axis|_xz = radius for the first hit.
    rel = np.array([start[0] - axis[0], start[2] - axis[2]]); dd = np.array([d[0], d[2]])
    b = rel @ dd; c = rel @ rel - CAP_R ** 2
    t = -b - math.sqrt(max(0.0, b * b - c))
    end = start + d * t
    n = len(hose); step = t / n
    z = d; x = np.cross([0, 1, 0], z); x /= np.linalg.norm(x); y = np.cross(z, x)
    for i, name in enumerate(hose):
        H = np.eye(4); H[:3, 0], H[:3, 1], H[:3, 2] = x, y, z
        H[:3, 3] = start + d * step * (i + .5)
        for e in links[name]:
            if e.tag in ('visual', 'collision') and e.find('geometry/cylinder') is not None:
                e.find('geometry/cylinder').set('length', f'{step * 1.02:.6f}')
                place(name, e, H)
    return dict(cap_length=length, hose_start=start, hose_end=end)


if __name__ == '__main__':
    update(Path(__file__).resolve().parent.parent / 'johnny5_head.urdf', apply)
