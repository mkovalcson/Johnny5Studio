"""Default spread for the two paired fingers.

With the thumb hard mounted between them, the paired fingers need to sit a
little apart at rest so that, when all three close, the fingers clear the
thumb and each other. This focused updater edits the existing URDF in place
(like the other *_details updaters): each FingerSpread joint's origin is
turned by SPREAD about its own axis, so the spread axle, its palm bar and eye,
and the whole finger rest SPREAD further apart. The positive direction of
both spread joints already moves the fingers away from each other, and the
joint limits stay relative to this new rest pose.

The origin rotation is set outright (not added), so running it again changes
nothing.
"""
from pathlib import Path
import numpy as np
from hand_cad_details import angles, fmt, update

SPREAD = 0.12          # radians, each finger away from the thumb


def axis_rotation(axis, q):
    a = np.array(axis, float); a /= np.linalg.norm(a)
    K = np.array([[0, -a[2], a[1]], [a[2], 0, -a[0]], [-a[1], a[0], 0]])
    return np.eye(3) + np.sin(q) * K + (1 - np.cos(q)) * K @ K


def apply(root):
    by_name = {j.get('name'): j for j in root.findall('joint')}
    for S in ('Left', 'Right'):
        for UL in ('Upper', 'Lower'):
            j = by_name[f'FingerSpread{UL}{S}']
            axis = [float(v) for v in j.find('axis').get('xyz').split()]
            j.find('origin').set('rpy', fmt(angles(axis_rotation(axis, SPREAD))))


if __name__ == '__main__':
    update(Path(__file__).resolve().parents[1] / 'johnny5_head.urdf', apply)
