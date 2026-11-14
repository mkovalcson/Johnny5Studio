from studio_paths import preview_directory
from studio_paths import CONFIG_ROOT, HEAD_CAD, HARDWARE_CAD, SOLUTION_ROOT
import xml.etree.ElementTree as ET
import numpy as np
import vtk
from install_head_bolts import ROOT,rotation
model=CONFIG_ROOT / 'Models/Robots/Johnny5Head';doc=ET.parse(model/'johnny5_head.urdf').getroot()
def trans(e):
 M=np.eye(4)
 if e is not None:M[:3,:3]=rotation(np.fromstring(e.get('rpy','0 0 0'),sep=' '));M[:3,3]=np.fromstring(e.get('xyz','0 0 0'),sep=' ')
 return M
T={'base_link':np.eye(4)}
for i in range(30):
 for j in doc.findall('joint'):
  p=j.find('parent').get('link');c=j.find('child').get('link')
  if p in T:T[c]=T[p]@trans(j.find('origin'))
ren=vtk.vtkRenderer();ren.SetBackground(.23,.23,.23)
for link in doc.findall('link'):
 if link.get('name') not in T:continue
 for v in link.findall('visual'):
  m=v.find('geometry/mesh')
  if m is None:continue
  path=model/m.get('filename');r=vtk.vtkSTLReader();r.SetFileName(str(path));r.Update();mapper=vtk.vtkPolyDataMapper();mapper.SetInputConnection(r.GetOutputPort());a=vtk.vtkActor();a.SetMapper(mapper);mat=T[link.get('name')]@trans(v.find('origin'))@np.diag([*np.fromstring(m.get('scale','1 1 1'),sep=' '),1]);vm=vtk.vtkMatrix4x4()
  for x in range(4):
   for y in range(4):vm.SetElement(x,y,mat[x,y])
  a.SetUserMatrix(vm);color=(.7,.7,.7);name=v.get('name')
  if 'delrin_ball' in name:color=(.1,1,.1)
  if name=='neck_static_cad_e9e9eb':color=(1,.1,.1)
  if name=='static_03':color=(.2,.2,1)
  if 'neck_static_black'==name:color=(.04,.04,.04)
  a.GetProperty().SetColor(color);ren.AddActor(a)
w=vtk.vtkRenderWindow();w.SetOffScreenRendering(1);w.SetSize(1000,800);w.AddRenderer(ren);c=ren.GetActiveCamera();c.SetFocalPoint(.18,0,.24);c.SetPosition(.9,0,.26);c.SetViewUp(0,0,1);c.ParallelProjectionOn();c.SetParallelScale(.11);ren.ResetCameraClippingRange();w.Render();f=vtk.vtkWindowToImageFilter();f.SetInput(w);f.Update();p=vtk.vtkPNGWriter();p.SetInputConnection(f.GetOutputPort());p.SetFileName(str(preview_directory('eye-surface-previews') / 'neck-part-identification.png'));p.Write()
