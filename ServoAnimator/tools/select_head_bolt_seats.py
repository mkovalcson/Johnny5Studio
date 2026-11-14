import json
import pathlib
import numpy as np

ROOT = pathlib.Path(__file__).resolve().parents[1]
DATA = json.loads((ROOT / 'tools/head-bolt-analysis.json').read_text())
MATCHES = json.loads((ROOT / 'tools/head-recess-matches.json').read_text())

def seat(face):
    rear = face['step'] == 'rearCover.STEP'
    if rear:
        original = DATA[face['step']]['cylinders'][face['face']]
        original = {**original, 'step': face['step']}
        result = _seat(original)
        if result is None: return None
        transform = json.loads((ROOT / 'tools/rear-cover-cad-transform.json').read_text())
        rotation, translation = np.array(transform['rotation']), np.array(transform['translation'])
        outer, outward, ends = result
        return rotation @ outer + translation, rotation @ outward, ends
    return _seat(face)

def _seat(face):
    origin, axis = np.array(face['center']), np.array(face['axis'])
    lo, hi = sorted(face['v'])
    ends = [[], []]
    for bore in DATA[face['step']]['cylinders']:
        if not bore['inward'] or not (.6 < bore['radius'] < face['radius'] * .85):
            continue
        direction = np.array(bore['axis'])
        if abs(direction @ axis) < .9999:
            continue
        delta = np.array(bore['center']) - origin
        if np.linalg.norm(delta - (delta @ axis) * axis) > .03:
            continue
        b1, b2 = sorted((delta @ axis + v * (direction @ axis)) for v in bore['v'])
        if b1 < lo - .1 and abs(b2 - lo) < .04:
            ends[0].append(bore['radius'])
        if b2 > hi + .1 and abs(b1 - hi) < .04:
            ends[1].append(bore['radius'])
    if bool(ends[0]) == bool(ends[1]):
        return None
    outward = axis if ends[0] else -axis
    outer = origin + axis * (hi if ends[0] else lo)
    return outer, outward, ends

if __name__ == '__main__':
    accepted, rejected = [], []
    for face in MATCHES:
        if face['u'][1] - face['u'][0] < 6.2:
            rejected.append({**face, 'reason': 'partial arc / rounded corner, not a complete bolt seat'})
            continue
        result = seat(face)
        if result is None:
            rejected.append({**face, 'reason': 'no unambiguous concentric smaller through bore'})
            continue
        outer, outward, ends = result
        if not (1.77 < face['radius'] < 5.0):
            rejected.append({**face, 'reason': 'outside fastener size range', 'outer': outer.tolist(), 'outward': outward.tolist()})
            continue
        row = {**face, 'outer': outer.tolist(), 'outward': outward.tolist(), 'shaft_radii': ends}
        accepted.append(row)
        print(face['step'], face['face'], round(face['radius'],4), np.round(outer,3).tolist(), np.round(outward,3).tolist(), face['matches'])
    (ROOT / 'tools/head-bolt-seats.json').write_text(json.dumps({'accepted': accepted, 'rejected': rejected}, indent=2))
    print('Accepted',len(accepted), 'Rejected',len(rejected))
