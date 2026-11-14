"""Align the two side trim outer edges, preserving original CAD and other triangles."""
from studio_paths import ARCHIVE_ROOT, DOCUMENTATION_ROOT
from studio_paths import CONFIG_ROOT, HEAD_CAD, HARDWARE_CAD, SOLUTION_ROOT
import hashlib,json,shutil
from pathlib import Path
import numpy as np
import vtk
from match_head_recesses import ROOT,STL_DTYPE
ARCHIVE=ARCHIVE_ROOT / 'head-side-trim-alignment-20261103'

def indices(data,solid):
 r=vtk.vtkSTLReader();r.SetFileName(str(ROOT/f'tools/head-trim-{solid}.stl'));r.Update()
 surface=vtk.vtkImplicitPolyDataDistance();surface.SetInput(r.GetOutput())
 b=np.array(r.GetOutput().GetBounds());lo=b[[0,2,4]];hi=b[[1,3,5]];points=data['p'].astype(float)
 candidates=np.where(np.all((points>=lo-.025)&(points<=hi+.025),axis=(1,2)))[0]
 return np.array([j for j in candidates if max(abs(surface.EvaluateFunction(q)) for q in np.vstack([points[j],points[j].mean(0)]))<.25],dtype=int)

def main():
 assert not ARCHIVE.exists(),'Already installed; inspect backup before rerunning'
 pending=[];report=[]
 for model in ['Johnny5Head','Johnny5FullBody']:
  for name in ['static_737373.stl','static_737373_collision_full.stl']:
   path=CONFIG_ROOT / f'Models/Robots/{model}/Meshes/SimplifiedHead2/{name}'
   raw=path.read_bytes();data=np.frombuffer(raw,dtype=STL_DTYPE,offset=84).copy();before=data.copy();selected=[]
   for solid,shift in [(43,1.10),(44,-1.10)]:
    ids=indices(before,solid);assert len(ids)>=30,(model,name,solid,len(ids));selected.extend(ids.tolist());data['p'][ids,:,0]+=shift
    report.append({'model':model,'mesh':name,'cadSolid':solid,'triangles':len(ids),'shiftCadMillimetres':[shift,0,0], 'outerXBeforeMm':float(before['p'][ids,:,0].min() if solid==43 else before['p'][ids,:,0].max()),'outerXAfterMm':float(data['p'][ids,:,0].min() if solid==43 else data['p'][ids,:,0].max())})
   assert len(selected)==len(set(selected));remaining=np.ones(len(data),bool);remaining[selected]=False
   assert np.array_equal(data[remaining],before[remaining]);assert np.array_equal(data['n'],before['n'])
   changed=raw[:84]+data.tobytes();assert len(changed)==len(raw)
   pending.append((path,raw,changed))
 for item in report:print(item,flush=True)
 ARCHIVE.mkdir(parents=True)
 for path,raw,changed in pending:
  rel=path.relative_to(CONFIG_ROOT / 'Models/Robots');save=ARCHIVE/rel;save.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(path,save)
  path.write_bytes(changed)
  # Every vertex outside the two selected source solids remains byte-for-byte unchanged.
  assert path.read_bytes()==changed
 (ARCHIVE/'alignment-report.json').write_text(json.dumps(report,indent=2))
 print('Updated four mesh assets; originals archived.',flush=True)
if __name__=='__main__':main()

