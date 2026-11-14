"""Increase the existing gold bezel slope by recessing its inner edge 0.5 mm."""
from studio_paths import ARCHIVE_ROOT, DOCUMENTATION_ROOT
from studio_paths import CONFIG_ROOT, HEAD_CAD, HARDWARE_CAD, SOLUTION_ROOT
import hashlib,json,shutil
from pathlib import Path
import numpy as np
from slope_eye_bezels import ROOT,STL_DTYPE,INNER,OUTER,normalise_normals
ARCHIVE=ARCHIVE_ROOT / 'gold-bezel-extra-half-mm-20261103'

def main():
 assert not ARCHIVE.exists(),'Already installed; refuse a duplicate adjustment'
 pending=[];report=[];preserved={}
 for model in ['Johnny5Head','Johnny5FullBody']:
  root=CONFIG_ROOT / 'Models/Robots'/model
  for name in ['iris_inner_black_band.stl','iris_inner_black_band_collision_full.stl','iris_clear_lens.stl','iris_twenty_blades.stl']:
   path=root/'Meshes/EyeMechanism'/name;preserved[str(path)]=hashlib.sha256(path.read_bytes()).hexdigest()
  for suffix in ['', '_collision_full']:
   rel=Path(model)/'Meshes/EyeMechanism'/f'iris_gold_continuous{suffix}.stl';path=CONFIG_ROOT / 'Models/Robots'/rel;raw=path.read_bytes();old=np.frombuffer(raw,dtype=STL_DTYPE,offset=84);new=old.copy();r=np.linalg.norm(old['p'][:,:,1:].astype(float),axis=2)
   weight=np.clip((OUTER-r)/(OUTER-INNER),0,1);outer=abs(r-OUTER)<.0001;inner=abs(r-INNER)<.0001;weight[outer]=0
   assert abs(old['p'][:,:,0][inner].max()-3.2)<.00001,'Unexpected starting placement'
   new['p'][:,:,0]-=(weight*.5).astype(np.float32);normalise_normals(new)
   assert np.array_equal(new['p'][outer],old['p'][outer]);assert np.array_equal(new['p'][:,:,1:],old['p'][:,:,1:])
   assert np.max(abs(old['p'][:,:,0][inner]-new['p'][:,:,0][inner]-.5))<.000001
   assert abs(new['p'][:,:,0][inner].max()-2.7)<.000001;assert np.all(abs(np.linalg.norm(new['n'],axis=1)-1)<.00001)
   changed=raw[:84]+new.tobytes();assert len(changed)==len(raw);pending.append((path,rel,raw,changed));report.append(dict(mesh=rel.as_posix(),beforeSha256=hashlib.sha256(raw).hexdigest(),afterSha256=hashlib.sha256(changed).hexdigest()))
 ARCHIVE.mkdir(parents=True)
 for path,rel,raw,changed in pending:
  backup=ARCHIVE/rel;backup.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(path,backup);path.write_bytes(changed);assert path.read_bytes()==changed
 for path,digest in preserved.items():assert hashlib.sha256(Path(path).read_bytes()).hexdigest()==digest
 for suffix in ['', '_collision_full']:
  sub=f'Meshes/EyeMechanism/iris_gold_continuous{suffix}.stl';assert (CONFIG_ROOT / 'Models/Robots/Johnny5Head'/sub).read_bytes()==(CONFIG_ROOT / 'Models/Robots/Johnny5FullBody'/sub).read_bytes()
 (ARCHIVE/'change-report.json').write_text(json.dumps(dict(additionalRecessMm=.5,totalAdditionalRecessFromOriginalMm=1.655,innerFrontXmm=2.7,outerEdgeFixed=True,files=report,unchangedMeshes=preserved),indent=2))
 print('PASS: Additional 0.5 mm inner-edge recession, fixed outer edges/radii, unit normals, matching models and unchanged black rings/lenses/blades verified.')
if __name__=='__main__':main()
