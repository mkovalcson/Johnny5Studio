import numpy as np, math, struct, sys
sys.path.insert(0,'/home/claude/torso')
import matplotlib; matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.patches import Polygon
import build_body as bb
import xml.etree.ElementTree as ET
HEAD="/home/claude/pkg/Models/johnny5_head.no-torso.urdf"
PALETTE={m.get("name"):m.find("color").get("rgba") for m in ET.parse(HEAD).getroot().findall("material")}
def rot(rpy):
    r,p,y=rpy
    cr,sr=math.cos(r),math.sin(r); cp,sp=math.cos(p),math.sin(p); cy,sy=math.cos(y),math.sin(y)
    return np.array([[cy,-sy,0],[sy,cy,0],[0,0,1]])@np.array([[cp,0,sp],[0,1,0],[-sp,0,cp]])@np.array([[1,0,0],[0,cr,-sr],[0,sr,cr]])
def corners(g):
    h=[v/2 for v in g[1]] if g[0]=="box" else [g[1],g[1],g[2]/2]
    return np.array([[sx*h[0],sy*h[1],sz*h[2]] for sx in(-1,1) for sy in(-1,1) for sz in(-1,1)])
def hull(pts):
    pts=sorted(set(map(tuple,pts)))
    if len(pts)<3: return pts
    def half(P):
        s=[]
        for p in P:
            while len(s)>=2 and (s[-1][0]-s[-2][0])*(p[1]-s[-2][1])-(s[-1][1]-s[-2][1])*(p[0]-s[-2][0])<=0: s.pop()
            s.append(p)
        return s
    return half(pts)[:-1]+half(pts[::-1])[:-1]
fig,(front,side)=plt.subplots(1,2,figsize=(11,13),facecolor="#0e1013")
for ax,t in ((front,"Front (Y left, Z up)"),(side,"Side (X forward, Z up)")):
    ax.set_facecolor("#0e1013"); ax.set_title(t,color="#ddd"); ax.set_aspect("equal"); ax.tick_params(colors="#777")
    for s in ax.spines.values(): s.set_color("#333")
order=bb.PARTS
for name,xyz,rpy,geom,mat in order:
    R=rot(rpy); o=np.array([bb.NX+xyz[0]+bb.TORSO_DX,bb.NY+xyz[1]+bb.TORSO_DY,bb.NZ+xyz[2]+bb.TORSO_DZ])
    W=(R@corners(geom).T).T+o
    rgb=tuple(float(v) for v in PALETTE[bb.HEAD_MATERIALS[mat]].split()[:3])
    for ax,pr in ((front,(1,2)),(side,(0,2))):
        ax.add_patch(Polygon(hull(W[:,pr]),closed=True,facecolor=rgb,edgecolor="#000",lw=.5,alpha=.97))
def stl(p,s=0.001):
    with open(p,'rb') as f:
        h=f.read(84); n=struct.unpack('<I',h[80:84])[0]; d=f.read(n*50)
    a=np.frombuffer(d,dtype=np.uint8).reshape(n,50)
    return a[:,12:48].copy().view('<f4').reshape(n,3,3).astype(float).reshape(-1,3)*s
MD='/home/claude/pkg/Models/Meshes/'
R=rot((1.570796,0,1.570796)); off=np.array([0,0,-0.045138975]); j=np.array([0.123658,0,0.134448])
for p,c in (('NeckAssembly/StepColors/neck_static_black.stl','#e05c5c'),
            ('NeckAssembly/StepColors/neck_static_cad_97a3da.stl','#6f7ac0')):
    W=(R@stl(MD+p).T).T+off+j; st=max(1,len(W)//3000)
    front.scatter(W[::st,1],W[::st,2],s=.4,c=c,alpha=.55,lw=0); side.scatter(W[::st,0],W[::st,2],s=.4,c=c,alpha=.55,lw=0)
head=(0.072,0.0,0.3199)
front.add_patch(Polygon([(-0.159,0.243),(0.159,0.243),(0.159,0.379),(-0.159,0.379)],closed=True,fill=False,edgecolor="#4fa3ff",ls="--",lw=1.1))
side.add_patch(Polygon([(0.072-0.117,0.243),(0.072+0.185,0.243),(0.072+0.185,0.379),(0.072-0.117,0.379)],closed=True,fill=False,edgecolor="#4fa3ff",ls="--",lw=1.1))
for ax in (front,side): ax.axhline(-1.0928,color="#e0c05c",ls=":",lw=.8); ax.set_ylim(-1.20,0.45)
front.set_xlim(0.55,-0.55); side.set_xlim(-0.45,0.65)
plt.tight_layout(); plt.savefig("/home/claude/torso/body_preview.png",dpi=95,facecolor="#0e1013")
print("ok")
