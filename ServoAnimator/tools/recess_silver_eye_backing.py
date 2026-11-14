"""Recess only the silver eye backing exposed by the sloping gold bezels."""
from studio_paths import ARCHIVE_ROOT, DOCUMENTATION_ROOT
from studio_paths import preview_directory
from studio_paths import CONFIG_ROOT, HEAD_CAD, HARDWARE_CAD, SOLUTION_ROOT
import hashlib,json,shutil
from pathlib import Path
import numpy as np
from slope_eye_bezels import ROOT,STL_DTYPE,INNER,OUTER,normalise_normals
ARCHIVE=ARCHIVE_ROOT / 'recess-silver-eye-backing-20261103'
ORIGIN=11.436691;CLEARANCE=.15
FILES=['eye_lens_cad1ee_without_gimbal_screws.stl','eye_lens_cad1ee_collision_full.stl']

def main():
 assert not ARCHIVE.exists(),'Already installed; refuse a duplicate correction'
 pending=[];report=[];preserved={}
 for model in ['Johnny5Head','Johnny5FullBody']:
  root=CONFIG_ROOT / 'Models/Robots'/model
  for p in (root/'Meshes/EyeMechanism').glob('*.stl'):
   if p.name not in FILES:preserved[str(p)]=hashlib.sha256(p.read_bytes()).hexdigest()
  preserved[str(root/'johnny5_head.urdf')]=hashlib.sha256((root/'johnny5_head.urdf').read_bytes()).hexdigest()
  for name in FILES:
   rel=Path(model)/'Meshes/EyeMechanism'/name;path=CONFIG_ROOT / 'Models/Robots'/rel;raw=path.read_bytes();old=np.frombuffer(raw,dtype=STL_DTYPE,offset=84);new=old.copy();p=old['p'].astype(float);r=np.linalg.norm(p[:,:,:2],axis=2)
   # Match the gold's front cone in pupil coordinates, with clearance behind it.
   # Continue the cone inward under the opaque iris; leave the outer silver lip fixed.
   limit=ORIGIN+2.7+(r-INNER)/(OUTER-INNER)*(4.5421-2.7)-CLEARANCE
   displacement=np.clip(.99*(OUTER-r)/(OUTER-22.6441),0,1.2)
   fade=np.clip((p[:,:,2]-8.89)/(12.0-8.89),0,1)
   amount=displacement*fade;mask=(r>21.4)&(r<OUTER-.0001)&(amount>1e-5)
   assert np.any(mask);new['p'][:,:,2][mask]-=amount[mask].astype(np.float32);normalise_normals(new)
   # Preserve thickness instead of flattening several front surfaces together.
   q=new['p'].astype(float);area=np.linalg.norm(np.cross(q[:,1]-q[:,0],q[:,2]-q[:,0]),axis=1)
   oldarea=np.linalg.norm(np.cross(p[:,1]-p[:,0],p[:,2]-p[:,0]),axis=1)
   assert np.count_nonzero(area<1e-12)<=np.count_nonzero(oldarea<1e-12)
   bad=np.linalg.norm(new['n'],axis=1)<1e-8;new['n'][bad]=[0,0,1]
   norms=np.linalg.norm(new['n'],axis=1);new['n']/=norms[:,None]
   assert np.array_equal(new['p'][:,:,:2],old['p'][:,:,:2]);assert np.array_equal(new['p'][~mask],old['p'][~mask])
   assert np.all(new['p'][:,:,2][mask]<old['p'][:,:,2][mask]);outside=r>=OUTER-.0001;assert np.array_equal(new['p'][outside],old['p'][outside])
   assert np.all(abs(np.linalg.norm(new['n'],axis=1)-1)<.00001)
   front=mask&(p[:,:,2]>13)&(r>=22.644)&(r<=OUTER-.001)
   assert np.all(new['p'][:,:,2][front]<limit[front]+.001)
   changed=raw[:84]+new.tobytes();pending.append((path,rel,raw,changed));report.append(dict(mesh=rel.as_posix(),changedVertexInstances=int(mask.sum()),largestRecessMm=float(np.max(old['p'][:,:,2]-new['p'][:,:,2])),beforeSha256=hashlib.sha256(raw).hexdigest(),afterSha256=hashlib.sha256(changed).hexdigest()))
 ARCHIVE.mkdir(parents=True)
 previews=ARCHIVE/'previews-before';previews.mkdir()
 for p in (preview_directory('iris-previews')).glob('*.png'):shutil.copy2(p,previews/p.name)
 for path,rel,raw,changed in pending:
  backup=ARCHIVE/rel;backup.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(path,backup);path.write_bytes(changed);assert path.read_bytes()==changed
 for path,digest in preserved.items():assert hashlib.sha256(Path(path).read_bytes()).hexdigest()==digest
 for name in FILES:assert (CONFIG_ROOT / 'Models/Robots/Johnny5Head/Meshes/EyeMechanism'/name).read_bytes()==(CONFIG_ROOT / 'Models/Robots/Johnny5FullBody/Meshes/EyeMechanism'/name).read_bytes()
 (ARCHIVE/'change-report.json').write_text(json.dumps(dict(clearanceBelowGoldFrontMm=CLEARANCE,outerSilverLipUnchanged=True,files=report,preservedMeshesAndUrdfs=preserved),indent=2))
 print(json.dumps(report,indent=2));print('PASS: Local silver-only recess, outer lip preservation, unchanged gold/black/lenses/blades/URDFs, normals and both model copies verified.')
if __name__=='__main__':main()

