from studio_paths import preview_directory
from studio_paths import CONFIG_ROOT, HEAD_CAD, HARDWARE_CAD, SOLUTION_ROOT
from pathlib import Path
import vtk
root=SOLUTION_ROOT; models=CONFIG_ROOT / 'Models/Robots/Johnny5Head/Meshes'
out=preview_directory('head-alignment-previews');out.mkdir(exist_ok=True)
colors={'020202':(.02,.02,.02),'080b4e':(.025,.04,.30),'737373':(.65,.65,.65),'97a3da':(.6,.68,.85),'e7c863':(.9,.77,.36),'f6f4e9':(.94,.94,.93)}
ren=vtk.vtkRenderer();ren.SetBackground(.26,.26,.26)
for p in (models/'SimplifiedHead2').glob('static*.stl'):
 if 'collision' in p.name:continue
 r=vtk.vtkSTLReader();r.SetFileName(str(p));r.Update();m=vtk.vtkPolyDataMapper();m.SetInputConnection(r.GetOutputPort());a=vtk.vtkActor();a.SetMapper(m);a.GetProperty().SetColor(colors[p.stem[7:]]);ren.AddActor(a)
r=vtk.vtkSTLReader();r.SetFileName(str(models/'RearCover/rear_cover.stl'));m=vtk.vtkPolyDataMapper();m.SetInputConnection(r.GetOutputPort());a=vtk.vtkActor();a.SetMapper(m);a.SetPosition(0,-1.575,0);a.GetProperty().SetColor(.7,.7,.7);ren.AddActor(a)
w=vtk.vtkRenderWindow();w.SetOffScreenRendering(1);w.SetSize(900,650);w.AddRenderer(ren)
for name,side in [('whip',-1),('opposite',1)]:
 cam=ren.GetActiveCamera();cam.SetFocalPoint(side*135,5,116);cam.SetPosition(side*650,220,430);cam.SetViewUp(0,1,0);cam.ParallelProjectionOn();cam.SetParallelScale(62);ren.ResetCameraClippingRange();w.Render();f=vtk.vtkWindowToImageFilter();f.SetInput(w);f.Update();p=vtk.vtkPNGWriter();p.SetFileName(str(out/(name+'-after.png')));p.SetInputConnection(f.GetOutputPort());p.Write()

