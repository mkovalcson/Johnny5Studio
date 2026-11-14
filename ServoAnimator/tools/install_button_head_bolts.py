"""Fit supplied button socket-head screws to eight front and four rear cover holes."""
from studio_paths import ARCHIVE_ROOT, DOCUMENTATION_ROOT
from studio_paths import CONFIG_ROOT, HEAD_CAD, HARDWARE_CAD, SOLUTION_ROOT
import collections,hashlib,json,re,shutil
import xml.etree.ElementTree as ET
import numpy as np
from OCP.BRepMesh import BRepMesh_IncrementalMesh
from OCP.StlAPI import StlAPI_Writer
from inspect_head_bolts import read_step
from match_head_recesses import ROOT,mesh,STL_DTYPE
from install_head_bolts import rotation,angles,numbers
SOURCE=(HARDWARE_CAD / 'SBHCSCREW 0.086-56x0.5625-HX-N_SBHCSCREW 0.086-56x0.5625-HX-N.step')
ARCHIVE=ARCHIVE_ROOT / 'button-head-fasteners-20261103'
MESH='Meshes/Fasteners/stainless_button_socket_head.step.stl'
PREFIX='domed_stainless_bolt_'
FACES=[1232,1233,1234,1235,1241,1242,1243,1244]

def circle(points):
 xy=np.unique(points[:,:2],axis=0);a=np.column_stack((2*xy,np.ones(len(xy))));x,y,k=np.linalg.lstsq(a,np.sum(xy*xy,axis=1),rcond=None)[0]
 radius=np.sqrt(k+x*x+y*y);error=np.max(abs(np.linalg.norm(xy-[x,y],axis=1)-radius));assert error<.003
 return np.array([x,y]),float(radius),float(error)

def seats():
 data=json.loads((ROOT/'tools/head-bolt-analysis.json').read_text());result=[]
 front=CONFIG_ROOT / 'Models/Robots/Johnny5Head/Meshes/SimplifiedHead2/static_737373.stl';p,n=mesh(front)
 for face in FACES:
  f=data['SimplifiedHead2.step']['cylinders'][face];c=np.array(f['center']);v=p[:,2]-c[2];rad=p[:,:2]-c[:2];r=np.linalg.norm(rad,axis=1)
  select=(abs(r-f['radius'])<.02)&(v>=min(f['v'])-.02)&(v<=max(f['v'])+.02)&(np.sum(n[:,:2]*rad,axis=1)<-.8*r)
  points=p[select];assert len(points)>100;center,radius,error=circle(points);assert abs(radius-1.143)<.003
  result.append(dict(section='front',hostVisual='static_03',cadCylinder=face,centerCadMm=[*center,float(points[:,2].max())],outwardCad=[0,0,1],holeRadiusMm=radius,fitErrorMm=error,wallDepthMm=float(np.ptp(points[:,2])),uniformScale=1.))
 # Fit the actual runtime rings instead of using the older approximate CAD ICP.
 p,n=mesh(CONFIG_ROOT / 'Models/Robots/Johnny5Head/Meshes/RearCover/rear_cover.stl')
 tf=json.loads((ROOT/'tools/rear-cover-cad-transform.json').read_text());rot=np.array(tf['rotation']);offset=np.array(tf['translation'])
 for face in range(4):
  f=data['rearCoverColor.step']['cylinders'][face];seed=rot@np.array(f['center'])+offset;d=p[:,:2]-seed[:2];r=np.linalg.norm(d,axis=1)
  select=(r<2.3)&(r>1)&(abs(n[:,2])<.02)&(np.sum(n[:,:2]*d,axis=1)<-.8*r)
  points=p[select];assert len(points)>40;center,radius,error=circle(points);assert abs(radius-1.63195)<.003
  result.append(dict(section='rear',hostVisual='rear_cover_cad',cadCylinder=face,centerCadMm=[*center,float(points[:,2].min())],outwardCad=[0,0,-1],holeRadiusMm=radius,fitErrorMm=error,wallDepthMm=float(np.ptp(points[:,2])),uniformScale=radius/1.143))
 assert len(result)==12
 return result

