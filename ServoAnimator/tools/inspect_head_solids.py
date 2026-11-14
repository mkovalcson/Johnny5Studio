from studio_paths import CONFIG_ROOT, HEAD_CAD, HARDWARE_CAD, SOLUTION_ROOT
from inspect_head_bolts import read_step
from OCP.TopAbs import TopAbs_SOLID
from OCP.TopExp import TopExp_Explorer
from OCP.Bnd import Bnd_Box
from OCP.BRepBndLib import BRepBndLib
s=read_step(HEAD_CAD / 'SimplifiedHead2.step');e=TopExp_Explorer(s,TopAbs_SOLID);i=0
while e.More():
 b=Bnd_Box();BRepBndLib.Add_s(e.Current(),b);v=b.Get()
 if v[3]-v[0]>30 or v[4]-v[1]>30 or v[5]-v[2]>30:print(i,tuple(round(x,4) for x in v),flush=True)
 i+=1;e.Next()
