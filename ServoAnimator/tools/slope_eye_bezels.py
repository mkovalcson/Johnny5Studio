"""Recess the gold eye bezel inner edge and slightly raise its black inner lip."""
from studio_paths import ARCHIVE_ROOT, DOCUMENTATION_ROOT
from studio_paths import CONFIG_ROOT, HEAD_CAD, HARDWARE_CAD, SOLUTION_ROOT
import hashlib,json,shutil
from pathlib import Path
import numpy as np
from match_head_recesses import ROOT,STL_DTYPE
ARCHIVE=ARCHIVE_ROOT / 'sloped-eye-bezels-20261103'
GOLD='iris_gold_continuous';BLACK='iris_inner_black_band'
INNER=24.796;OUTER=30.7467;GOLD_DROP=1.155;BLACK_RAISE=.125

def normalise_normals(data):
 p=data['p'].astype(float);n=np.cross(p[:,1]-p[:,0],p[:,2]-p[:,0]);length=np.linalg.norm(n,axis=1);valid=length>1e-12
 data['n'][valid]=(n[valid]/length[valid,None]).astype(np.float32)

def main():
 assert not ARCHIVE.exists(),'Already installed; inspect the existing backup before retrying'
 pending=[];report=[]
 for model in ['Johnny5Head','Johnny5FullBody']:
  for stem in [GOLD,BLACK]:
   for suffix in ['', '_collision_full']:
    rel=Path(model)/'Meshes/EyeMechanism'/(stem+suffix+'.stl');path=CONFIG_ROOT / 'Models/Robots'/rel;raw=path.read_bytes();before=np.frombuffer(raw,dtype=STL_DTYPE,offset=84);after=before.copy()
    if stem==GOLD:
     radius=np.linalg.norm(before['p'][:,:,1:].astype(float),axis=2);weight=np.clip((OUTER-radius)/(OUTER-INNER),0,1);weight[np.abs(radius-OUTER)<.0001]=0
     after['p'][:,:,0]-=(weight*GOLD_DROP).astype(np.float32)
     outer=np.abs(radius-OUTER)<.0001;assert np.array_equal(after['p'][outer],before['p'][outer])
    else:after['p'][:,:,0]+=BLACK_RAISE
    assert np.array_equal(before['p'][:,:,1:],after['p'][:,:,1:]);normalise_normals(after)
    pending.append((path,rel,raw,raw[:84]+after.tobytes()))
    report.append(dict(model=model,mesh=rel.as_posix(),beforeSha256=hashlib.sha256(raw).hexdigest(),afterSha256=hashlib.sha256(pending[-1][-1]).hexdigest()))
 ARCHIVE.mkdir(parents=True)
 for path,rel,raw,changed in pending:
  backup=ARCHIVE/rel;backup.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(path,backup);path.write_bytes(changed)
 (ARCHIVE/'change-report.json').write_text(json.dumps(dict(goldInnerEdgeRecessMm=GOLD_DROP,blackRingForwardShiftMm=BLACK_RAISE,goldInnerFrontXmm=3.2,blackFrontXmm=3.4,blackProjectionAboveGoldMm=.2,files=report),indent=2))
 print('Updated gold and black visual/collision meshes in both robot models; original assets archived.')
if __name__=='__main__':main()
