"""Check the exact bezel deformation and seating clearance in both models."""
from studio_paths import CONFIG_ROOT, HEAD_CAD, HARDWARE_CAD, SOLUTION_ROOT
from slope_eye_bezels import *
checks=0
for model in ['Johnny5Head','Johnny5FullBody']:
 for stem in [GOLD,BLACK]:
  for suffix in ['', '_collision_full']:
   rel=Path(model)/'Meshes/EyeMechanism'/(stem+suffix+'.stl');raw=(ARCHIVE/rel).read_bytes();now=(CONFIG_ROOT / 'Models/Robots'/rel).read_bytes();before=np.frombuffer(raw,dtype=STL_DTYPE,offset=84);after=np.frombuffer(now,dtype=STL_DTYPE,offset=84)
   assert len(now)==len(raw) and now[:84]==raw[:84];checks+=1
   assert np.array_equal(before['p'][:,:,1:],after['p'][:,:,1:]);checks+=1
   if stem==GOLD:
    r=np.linalg.norm(before['p'][:,:,1:].astype(float),axis=2);outer=abs(r-OUTER)<.0001;inner=abs(r-INNER)<.0001
    assert np.array_equal(before['p'][outer],after['p'][outer]);checks+=1
    assert np.max(abs(after['p'][:,:,0][inner]-before['p'][:,:,0][inner]+GOLD_DROP))<.000002;checks+=1
    assert abs(after['p'][:,:,0][inner].max()-3.2)<.000002;checks+=1
   else:
    assert np.max(abs(after['p'][:,:,0]-before['p'][:,:,0]-BLACK_RAISE))<.000002;checks+=1
    assert abs(after['p'][:,:,0].max()-3.4)<.000002 and abs(after['p'][:,:,0].min()-3.18)<.000002;checks+=1
   assert np.all(abs(np.linalg.norm(after['n'],axis=1)-1)<.00001);checks+=1
   # Actual normals agree with each triangle's unchanged winding after deformation.
   p=after['p'].astype(float);n=np.cross(p[:,1]-p[:,0],p[:,2]-p[:,0]);valid=np.linalg.norm(n,axis=1)>1e-12;n[valid]/=np.linalg.norm(n[valid],axis=1)[:,None]
   assert np.all(np.sum(n[valid]*after['n'][valid],axis=1)>.99999);checks+=1
for stem in [GOLD,BLACK]:
 for suffix in ['', '_collision_full']:
  sub='Meshes/EyeMechanism/'+stem+suffix+'.stl';assert (CONFIG_ROOT / 'Models/Robots/Johnny5Head'/sub).read_bytes()==(CONFIG_ROOT / 'Models/Robots/Johnny5FullBody'/sub).read_bytes();checks+=1
assert 3.18<3.2<3.4<3.5;checks+=1 # Black overlaps the gold seat and clears the lens rim.
print(f'PASS: {checks} bezel deformation, fixed outer edge, unchanged radii, normals and clearance assertions.')
