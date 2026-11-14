"""Separate existing neck solids for independent finish assignment; retain CAD/collisions."""
from studio_paths import ARCHIVE_ROOT, DOCUMENTATION_ROOT
from studio_paths import CONFIG_ROOT, HEAD_CAD, HARDWARE_CAD, SOLUTION_ROOT
from pathlib import Path
import re
import shutil
import struct
from collections import Counter
from scipy.sparse import coo_matrix
from scipy.sparse.csgraph import connected_components
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
DT = np.dtype([('n', '<f4', (3,)), ('p', '<f4', (3, 3)), ('a', '<u2')])

for robot in ('Johnny5Head', 'Johnny5FullBody'):
    folder = CONFIG_ROOT / 'Models/Robots' / robot
    urdf = folder / 'johnny5_head.urdf'
    meshes = folder / 'Meshes/NeckAssembly/StepColors'
    source = meshes / 'neck_static_black.stl'
    original = source.read_bytes()
    triangles = np.frombuffer(original, dtype=DT, offset=84)
    # CAD Y is the neck axis. The two rigid solids end at the bellows' seat.
    vertices, indices = np.unique(np.round(triangles['p'].reshape(-1, 3), 4), axis=0, return_inverse=True)
    faces = indices.reshape(-1, 3)
    graph = coo_matrix((np.ones(faces.size), (faces.ravel(), faces[:, [1, 2, 0]].ravel())), shape=(len(vertices), len(vertices)))
    count, labels = connected_components(graph, directed=False)
    assert count == 3
    rubber_solids = [i for i in range(count) if vertices[labels == i, 1].min() > 117.9]
    assert len(rubber_solids) == 1
    rubber = labels[faces[:, 0]] == rubber_solids[0]
    assert rubber.sum() == 6552 and (~rubber).sum() == 4900
    assert triangles['p'][~rubber, :, 1].max() < 117.911
    for filename, mask in (('neck_bellows_black.stl', rubber), ('neck_rigid_support.stl', ~rubber)):
        data = triangles[mask]
        (meshes / filename).write_bytes(original[:80] + struct.pack('<I', len(data)) + data.tobytes())
    # Verify an exact partition of the source triangles, including normals.
    records = lambda data: Counter(data[i:i+50] for i in range(0, len(data), 50))
    assert records(triangles[rubber].tobytes()) + records(triangles[~rubber].tobytes()) == records(triangles.tobytes())
    text = urdf.read_text(encoding='utf-8')
    match = re.search(r'    <visual name="neck_static_black">.*?</visual>', text, re.S)
    assert match
    if 'name="neck_rigid_support"' not in text:
        backup = ARCHIVE_ROOT / 'neck-support-finish-20261104' / robot
        backup.mkdir(parents=True, exist_ok=True)
        shutil.copy2(urdf, backup / urdf.name)
        visual = match.group()
        bellows = visual.replace('neck_static_black.stl', 'neck_bellows_black.stl')
        support = visual.replace('name="neck_static_black"', 'name="neck_rigid_support"').replace('neck_static_black.stl', 'neck_rigid_support.stl')
        text = text[:match.start()] + bellows + '\n' + support + text[match.end():]
        urdf.write_text(text, encoding='utf-8', newline='\n')
    print(robot, '6552 bellows triangles; 4900 rigid support triangles; original collision mesh retained')
