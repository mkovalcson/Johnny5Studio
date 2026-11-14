from studio_paths import CONFIG_ROOT, HEAD_CAD, HARDWARE_CAD, SOLUTION_ROOT
from inspect_head_bolts import *
from OCP.GeomAbs import GeomAbs_Plane
from OCP.BRepMesh import BRepMesh_IncrementalMesh
from OCP.StlAPI import StlAPI_Writer
from match_head_recesses import mesh,ROOT
p=(HARDWARE_CAD / 'SBHCSCREW 0.086-56x0.5625-HX-N_SBHCSCREW 0.086-56x0.5625-HX-N.step');s=read_step(p)
print(list(cylinders(s)))
e=TopExp_Explorer(s,TopAbs_FACE)
while e.More():
 f=TopoDS.Face_s(e.Current());a=BRepAdaptor_Surface(f)
 if a.GetType()==GeomAbs_Plane:
  b=Bnd_Box();BRepBndLib.Add_s(f,b);print('plane',a.Plane().Axis().Direction().Coord(),b.Get())
 e.Next()
BRepMesh_IncrementalMesh(s,.015,False,.14,True).Perform();w=StlAPI_Writer();w.ASCIIMode=False;w.Write(s,str(ROOT/'tools/button-head-source.stl'))
p,n=mesh(ROOT/'tools/button-head-source.stl');print('mesh',len(p)//3,p.min(0),p.max(0))