def main():
 assert not ARCHIVE.exists(),'Already installed; refuse duplicate installation'
 selected=seats();prepared=[];record=dict(source=str(SOURCE),sourceSha256=hashlib.sha256(SOURCE.read_bytes()).hexdigest(),mesh=MESH,headRadiusMm=2.0828,shaftRadiusMm=1.0922,underHeadSourceXmm=1.1684,sourceLengthMm=14.2875,seats=selected,models={})
 shape=read_step(SOURCE);BRepMesh_IncrementalMesh(shape,.015,False,.14,True).Perform()
 # Only create backup directory after seats and both models validate.
 for model in ['Johnny5Head','Johnny5FullBody']:
  path=CONFIG_ROOT / f'Models/Robots/{model}/johnny5_head.urdf';text=path.read_text(encoding='utf-8-sig');doc=ET.fromstring(text)
  assert not any(v.get('name','').startswith(PREFIX) for v in doc.iter('visual'))
  link=doc.find("link[@name='head_link']");assert link is not None
  additions=[];rows=[]
  for i,seat in enumerate(selected,1):
   host=link.find(f"visual[@name='{seat['hostVisual']}']");assert host is not None
   orig=host.find('origin');R=rotation(np.fromstring(orig.get('rpy'),sep=' '));offset=np.fromstring(orig.get('xyz'),sep=' ')
   outward=np.array(seat['outwardCad'],float);up=np.array([0.,1.,0.]);tangent=np.cross(up,-outward);tangent/=np.linalg.norm(tangent);B=np.column_stack((-outward,tangent,np.cross(-outward,tangent)))
   position=offset+R@(np.array(seat['centerCadMm'])/1000);normal=R@outward;name=PREFIX+f'{i:03}'
   v=ET.Element('visual',{'name':name});ET.SubElement(v,'origin',{'xyz':numbers(position),'rpy':numbers(angles(R@B))});ET.SubElement(ET.SubElement(v,'geometry'),'mesh',{'filename':MESH,'scale':numbers([seat['uniformScale']/1000]*3)});ET.SubElement(v,'material',{'name':'head_recess_stainless_steel'})
   ET.indent(v,space='  ',level=2);additions.append('    '+ET.tostring(v,encoding='unicode').rstrip()+'\n')
   rows.append(dict(name=name,link='head_link',**seat,seatLinkMetres=position.tolist(),outwardLink=normal.tolist()))
  pattern=re.compile(r'(<link\s+name="head_link"[^>]*>)(.*?)(  </link>)',re.S);new,count=pattern.subn(lambda m:m[1]+m[2]+''.join(additions)+m[3],text);assert count==1;ET.fromstring(new)
  prepared.append((model,path,new));record['models'][model]=rows
 ARCHIVE.mkdir(parents=True);shutil.copy2(SOURCE,ARCHIVE/SOURCE.name)
 writer=StlAPI_Writer();writer.ASCIIMode=False;rawfile=ARCHIVE/'supplied-bolt-tessellation.stl';assert writer.Write(shape,str(rawfile))
 raw=rawfile.read_bytes();d=np.frombuffer(raw,dtype=STL_DTYPE,offset=84).copy();d['p'][:,:,0]-=1.1684
 assert abs(d['p'][:,:,0].max()-14.2875)<.001 and abs(d['p'][:,:,0].min()+1.1684)<.001
 bolt=raw[:84]+d.tobytes();record['meshSha256']=hashlib.sha256(bolt).hexdigest();record['triangles']=len(d)
 for model,path,text in prepared:
  shutil.copy2(path,ARCHIVE/(model+'-before.urdf'))
  dest=path.parent/MESH;assert not dest.exists();dest.parent.mkdir(exist_ok=True);dest.write_bytes(bolt);path.write_text(text,encoding='utf-8',newline='\n')
  print(model,'8 front + 4 rear domed bolts added; existing 90 bolts preserved.',flush=True)
 (ARCHIVE/'placement-manifest.json').write_text(json.dumps(record,indent=2))
if __name__=='__main__':main()
