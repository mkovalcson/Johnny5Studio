from studio_paths import CONFIG_ROOT, HEAD_CAD, HARDWARE_CAD, SOLUTION_ROOT
from match_head_recesses import *
D=json.loads((ROOT/'tools/head-bolt-analysis.json').read_text());faces=[1232,1233,1234,1235,1241,1242,1243,1244]
files=[(p,*mesh(p)) for p in (CONFIG_ROOT / 'Models/Robots/Johnny5Head/Meshes/SimplifiedHead2').glob('*.stl') if 'collision' not in p.name]
for i in faces:
 f=D['SimplifiedHead2.step']['cylinders'][i];c=np.array(f['center']);a=np.array(f['axis']);print(i,end=' ')
 for path,p,n in files:
  delta=p-c;v=delta@a;rad=delta-v[:,None]*a;r=np.linalg.norm(rad,axis=1);sel=(abs(r-f['radius'])<.02)&(v>min(f['v'])-.02)&(v<max(f['v'])+.02)&(np.sum(n*rad,axis=1)<-.8*r)
  if sum(sel)>10:print(path.name,sum(sel),p[sel].min(0).round(3),p[sel].max(0).round(3),end='; ')
 print()
