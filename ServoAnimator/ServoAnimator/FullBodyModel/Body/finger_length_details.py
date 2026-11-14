"""Finger and thumb segment lengths and closing range from the closed-hand CAD drawing.

Estimated from the drawing (pivot centres scaled by the blue joint domes, ~23 mm):

* paired fingers: base pivot -> middle knuckle 50 mm, middle knuckle -> bend 38 mm
* single finger (thumb): base pivot -> bend 54 mm
* the segments beyond the bends keep their lengths

With the thumb hard-mounted between the fingers (thumb_mount_details.py) the
digits run in separate lanes and cannot meet, so the closing range is set by
the drawing's closed pose instead of a shared stop plane: the fingers curl over
the top of the thumb (middle knuckle ~0.92 rad, end ~0.53 rad) while the thumb
hooks toward them (tip ~1.15 rad).

This focused updater moves each downstream pivot along its segment to the
target length, lengthens the segment's saved bar (the source of the CAD
details) to match and sets the closing limits. Run hand_cad_details.py after it
to rebuild the bars' CAD details. Running it again changes nothing.
"""
from pathlib import Path
import numpy as np
from hand_cad_details import transform, angles, fmt, update, vec

FINGER_PROX, FINGER_DIST, THUMB_PROX = 0.050, 0.038, 0.054
LIMITS = {'FingerCurl{}': 0.8, 'FingerTipCurl{}': 1.0, 'FingerEndCurl{}': 0.8, 'ThumbCurl': 0.8, 'ThumbTipCurl': 1.3}


def apply(root):
    links = {e.get('name'): e for e in root.findall('link')}
    js = {j.find('child').get('link'): j for j in root.findall('joint')}
    by_name = {j.get('name'): j for j in root.findall('joint')}

    def world(n):
        j = js.get(n)
        return np.eye(4) if j is None else world(j.find('parent').get('link')) @ transform(j)

    def pivot(joint):
        return world(by_name[joint].find('child').get('link'))[:3, 3]

    def set_length(start_joint, end_joint, bar, length):
        a, b = pivot(start_joint), pivot(end_joint)
        u = (b - a) / np.linalg.norm(b - a); b_new = a + u * length
        # downstream pivot: everything beyond it rides along
        j = by_name[end_joint]; P = world(j.find('parent').get('link'))
        j.find('origin').set('xyz', fmt(np.linalg.inv(P)[:3, :3] @ (b_new - P[:3, 3])))
        # the bar spans the two pivots: recentre it and set its saved (and current) box length
        bj = js[bar]; P = world(bj.find('parent').get('link'))
        bj.find('origin').set('xyz', fmt(np.linalg.inv(P)[:3, :3] @ ((a + b_new) / 2 - P[:3, 3])))
        for box in links[bar].findall('.//geometry/box'):
            sx, sy, old = vec(box.get('size'))
            if abs(old - np.linalg.norm(b - a)) < 0.004:            # the bar itself, not a CAD detail
                box.set('size', fmt((sx, sy, length)))

    for side in ('left', 'right'):
        S = side.title()
        for fi, UL in ((0, 'Upper'), (1, 'Lower')):
            set_length(f'FingerCurl{UL}{S}', f'FingerTipCurl{UL}{S}', f'body_finger_prox_{side}_{fi}_link', FINGER_PROX)
            set_length(f'FingerTipCurl{UL}{S}', f'FingerEndCurl{UL}{S}', f'body_finger_dist_{side}_{fi}_link', FINGER_DIST)
        set_length(f'ThumbCurl{S}', f'ThumbTipCurl{S}', f'body_thumb_prox_{side}_link', THUMB_PROX)
        for pattern, upper in LIMITS.items():
            for name in ([pattern.format(UL + S) for UL in ('Upper', 'Lower')] if '{}' in pattern else [pattern + S]):
                by_name[name].find('limit').set('upper', f'{upper:.6f}')


if __name__ == '__main__':
    update(Path(__file__).resolve().parent.parent / 'johnny5_head.urdf', apply)
