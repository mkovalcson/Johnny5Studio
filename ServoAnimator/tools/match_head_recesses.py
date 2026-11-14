"""Map CAD cylindrical recesses to the existing articulated mesh visuals."""
from studio_paths import CONFIG_ROOT, HEAD_CAD, HARDWARE_CAD, SOLUTION_ROOT
import collections
import json
import pathlib
import numpy as np

ROOT = pathlib.Path(__file__).resolve().parents[1]
DATA = json.loads((ROOT / 'tools/head-bolt-analysis.json').read_text())
STL_DTYPE = np.dtype([('n', '<f4', (3,)), ('p', '<f4', (3, 3)), ('a', '<u2')])


def mesh(path):
    data = np.fromfile(path, dtype=STL_DTYPE, offset=84)
    return data['p'].reshape(-1, 3).astype(float), np.repeat(data['n'].astype(float), 3, axis=0)


if __name__ == '__main__':
    result = []
    for step, folder in [('SimplifiedHead2.step', 'SimplifiedHead2'), ('EyeMechanism.step', 'EyeMechanism'),
                         ('rearCover.STEP', 'RearCover'), ('SimpleMouth.step', 'SimpleMouth')]:
        files = [(p, *mesh(p)) for p in (CONFIG_ROOT / 'Models/Robots/Johnny5Head/Meshes' / folder).glob('*.stl') if 'collision' not in p.name]
        for index, face in enumerate(DATA[step]['cylinders']):
            if not face['inward'] or not (1.77 < face['radius'] < 5.0) or not (.5 < abs(face['v'][1] - face['v'][0]) < 12):
                continue
            center, axis = np.array(face['center']), np.array(face['axis'])
            if folder == 'RearCover':
                transform = json.loads((ROOT / 'tools/rear-cover-cad-transform.json').read_text())
                rotation, translation = np.array(transform['rotation']), np.array(transform['translation'])
                center = rotation @ center + translation; axis = rotation @ axis
                face = {**face, 'center': center.tolist(), 'axis': axis.tolist()}
            matches = []
            for path, points, normals in files:
                relative = points - center
                v = relative @ axis
                radial = relative - v[:, None] * axis
                radius = np.linalg.norm(radial, axis=1)
                inward = np.sum(normals * radial, axis=1) < -.6 * face['radius']
                tolerance = .12 if folder == 'RearCover' else .025
                mask = (np.abs(radius - face['radius']) < tolerance) & (v >= min(face['v']) - tolerance) & (v <= max(face['v']) + tolerance) & inward
                count = int(np.count_nonzero(mask))
                if count >= 12:
                    matches.append((path.relative_to(CONFIG_ROOT / 'Models/Robots/Johnny5Head').as_posix(), count))
            if matches:
                row = {'step': step, 'face': index, **face, 'matches': matches}
                result.append(row)
                print(step, index, round(face['radius'],4), round(abs(face['v'][1]-face['v'][0]),3), matches, flush=True)
        print('Done', step, flush=True)
    (ROOT / 'tools/head-recess-matches.json').write_text(json.dumps(result, indent=2))
    print('Matched surfaces:', len(result), collections.Counter(row['step'] for row in result))
