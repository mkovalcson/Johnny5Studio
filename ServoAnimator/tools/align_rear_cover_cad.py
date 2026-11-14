from studio_paths import CONFIG_ROOT, HEAD_CAD, HARDWARE_CAD, SOLUTION_ROOT
import json
import pathlib
import numpy as np
from scipy.spatial import cKDTree
from OCP.BRepMesh import BRepMesh_IncrementalMesh
from OCP.StlAPI import StlAPI_Writer
from inspect_head_bolts import read_step
from match_head_recesses import mesh, ROOT

path = ROOT / 'tools/rear-cover-cad.stl'
shape = read_step(HEAD_CAD / 'rearCoverColor.step')
BRepMesh_IncrementalMesh(shape, .04, False, .15, True).Perform()
writer = StlAPI_Writer(); writer.ASCIIMode = False; writer.Write(shape, str(path))
source = np.unique(mesh(path)[0].round(5), axis=0)
target = np.unique(mesh(CONFIG_ROOT / 'Models/Robots/Johnny5Head/Meshes/RearCover/rear_cover.stl')[0].round(5), axis=0)
tree = cKDTree(target)
best = None
for diagonal in [[1,-1,-1],[-1,1,-1],[-1,-1,1],[1,1,1]]:
    rotation = np.diag(diagonal).astype(float)
    translation = target.mean(0) - source.mean(0) @ rotation.T
    for iteration in range(100):
        transformed = source @ rotation.T + translation
        distances, indices = tree.query(transformed)
        mask = distances < np.quantile(distances, .9)
        a, b = transformed[mask], target[indices[mask]]
        ac, bc = a.mean(0), b.mean(0)
        u, s, vt = np.linalg.svd((a-ac).T @ (b-bc))
        delta = vt.T @ u.T
        if np.linalg.det(delta) < 0:
            vt[-1] *= -1; delta = vt.T @ u.T
        shift = bc - ac @ delta.T
        rotation, translation = delta @ rotation, delta @ translation + shift
    error = float(np.median(tree.query(source @ rotation.T + translation)[0]))
    print(diagonal, 'median', error, 'rotation', rotation.tolist(), 'translation', translation.tolist(), flush=True)
    if best is None or error < best['error']:
        best = {'rotation': rotation.tolist(), 'translation': translation.tolist(), 'error': error}
(ROOT / 'tools/rear-cover-cad-transform.json').write_text(json.dumps(best, indent=2))
