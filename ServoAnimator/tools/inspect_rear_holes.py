from studio_paths import preview_directory
from studio_paths import CONFIG_ROOT, HEAD_CAD, HARDWARE_CAD, SOLUTION_ROOT
from pathlib import Path
import vtk
root=SOLUTION_ROOT;r=vtk.vtkSTLReader();r.SetFileName(str(CONFIG_ROOT / 'Models/Robots/Johnny5Head/Meshes/RearCover/rear_cover.stl'));r.Update();m=vtk.vtkPolyDataMapper();m.SetInputConnection(r.GetOutputPort());a=vtk.vtkActor();a.SetMapper(m);a.GetProperty().SetColor(.7,.7,.7)
ren=vtk.vtkRenderer();ren.AddActor(a);ren.SetBackground(.2,.2,.2);w=vtk.vtkRenderWindow();w.SetOffScreenRendering(1);w.SetSize(1100,600);w.AddRenderer(ren)
for side in [1,-1]:
 c=ren.GetActiveCamera();c.SetFocalPoint(0,0,-30);c.SetPosition(0,40,side*500);c.SetViewUp(0,1,0);c.ParallelProjectionOn();c.SetParallelScale(80);ren.ResetCameraClippingRange();w.Render();f=vtk.vtkWindowToImageFilter();f.SetInput(w);f.Update();p=vtk.vtkPNGWriter();p.SetInputConnection(f.GetOutputPort());p.SetFileName(str(preview_directory('head-alignment-previews') / f'rear-cover-only-{side}.png'));p.Write()
