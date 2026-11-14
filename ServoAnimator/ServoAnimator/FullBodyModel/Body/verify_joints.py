import sys, os, math; sys.path.insert(0,'/home/claude/torso')
import build_robot as br, numpy as np
import xml.etree.ElementTree as ET
URDF=sys.argv[1]
rt=ET.parse(URDF).getroot()
links={l.get('name') for l in rt.findall('link')}; J=rt.findall('joint')
kids=[j.find('child').get('link') for j in J]
print('roots:', links-set(kids), '| duplicate children:', len(kids)-len(set(kids)),
      "| editor's root pick:", [l.get('name') for l in rt.findall('link') if l.get('name') not in set(kids)][0])
mov=[j for j in J if j.get('type')!='fixed']
print('movable joints:', len(mov), '| body ones:', len([j for j in mov if j.find('child').get('link').startswith('body_')]),
      '| of which mimic followers:', len([j for j in mov if j.find('mimic') is not None]))
def rpy(r,p,y):
    cr,sr,cp,sp,cy,sy=math.cos(r),math.sin(r),math.cos(p),math.sin(p),math.cos(y),math.sin(y)
    return np.array([[cy*cp,cy*sp*sr-sy*cr,cy*sp*cr+sy*sr],[sy*cp,sy*sp*sr+cy*cr,sy*sp*cr-cy*sr],[-sp,cp*sr,cp*cr]])
def axang(a,t):
    a=np.array(a)/np.linalg.norm(a); K=np.array([[0,-a[2],a[1]],[a[2],0,-a[0]],[-a[1],a[0],0]])
    return np.eye(3)+math.sin(t)*K+(1-math.cos(t))*K@K
jmap={j.find('child').get('link'):j for j in J}
def fk(q):
    q=dict(q)
    for j in J:
        m=j.find('mimic')
        if m is not None: q[j.get('name')]=q.get(m.get('joint'),0.0)*float(m.get('multiplier',1))+float(m.get('offset',0))
    cache={}
    def world(link):
        if link in cache: return cache[link]
        j=jmap.get(link)
        if j is None: T=np.eye(4)
        else:
            o=j.find('origin'); xyz=np.array([float(v) for v in o.get('xyz','0 0 0').split()]); r=[float(v) for v in o.get('rpy','0 0 0').split()]
            L=np.eye(4); L[:3,:3]=rpy(*r); L[:3,3]=xyz
            if j.get('type') in ('revolute','continuous'):
                M=np.eye(4); M[:3,:3]=axang([float(v) for v in j.find('axis').get('xyz').split()],q.get(j.get('name'),0.0)); L=L@M
            T=world(j.find('parent').get('link'))@L
        cache[link]=T; return T
    return world
W0=fk({})
err=0
for n,c,r,g,m in br.PARTS:
    T=W0(f'body_{n}_link'); err=max(err,np.abs(T[:3,3]-np.array(c)).max(), np.abs(T[:3,:3]-np.eye(3)).max())
print(f'zero pose: max deviation of any body part {err:.1e} m; base_link at the origin: {np.allclose(W0("base_link"),np.eye(4))}')
def pos(q,link): return fk(q)(link)[:3,3]
def d(name,link,q=0.2): return pos({name:q},link)-pos({},link)
tip=lambda s,f: f'body_finger_tip_{s}_{f}_round_link'
checks=[('NeckLift','base_link',lambda v: v[2]>0,'head rises'),
        ('ChestTilt','base_link',lambda v: v[0]>0,'head moves forward'),
        ('WaistLean','base_link',lambda v: v[0]>0,'head moves forward')]
for s,S,sy in (('left','Left',1),('right','Right',-1)):
    checks+=[(f'ShoulderRotate{S}',f'body_seg_forearm_{s}_link',lambda v: v[0]>0,'elbow swings forward'),
             (f'ShoulderHug{S}',f'body_seg_forearm_{s}_link',lambda v: np.linalg.norm(v)>1e-6,'shoulder hinges between yoke indentations'),
             (f'ArmTwist{S}',f'body_wrist_disk_{s}_link',(lambda v,sy=sy: v[1]*sy>0),'forearm turns outward'),
             (f'Elbow{S}',f'body_wrist_disk_{s}_link',lambda v: v[2]>0,'forearm bends up'),
             (f'WristPitch{S}',f'body_palm_{s}_link',lambda v: v[2]>0,'hand tips up')]
bad=[(nm,desc) for nm,lk,f,desc in checks if not f(d(nm,lk))]
print('posture and arm directions as intended:', not bad, bad or '')
res={}
for s,S in (('left','Left'),('right','Right')):
    gap=lambda q: np.linalg.norm(pos(q,tip(s,0))-pos(q,tip(s,1)))
    to_th=lambda q,f: np.linalg.norm(pos(q,tip(s,f))-pos(q,f'body_thumb_tip_{s}_round_link'))
    res.update({f'FingerSpreadUpper{S} opens':gap({f'FingerSpreadUpper{S}':0.2})>gap({}),
                f'FingerSpreadLower{S} opens':gap({f'FingerSpreadLower{S}':0.2})>gap({}),
                f'FingerCurlUpper{S} closes':to_th({f'FingerCurlUpper{S}':0.2},0)<to_th({},0),
                f'FingerCurlLower{S} closes':to_th({f'FingerCurlLower{S}':0.2},1)<to_th({},1),
                f'FingerTipCurlUpper{S} closes':to_th({f'FingerTipCurlUpper{S}':0.2},0)<to_th({},0),
                f'FingerTipCurlLower{S} closes':to_th({f'FingerTipCurlLower{S}':0.2},1)<to_th({},1),
                f'ThumbCurl{S} closes':to_th({f'ThumbCurl{S}':0.2},0)<to_th({},0)})
print('finger joints as intended (both hands):', all(res.values()), [k for k,v in res.items() if not v] or '')
Rp=[p for p in br.PARTS if p[0]=='neck_front_rod'][0][2]
for q in (0.3,-0.3):
    Wq=fk({'NeckLift':q}); Rb=Wq('base_link')[:3,:3]
    tilt=math.degrees(math.acos(min(1,(np.trace(Rb)-1)/2)))
    rod=Wq('body_neck_front_rod_link'); ax=rod[:3,:3]@np.array(br._rpy_to_m(*Rp))[:,2]
    miss=np.linalg.norm(np.cross(Wq('body_neck_rod_housing_link')[:3,3]-rod[:3,3],ax))
    print(f'NeckLift {q:+.1f} rad: head rises {1000*Wq("base_link")[2,3]:+.1f} mm, head tilt {tilt:.3f} deg, front rod off its housing by {miss*1000:.1f} mm')
