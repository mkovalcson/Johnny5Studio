from studio_paths import CONFIG_ROOT, HEAD_CAD, HARDWARE_CAD, SOLUTION_ROOT
from pathlib import Path
import vtk
root=CONFIG_ROOT / 'Models/Robots/Johnny5Head/Meshes'
for folder in ['SimplifiedHead2','RearCover']:
 for p in (root/folder).glob('*.stl'):
  if 'collision' in p.name or not (p.name.startswith('static') or folder=='RearCover'):continue
  r=vtk.vtkSTLReader();r.SetFileName(str(p));r.Update()
  c=vtk.vtkPolyDataConnectivityFilter();c.SetInputConnection(r.GetOutputPort());c.SetExtractionModeToAllRegions();c.Update()
  print(p.name, 'total',r.GetOutput().GetBounds(), 'components',c.GetNumberOfExtractedRegions())
  for i in range(c.GetNumberOfExtractedRegions()):
   e=vtk.vtkPolyDataConnectivityFilter();e.SetInputConnection(r.GetOutputPort());e.SetExtractionModeToSpecifiedRegions();e.AddSpecifiedRegion(i);e.Update()
   clean=vtk.vtkCleanPolyData();clean.SetInputConnection(e.GetOutputPort());clean.Update()
   if clean.GetOutput().GetNumberOfCells()>50:print(i,clean.GetOutput().GetNumberOfCells(),tuple(round(x,3) for x in clean.GetOutput().GetBounds()))
