"""Identify analytic counterbores in the gold head detail mesh."""
from studio_paths import CONFIG_ROOT, HEAD_CAD, HARDWARE_CAD, SOLUTION_ROOT
import numpy as np
from select_head_bolt_seats import DATA, seat, ROOT
from match_head_recesses import mesh

SOURCE='Meshes/SimplifiedHead2/static_e7c863.stl'
points,normals=mesh(CONFIG_ROOT / 'Models/Robots/Johnny5Head'/SOURCE)
def gold_seats():
    result=[]
    for index,cylinder in enumerate(DATA['SimplifiedHead2.step']['cylinders']):
        if not cylinder['inward'] or not .6<cylinder['radius']<5 or cylinder['u'][1]-cylinder['u'][0]<6.2:continue
        center,axis=np.array(cylinder['center']),np.array(cylinder['axis'])
        delta=points-center;along=delta@axis;radial=delta-along[:,None]*axis
        mask=(abs(np.linalg.norm(radial,axis=1)-cylinder['radius'])<.015)&(along>min(cylinder['v'])-.015)&(along<max(cylinder['v'])+.015)&(np.sum(normals*radial,axis=1)<-.6*cylinder['radius'])
        count=int(mask.sum())
        if count<12:continue
        face={'step':'SimplifiedHead2.step','face':index,**cylinder,'matches':[(SOURCE,count)]}
        fitted=seat(face)
        if fitted is None:continue
        outer,outward,ends=fitted
        face.update(outer=outer.tolist(),outward=outward.tolist(),shaft_radii=ends)
        result.append(face)
    return result
if __name__=='__main__':
    for face in gold_seats():print(face['face'],'radius',face['radius'],'depth',abs(face['v'][1]-face['v'][0]),'rim',face['outer'],'normal',face['outward'],flush=True)
