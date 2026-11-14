"""Hard-mounted thumb (the single finger) centred between the two paired fingers.

From the CAD drawing: the single finger does not ride a spread axle. It is hard
mounted on the palm, between the two spreading fingers. This focused updater
edits the existing URDF in place (like the other *_details updaters):

* ThumbCurl hangs from the palm (body_seg_hand) instead of the upper spread
  axle, so FingerSpreadUpper no longer swings the thumb. ThumbCurl and
  ThumbTipCurl still curl it toward the fingers.
* The whole thumb slides sideways to lie exactly halfway between the base
  axles of finger 0 and finger 1, so when it curls it passes between them.
* Its palm bar and eye are fixed to the palm and run from the palm's middle
  bolt (between the two spread axles) straight to the thumb's pivot.

Every position is measured from the fingers and the palm, so running it again
changes nothing.
"""
import math
from pathlib import Path
import numpy as np
from hand_cad_details import transform, angles, fmt, update


def apply(root):
    links = {e.get('name'): e for e in root.findall('link')}
    js = {j.find('child').get('link'): j for j in root.findall('joint')}
    by_name = {j.get('name'): j for j in root.findall('joint')}

    def world(n):
        j = js.get(n)
        return np.eye(4) if j is None else world(j.find('parent').get('link')) @ transform(j)

    def place(child, parent, position):
        """Hang `child` from `parent` with its frame at `position` (world), keeping its world rotation."""
        j = js[child]
        R_child = world(child)[:3, :3]
        j.find('parent').set('link', parent)
        P = world(parent)
        j.find('origin').set('xyz', fmt(np.linalg.inv(P)[:3, :3] @ (position - P[:3, 3])))
        j.find('origin').set('rpy', fmt(angles(P[:3, :3].T @ R_child)))

    for side in ('left', 'right'):
        S = side.title()
        hand = f'body_seg_hand_{side}_link'
        thumb = f'body_seg_thumb_{side}_link'
        f0 = world(f'body_finger_base_axle_{side}_0_link')[:3, 3]
        f1 = world(f'body_finger_base_axle_{side}_1_link')[:3, 3]
        across = (f1 - f0) / np.linalg.norm(f1 - f0)                 # finger 0 -> finger 1
        mid = (f0 + f1) / 2

        # 1. thumb on the palm, centred between the fingers
        curl = by_name[f'ThumbCurl{S}']
        axis_world = world(curl.find('parent').get('link'))[:3, :3] @ np.array([float(v) for v in curl.find('axis').get('xyz').split()])
        pivot = world(thumb)[:3, 3]
        pivot = pivot - ((pivot - mid) @ across) * across
        place(thumb, hand, pivot)
        curl.find('axis').set('xyz', fmt(world(hand)[:3, :3].T @ axis_world))

        # 2. its bar and eye, fixed to the palm, from the middle bolt to the pivot
        hole = links[f'body_palm_bolt_hole_{side}_link']
        hole_T = world(hole.get('name')) @ transform(hole.find('visual'))
        bolt, palm_y = hole_T[:3, 3], hole_T[:3, 2]                   # the bolt hole's cylinder axis runs across the palm
        eye = bolt + ((pivot - bolt) @ palm_y) * palm_y              # at the bolt, level with the thumb across the palm
        d = pivot - eye; length = np.linalg.norm(d); z = d / length
        y = palm_y - (palm_y @ z) * z; y /= np.linalg.norm(y); x = np.cross(y, z)
        bar_R = np.column_stack((x, y, z))                          # box: 16 mm wide (x), 8 mm thick across the palm (y)
        eye_R = np.column_stack((x, z, palm_y)) if abs(np.linalg.det(np.column_stack((x, z, palm_y)))) > .5 else bar_R
        if np.linalg.det(eye_R) < 0: eye_R = np.column_stack((z, x, palm_y))   # eye: a cylinder whose axis runs across the palm
        for name, centre, Rv in ((f'body_palm_bar_{side}_thumb_link', (eye + pivot) / 2, bar_R),
                                 (f'body_palm_bar_eye_{side}_thumb_link', eye, eye_R)):
            place(name, hand, centre)
            v = links[name].find('visual')
            v.find('origin').set('rpy', fmt(angles(world(name)[:3, :3].T @ Rv)))
            v.find('origin').set('xyz', '0 0 0')
            box = v.find('geometry/box')
            if box is not None:
                w, t, _ = box.get('size').split()
                box.set('size', f'{w} {t} {length:.6f}')


if __name__ == '__main__':
    update(Path(__file__).resolve().parent.parent / 'johnny5_head.urdf', apply)
