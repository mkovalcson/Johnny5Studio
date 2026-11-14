from studio_paths import CONFIG_ROOT, HEAD_CAD, HARDWARE_CAD, SOLUTION_ROOT
from inspect_head_bolts import read_step
from OCP.TopAbs import TopAbs_SOLID
from OCP.TopExp import TopExp_Explorer
from OCP.BRepMesh import BRepMesh_IncrementalMesh
from OCP.StlAPI import StlAPI_Writer
from pathlib import Path
s=read_step(HEAD_CAD / 'SimplifiedHead2.step');e=TopExp_Explorer(s,TopAbs_SOLID);i=0
while e.More():
 if i in [43,44]:
  shape=e.Current();BRepMesh_IncrementalMesh(shape,.01,False,.1,True).Perform();w=StlAPI_Writer();w.ASCIIMode=False;w.Write(shape,str(Path(__file__).parent/f'head-trim-{i}.stl'))
 i+=1;e.Next()
