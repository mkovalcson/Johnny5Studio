from studio_paths import CONFIG_ROOT, HEAD_CAD, HARDWARE_CAD, SOLUTION_ROOT
from pathlib import Path
import numpy as np
import vtk
from match_head_recesses import STL_DTYPE,ROOT
p=CONFIG_ROOT / 'Models/Robots/Johnny5Head/Meshes/SimplifiedHead2/static_737373.stl'
data=np.fromfile(p,dtype=STL_DTYPE,offset=84)
for i in [43,44]:
 r=vtk.vtkSTLReader();r.SetFileName(str(ROOT/f'tools/head-trim-{i}.stl'));r.Update();dist=vtk.vtkImplicitPolyDataDistance();dist.SetInput(r.GetOutput());b=r.GetOutput().GetBounds();pts=data['p'].astype(float);lo=np.array(b)[[0,2,4]];hi=np.array(b)[[1,3,5]]
 candidate=np.where(np.all((pts>=lo-.025)&(pts<=hi+.025),axis=(1,2)))[0];selected=[]
 for j in candidate:
  test=np.vstack([pts[j],pts[j].mean(0)])
  error=max(abs(dist.EvaluateFunction(q)) for q in test)
  if error<.25:selected.append(j)
  else:print('excluded',j,round(error,3),pts[j].mean(0).round(3).tolist())
 print(i,'candidates',len(candidate),'selected',len(selected),'bounds',pts[selected].reshape(-1,3).min(0),pts[selected].reshape(-1,3).max(0),flush=True)
 np.save(ROOT/f'tools/head-trim-{i}-indices.npy',selected)

