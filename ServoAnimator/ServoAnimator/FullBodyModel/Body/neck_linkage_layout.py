"""Relocate the neck mounts on the yoke while preserving the default head pose."""
import math
import xml.etree.ElementTree as E
import numpy as np
from hand_cad_details import transform, rotation, angles, fmt, vec

def apply(root):
    js={j.find('child').get('link'):j for j in root.findall('joint')}
    def world(n):
        j=js.get(n)
        return np.eye(4) if j is None else world(j.find('parent').get('link'))@transform(j)
    def name(n):return 'body_'+n+'_link'
    def point(n):return world(name(n))[:3,3]
    def place(n,position,parent=None):
        j=js[name(n)]
        if parent:j.find('parent').set('link',parent)
        inv=np.linalg.inv(world(j.find('parent').get('link')))
        j.find('origin').set('xyz',fmt((inv@np.append(position,1))[:3]))
    def rod(n,a,b,r):
        mid=(a+b)/2;place(n,mid)
        d=b-a;L=np.linalg.norm(d);u=d/L
        R=rotation((-math.asin(u[1]),math.atan2(u[0],u[2]),0))
        parentR=world(name(n))[:3,:3]
        v=root.find(f"link[@name='{name(n)}']/visual")
        v.find('origin').set('xyz','0 0 0');v.find('origin').set('rpy',fmt(angles(parentR.T@R)))
        c=v.find('geometry/cylinder');c.set('radius',str(r));c.set('length',f'{L:.9f}')
    # Extend the upper cross-box front-to-back. Its link frame and the head
    # mount stay put; the longer rear portion carries the rear spring pivots.
    box_visual=root.find(f"link[@name='{name('neck_box')}']/visual")
    box_visual.find('geometry/box').set('size','.140 .076 .028')
    box_visual.find('origin').set('xyz','-.008 0 0')
    box_lip=root.find(f"link[@name='{name('neck_box_lip')}']/visual")
    box_lip.find('geometry/box').set('size','.144 .080 .006')
    box_lip.find('origin').set('xyz','-.008 0 0')
    upper_box=point('neck_box')
    for sd,sy in (('l',1),('r',-1)):
        # Each pivot is inset from its corresponding end of the longer box.
        p=upper_box+np.array([-.056,sy*.054,-.0155])
        place('neck_rear_pin_'+sd,p)
        place('neck_rear_pin_head_'+sd,p+np.array([0,sy*.017,0]))
        place('neck_rear_eye_top_'+sd,p+np.array([0,-sy*.002,0]))
    K=upper_box+np.array([.045,0,-.0195])
    place('neck_front_pin',K)
    frontseg='body_seg_neck_front_rod_link'
    old_front=world(frontseg)[:3,3]
    fixed_front={n:world(n)[:3,3] for n,j in js.items() if j.find('parent').get('link')==frontseg and j.get('type')=='fixed'}
    j=js[frontseg];inv=np.linalg.inv(world(j.find('parent').get('link')))
    j.find('origin').set('xyz',fmt((inv@np.append(K,1))[:3]))
    for n,pos in fixed_front.items():place(n[5:-5],pos+(K-old_front))
    for sd,sy in (('l',1),('r',-1)):
        place('neck_front_clevis_'+sd,K+np.array([0,sy*.014,.001]))
    yoke=point('yoke');old_axle=point('neck_rear_axle')
    # Put the axle on the rear half of the yoke, under the reference arrow.
    # This is 32 mm behind the previous centre; fixed upper pins consequently
    # require longer rear rods and boots, rebuilt from the endpoints below.
    axle=yoke+np.array([-.016,0,.0585])
    upper=(point('neck_rear_pin_l')+point('neck_rear_pin_r'))/2
    rearseg='body_seg_neck_rods_link';boxseg='body_seg_neck_box_link'
    backward_max=.35+math.radians(25)
    js[rearseg].find('limit').set('upper',f'{backward_max:.9f}')
    js[boxseg].find('limit').set('lower',f'{-backward_max:.9f}')
    # Retain all zero-pose world locations while moving the driven pivot.
    fixed={n:world(n)[:3,3] for n,j in js.items() if j.find('parent').get('link')==rearseg and j.get('type')=='fixed'}
    # The level box's pivot follows the relocated upper pins. Its fixed child
    # frames are compensated so the head and box keep their default positions.
    box_fixed={n:world(n)[:3,3] for n,j in js.items() if j.find('parent').get('link')==boxseg}
    j=js[rearseg];inv=np.linalg.inv(world(j.find('parent').get('link')))
    j.find('origin').set('xyz',fmt((inv@np.append(axle,1))[:3]))
    js[boxseg].find('origin').set('xyz',fmt(upper-axle))
    for n,pos in box_fixed.items():
        inv=np.linalg.inv(world(boxseg))
        js[n].find('origin').set('xyz',fmt((inv@np.append(pos,1))[:3]))
    for n,pos in fixed.items():js[n].find('origin').set('xyz',fmt(pos-axle))
    delta=axle-old_axle
    for sd,sy in (('l',1),('r',-1)):
        top=point('neck_rear_pin_'+sd);top[1]=sy*.052
        bottom=axle+np.array([0,sy*.057,0])
        rod('neck_rear_core_'+sd,top,bottom,.0082)
        place('neck_rear_eye_bot_'+sd,bottom)
        # The curved lug is a rod-side end fitting, not a rigid box bracket.
        lug='neck_rear_lug_'+sd;pos=point(lug);place(lug,pos,rearseg)
        for i in (0,1):
            n='neck_axle_bracket_'+sd+'_'+str(i);place(n,point(n)+delta)
        n='neck_axle_cap_'+sd;place(n,point(n)+delta)
    place('neck_rear_axle',axle);place('neck_drive_hub',axle)
    place('neck_drive_gearbox',point('neck_drive_gearbox')+delta)
    # Front follower terminates at the top/front of the yoke, not down its face.
    K=point('neck_front_pin');H=yoke+np.array([.085,0,.036])
    d=H-K;distance=np.linalg.norm(d);u=d/distance
    sleeve_end=K+u*.026
    rod('neck_front_sleeve',K,sleeve_end,.0115)
    rod('neck_front_rod',sleeve_end,H,.008)
    place('neck_rod_housing',H)
    # Hide the old oversized black slot; the new shroud supplies its aperture.
    slot=root.find(f"link[@name='{name('neck_rod_slot')}']")
    if slot is not None:
        for v in list(slot.findall('visual')):slot.remove(v)
    # External URDF viewers retain the first-order mimic approximation. The
    # editor uses the exact aim/extension constraint described by this marker.
    front=js['body_seg_neck_front_rod_link']
    axis=vec(front.find('axis').get('xyz'))
    theta=.0001;rearaxis=np.array([0.,-1.,0.])
    r=upper-axle
    moved=axle+r*math.cos(theta)+np.cross(rearaxis,r)*math.sin(theta)+rearaxis*(rearaxis@r)*(1-math.cos(theta))
    shiftedK=K+(moved-upper);v=H-shiftedK;v/=np.linalg.norm(v)
    ratio=math.atan2(axis@np.cross(u,v),u@v)/theta
    front.find('mimic').set('multiplier',f'{ratio:.9f}')
    front.find('limit').set('lower',str(-max(.5,abs(ratio)*backward_max+.05)))
    front.find('limit').set('upper',str(max(.5,abs(ratio)*backward_max+.05)))
    marker=root.find('johnny5_neck_linkage')
    if marker is None:marker=E.SubElement(root,'johnny5_neck_linkage',name='johnny5_neck_constraint')
    W=np.linalg.inv(world(name('neck_front_rod')))
    marker.attrib.update(version='1',frontJoint='NeckLiftFrontRod',anchorLink=name('neck_rod_housing'),
        rodLink=name('neck_front_rod'),direction=fmt(u),sleeveLength='.026',restLength=f'{distance-.026:.9f}',
        stretchPivot=fmt((W@np.append(sleeve_end,1))[:3]),stretchAxis=fmt(W[:3,:3]@u))
