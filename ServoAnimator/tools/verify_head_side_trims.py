"""Verify the installed side trim correction against its archived originals."""
from studio_paths import CONFIG_ROOT, HEAD_CAD, HARDWARE_CAD, SOLUTION_ROOT
import json
import numpy as np
from align_head_side_trims import ROOT,ARCHIVE,STL_DTYPE,indices
checks=0
for model in ['Johnny5Head','Johnny5FullBody']:
 for name in ['static_737373.stl','static_737373_collision_full.stl']:
  rel=f'{model}/Meshes/SimplifiedHead2/{name}';old=(ARCHIVE/rel).read_bytes();new=(CONFIG_ROOT / 'Models/Robots'/rel).read_bytes()
  assert len(old)==len(new) and old[:84]==new[:84];checks+=1
  before=np.frombuffer(old,dtype=STL_DTYPE,offset=84);after=np.frombuffer(new,dtype=STL_DTYPE,offset=84);untouched=np.ones(len(before),bool)
  for solid,shift,edge in [(43,1.10,-164.084),(44,-1.10,148.844)]:
   ids=indices(before,solid);untouched[ids]=False;expected=before[ids].copy();expected['p'][:,:,0]+=shift
   assert np.array_equal(after[ids],expected);checks+=1
   outer=after['p'][ids,:,0].min() if solid==43 else after['p'][ids,:,0].max()
   assert abs(outer-edge)<.06;checks+=1
  assert np.array_equal(before[untouched],after[untouched]);checks+=1
  assert np.array_equal(before['n'],after['n']);checks+=1
  assert np.array_equal(before['p'][:,:,1:],after['p'][:,:,1:]);checks+=1
for name in ['static_737373.stl','static_737373_collision_full.stl']:
 assert (CONFIG_ROOT / f'Models/Robots/Johnny5Head/Meshes/SimplifiedHead2/{name}').read_bytes()==(CONFIG_ROOT / f'Models/Robots/Johnny5FullBody/Meshes/SimplifiedHead2/{name}').read_bytes();checks+=1
print(f'PASS: {checks} trim alignment, exact displacement, unaffected geometry and collision asset assertions.')
