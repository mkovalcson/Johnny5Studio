"""Raise the outer radial half of the black eye trim by 0.4 mm."""
from studio_paths import ARCHIVE_ROOT, DOCUMENTATION_ROOT
from studio_paths import CONFIG_ROOT, HEAD_CAD, HARDWARE_CAD, SOLUTION_ROOT
import hashlib,json,shutil
from pathlib import Path
import numpy as np
from match_head_recesses import ROOT,STL_DTYPE
ARCHIVE=ARCHIVE_ROOT / 'raised-black-eye-lip-20261103'
INNER=22.86;OUTER=24.796;MID=(INNER+OUTER)/2;RAISE=.4

def build(raw):
 old=np.frombuffer(raw,dtype=STL_DTYPE,offset=84);p=old['p'].reshape(-1,3).astype(float);radius=np.linalg.norm(p[:,1:],axis=1);sectors=len(old)//8
 assert sectors in [72,144] and len(old)==sectors*8
 back=float(p[:,0].min());front=float(p[:,0].max());assert abs(front-3.4)<.00001
 index=np.rint(np.arctan2(p[:,2],p[:,1])*sectors/(2*np.pi)).astype(int)%sectors
 rings={}
 for kind,r in [('inner',INNER),('outer',OUTER)]:
  rings[kind]=[p[np.where((index==i)&(abs(radius-r)<.0001))[0][0],1:].copy() for i in range(sectors)]
 rings['middle']=[(a+b)/2 for a,b in zip(rings['inner'],rings['outer'])]
 profile=[(back,'inner'),(front,'inner'),(front,'middle'),(front+RAISE,'middle'),(front+RAISE,'outer'),(back,'outer')]
 faces=[]
 for j,(x,ring) in enumerate(profile):
  xx,rr=profile[(j+1)%len(profile)]
  r1={'inner':INNER,'middle':MID,'outer':OUTER}[ring];r2={'inner':INNER,'middle':MID,'outer':OUTER}[rr]
  for i in range(sectors):
   nxt=(i+1)%sectors
   quad=[np.array([x,*rings[ring][i]]),np.array([x,*rings[ring][nxt]]),np.array([xx,*rings[rr][nxt]]),np.array([xx,*rings[rr][i]])]
   radial=(quad[0]+quad[1])/2;radial[0]=0;radial/=np.linalg.norm(radial);desired=np.array([r2-r1,0,0])-(xx-x)*radial
   if np.cross(quad[1]-quad[0],quad[2]-quad[0])@desired<0:quad.reverse()
   faces.extend([[quad[0],quad[1],quad[2]],[quad[0],quad[2],quad[3]]])
 data=np.zeros(len(faces),dtype=STL_DTYPE);data['p']=faces;q=data['p'].astype(float);norm=np.cross(q[:,1]-q[:,0],q[:,2]-q[:,0]);length=np.linalg.norm(norm,axis=1);assert np.all(length>1e-10);data['n']=norm/length[:,None]
 # Confirm unchanged inner half front and rear, and exact outer-half lift.
 q=data['p'].reshape(-1,3).astype(float);r=np.linalg.norm(q[:,1:],axis=1)
 assert abs(q[abs(r-INNER)<.0001,0].max()-front)<.000001
 assert abs(q[abs(r-OUTER)<.0001,0].max()-(front+RAISE))<.000001
 assert abs(q[:,0].min()-back)<.000001
 assert np.max(r)<OUTER+.00001 and np.min(r)>INNER-.00001
 assert np.all(abs(np.linalg.norm(data['n'],axis=1)-1)<.00001)
 # Check closed triangle boundaries, including the vertical step.
 from collections import Counter
 edges=Counter()
 for tri in data['p']:
  points=[tuple(v) for v in tri]
  for a,b in zip(points,points[1:]+points[:1]):edges[tuple(sorted((a,b)))]+=1
 assert set(edges.values())=={2},'Lip must be watertight'
 return raw[:80]+np.uint32(len(data)).tobytes()+data.tobytes(),sectors

def main():
 assert not ARCHIVE.exists(),'Already installed; refuse duplicate raise'
 pending=[];report=[]
 for model in ['Johnny5Head','Johnny5FullBody']:
  root=CONFIG_ROOT / 'Models/Robots'/model
  for suffix in ['', '_collision_full']:
   rel=Path(model)/'Meshes/EyeMechanism'/f'iris_inner_black_band{suffix}.stl';path=CONFIG_ROOT / 'Models/Robots'/rel;raw=path.read_bytes();new,sectors=build(raw);pending.append((path,rel,raw,new));report.append(dict(mesh=rel.as_posix(),sectors=sectors,beforeSha256=hashlib.sha256(raw).hexdigest(),afterSha256=hashlib.sha256(new).hexdigest()))
 ARCHIVE.mkdir(parents=True)
 for path,rel,raw,new in pending:
  backup=ARCHIVE/rel;backup.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(path,backup);path.write_bytes(new)
 for suffix in ['', '_collision_full']:
  sub=f'Meshes/EyeMechanism/iris_inner_black_band{suffix}.stl';assert (CONFIG_ROOT / 'Models/Robots/Johnny5Head'/sub).read_bytes()==(CONFIG_ROOT / 'Models/Robots/Johnny5FullBody'/sub).read_bytes()
 (ARCHIVE/'change-report.json').write_text(json.dumps(dict(raiseMm=RAISE,radialSplitMm=MID,innerFrontMm=3.4,outerFrontMm=3.8,backUnchanged=True,files=report),indent=2))
 print('PASS: Raised outer half 0.4 mm in both visual/collision meshes of both models. Inner half and rear seating unchanged; meshes watertight, normals verified, model copies identical.')
if __name__=='__main__':main()
