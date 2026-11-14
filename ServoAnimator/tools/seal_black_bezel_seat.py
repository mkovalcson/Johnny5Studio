"""Seal the hidden gold/black junction without moving the visible black lip."""
from studio_paths import ARCHIVE_ROOT, DOCUMENTATION_ROOT
from studio_paths import CONFIG_ROOT, HEAD_CAD, HARDWARE_CAD, SOLUTION_ROOT
import hashlib,json,shutil
from pathlib import Path
from collections import Counter
import numpy as np
from match_head_recesses import ROOT,STL_DTYPE
ARCHIVE=ARCHIVE_ROOT / 'sealed-black-bezel-seat-20261103'
INNER=22.86;MID=23.828;OUTER=24.796;REAR=2.45;OVERLAP=.08

def build(raw):
 old=np.frombuffer(raw,dtype=STL_DTYPE,offset=84);p=old['p'].reshape(-1,3).astype(float);r=np.linalg.norm(p[:,1:],axis=1);N=len(old)//12;assert N in [72,144] and len(old)==12*N
 assert abs(p[:,0].min()-3.18)<.00001 and abs(p[:,0].max()-3.8)<.00001
 index=np.rint(np.arctan2(p[:,2],p[:,1])*N/(2*np.pi)).astype(int)%N
 rings={}
 for name,radius in [('inner',INNER),('outer',OUTER)]:
  rings[name]=[p[np.where((index==i)&(abs(r-radius)<.0001))[0][0],1:].copy() for i in range(N)]
 rings['skirt']=[q*((OUTER+OVERLAP)/OUTER) for q in rings['outer']]
 profile=[(3.18,'outer'),(REAR,'skirt'),(REAR,'inner'),(3.18,'inner')]
 radii={'inner':INNER,'outer':OUTER,'skirt':OUTER+OVERLAP};faces=old['p'][:N*10].astype(float).tolist()
 for j,(x,a) in enumerate(profile[:-1]):
  xx,b=profile[j+1]
  for i in range(N):
   k=(i+1)%N;quad=[np.array([x,*rings[a][i]]),np.array([x,*rings[a][k]]),np.array([xx,*rings[b][k]]),np.array([xx,*rings[b][i]])]
   radial=(quad[0]+quad[1])/2;radial[0]=0;radial/=np.linalg.norm(radial);desired=np.array([radii[b]-radii[a],0,0])-(xx-x)*radial
   if np.cross(quad[1]-quad[0],quad[2]-quad[0])@desired<0:quad.reverse()
   faces.extend([[quad[0],quad[1],quad[2]],[quad[0],quad[2],quad[3]]])
 data=np.zeros(len(faces),dtype=STL_DTYPE);data['p']=faces;q=data['p'].astype(float);n=np.cross(q[:,1]-q[:,0],q[:,2]-q[:,0]);length=np.linalg.norm(n,axis=1);assert np.all(length>1e-12);data['n']=n/length[:,None]
 # Existing visible surfaces (first five profile faces) are retained exactly.
 assert np.array_equal(data['p'][:N*10],old['p'][:N*10])
 assert np.all(abs(np.linalg.norm(data['n'],axis=1)-1)<.00001)
 edges=Counter()
 for tri in data['p']:
  v=[tuple(q) for q in tri]
  for a,b in zip(v,v[1:]+v[:1]):edges[tuple(sorted((a,b)))]+=1
 assert set(edges.values())=={2}
 # The rear skirt overlaps the gold inner edge at X=2.7 even at polygon midpoints.
 atGold=OUTER+OVERLAP*(3.18-2.7)/(3.18-REAR)
 assert atGold*np.cos(np.pi/N)>OUTER+.01
 assert REAR<2.7<3.18 and MID>22.86
 return raw[:80]+np.uint32(len(data)).tobytes()+data.tobytes()

def main():
 assert not ARCHIVE.exists(),'Already installed; do not duplicate'
 pending=[];report=[];preserved={}
 for model in ['Johnny5Head','Johnny5FullBody']:
  root=CONFIG_ROOT / 'Models/Robots'/model
  for path in (root/'Meshes/EyeMechanism').glob('*.stl'):
   if not path.name.startswith('iris_inner_black_band'):preserved[str(path)]=hashlib.sha256(path.read_bytes()).hexdigest()
  for suffix in ['', '_collision_full']:
   rel=Path(model)/'Meshes/EyeMechanism'/f'iris_inner_black_band{suffix}.stl';path=CONFIG_ROOT / 'Models/Robots'/rel;raw=path.read_bytes();new=build(raw);pending.append((path,rel,new));report.append(dict(mesh=rel.as_posix(),beforeSha256=hashlib.sha256(raw).hexdigest(),afterSha256=hashlib.sha256(new).hexdigest()))
 ARCHIVE.mkdir(parents=True)
 for path,rel,new in pending:
  save=ARCHIVE/rel;save.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(path,save);path.write_bytes(new)
 for path,digest in preserved.items():assert hashlib.sha256(Path(path).read_bytes()).hexdigest()==digest
 for suffix in ['', '_collision_full']:
  sub=f'Meshes/EyeMechanism/iris_inner_black_band{suffix}.stl';assert (CONFIG_ROOT / 'Models/Robots/Johnny5Head'/sub).read_bytes()==(CONFIG_ROOT / 'Models/Robots/Johnny5FullBody'/sub).read_bytes()
 (ARCHIVE/'change-report.json').write_text(json.dumps(dict(rearSkirtXmm=REAR,rearRadialOverlapMm=OVERLAP,visibleSurfacesUnchanged=True,files=report,preserved=preserved),indent=2))
 print('PASS: Concealed seat overlap closes the gap around the full circumference; visible front surfaces unchanged, closed meshes, unit normals, matching models and other eye assets preserved.')
if __name__=='__main__':main()
