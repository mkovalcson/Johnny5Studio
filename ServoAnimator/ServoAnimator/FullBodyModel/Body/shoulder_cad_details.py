"""Round shoulder sleeves, reduced black necks and covered arm transitions."""
import copy
import math
from pathlib import Path
import xml.etree.ElementTree as E
import numpy as np
from hand_cad_details import transform, angles, fmt, update
from arm_cad_details import sleeve, mesh, quad
from neck_cad_details import rounded_box

GRAY='step_steel_satin';LIGHT='cad_b3b3b3';DARK='cad_404040';BLACK='cad_020202';BLUE='iris_blue'   # the waist's blue panels

def shallow_dome(radius,rise,axial_sign,segments=64,rings=20):
    """Elliptical cap that meets the exposed bearing face at full radius.
    Rings are spaced evenly round the profile, so a deep dome stays smooth."""
    bands=[]
    for j in range(rings):
        phi=math.pi/2*j/rings
        radial=radius*math.cos(phi)
        axial=axial_sign*rise*math.sin(phi)
        bands.append([(radial*math.cos(2*math.pi*i/segments),
                       radial*math.sin(2*math.pi*i/segments),axial)
                      for i in range(segments)])
    tris=[]
    for j in range(rings-1):
        for i in range(segments):
            k=(i+1)%segments
            quad(tris,bands[j][i],bands[j][k],bands[j+1][k],bands[j+1][i])
    tip=(0,0,axial_sign*rise)
    for i in range(segments):
        k=(i+1)%segments
        tris.append((bands[-1][i],bands[-1][k],tip))
    return tris if axial_sign>0 else [(a,c,b) for a,b,c in tris]

def recessed_extension(radius,start,end,pivot,hub_start,hub_end,hub_radius):
    """Yoke end with a narrow, rounded nose between the relieved corners."""
    def first_hub_contact(x,z,angle):
        # Intersect this point on the yoke's cylinder with the rotated solid
        # hub. The returned Y is the hub's innermost face at this X/Z.
        section=hub_radius**2-z**2
        if section<0:return None
        half_width=math.sqrt(section)
        sine,cosine=math.sin(angle),math.cos(angle)
        if abs(sine)<1e-12:
            return hub_start if abs(x)<=half_width else None
        low=(hub_start-pivot+sine*x)/cosine
        high=(hub_end-pivot+sine*x)/cosine
        if sine>0:
            low=max(low,(-half_width-cosine*x)/sine)
            high=min(high,(half_width-cosine*x)/sine)
        else:
            low=max(low,(half_width-cosine*x)/sine)
            high=min(high,(-half_width-cosine*x)/sine)
        return pivot+low if low<=high+1e-12 else None

    N=256;steps=24;rows=[]
    sweep_angles=[-.5+j/200 for j in range(201)]
    for i in range(N):
        a=2*math.pi*i/N
        x,z=radius*math.cos(a),radius*math.sin(a)
        contacts=[v for angle in sweep_angles
                  if (v:=first_hub_contact(x,z,angle)) is not None]
        intrusion=max(0.,end-min(contacts)) if contacts else 0.
        # Keep the swept relief outside the short nose. The nose spans only
        # the marked pair of points and reaches the hub at its midpoint.
        clearance=min(.0015,intrusion*.4)
        relieved=max(start+.001,end-intrusion-clearance)
        # The two marked corners straddle the top of the yoke by the pivot
        # washer. Lift only that short upper arc; the front/back swept recesses
        # remain free for the +/-0.5 radian shoulder pivot.
        arc_half_width=math.radians(18)
        arc_angle=math.acos(max(-1.,min(1.,z/radius)))
        arc_weight=max(0.,1-(arc_angle/arc_half_width)**2)**2
        finish=relieved+(hub_start-.0004-relieved)*arc_weight
        if arc_weight and contacts:
            finish=min(finish,min(contacts)-.0005)
        row=[]
        for j in range(steps+1):
            y=start+(finish-start)*j/steps
            row.append((x,y,z))
        rows.append(row)
    tris=[]
    for i in range(N):
        k=(i+1)%N
        for j in range(steps):quad(tris,rows[i][j],rows[k][j],rows[k][j+1],rows[i][j+1])
        tris.extend((((0,start,0),rows[k][0],rows[i][0]),((0,end-.005,0),rows[i][-1],rows[k][-1])))
    # The angle runs X toward Z, so reverse the generated winding to expose
    # the outside. Otherwise the left extension is culled as a hole.
    return [(a,c,b) for a,b,c in tris]

def outward(tris):
    """Wind a closed mesh so its normals point out (positive signed volume)."""
    v=sum(np.dot(np.array(a),np.cross(np.array(b),np.array(c))) for a,b,c in tris)
    return tris if v>0 else [(a,c,b) for a,b,c in tris]

def bump_profile(radius,back,back_half,arc=40):
    """Side outline of the shoulder bump (x forward, z up, hub axis at the origin),
    anticlockwise: the hub circle round the front, then straight tangents back to a
    flat back face of half-height back_half."""
    d=math.hypot(back,back_half);t=math.atan2(back_half,back)-math.acos(radius/d)
    poly=[(radius*math.cos(-t+2*t*k/arc),radius*math.sin(-t+2*t*k/arc)) for k in range(arc+1)]
    return poly+[(back,back_half),(back,-back_half)]

def inset(poly,d):
    """Convex anticlockwise polygon moved inward by d (mitred corners)."""
    out=[]
    for k in range(len(poly)):
        a,b,c=np.array(poly[k-1]),np.array(poly[k]),np.array(poly[(k+1)%len(poly)])
        e1=(b-a)/np.linalg.norm(b-a);e2=(c-b)/np.linalg.norm(c-b)
        n1=np.array([-e1[1],e1[0]]);n2=np.array([-e2[1],e2[0]])
        out.append(tuple(b+d*(n1+n2)/(1+n1@n2)))
    return out

def prism_xz(poly,w0,w1):
    """Closed prism over a convex (x,z) outline between w0 and w1 (local y)."""
    c=np.mean(poly,axis=0);tris=[]
    for k in range(len(poly)):
        a,b=poly[k],poly[(k+1)%len(poly)]
        tris+=[((c[0],w1,c[1]),(a[0],w1,a[1]),(b[0],w1,b[1])),((c[0],w0,c[1]),(b[0],w0,b[1]),(a[0],w0,a[1]))]
        quad(tris,(a[0],w0,a[1]),(b[0],w0,b[1]),(b[0],w1,b[1]),(a[0],w1,a[1]))
    return outward(tris)

def ray_sample(poly,n):
    """Resample a convex outline containing the origin at n equal angles about the origin."""
    out=[]
    for k in range(n):
        t=2*math.pi*k/n;d=np.array([math.cos(t),math.sin(t)]);best=None
        for a,b in zip(poly,poly[1:]+poly[:1]):
            a,b=np.array(a),np.array(b);e=b-a;den=d[0]*-e[1]+d[1]*e[0]
            if abs(den)<1e-15:continue
            r=(a[0]*-e[1]+a[1]*e[0])/den;u=(d[0]*a[1]-d[1]*a[0])/den
            if r>0 and -1e-9<=u<=1+1e-9 and (best is None or r<best):best=r
        out.append(tuple(d*best))
    return out

def slab(outline,w0,w1,edge,end_scale=1.0,steps=6):
    """Solid from w0 to w1 (local y) over a convex (x,z) outline round the hub axis,
    shrinking in z to end_scale at w1, with its outer (w1) edge rounded over `edge`."""
    def exact(w,d=0):
        s=1-(1-end_scale)*(w-w0)/(w1-w0);poly=[(x,z*s) for x,z in outline]
        return inset(poly,d) if d else poly
    rings=[(w,ray_sample(exact(w),240)) for w in np.linspace(w0,w1-edge,4)]
    for k in range(1,steps+1):
        phi=math.pi/2*k/steps;w=w1-edge+edge*math.sin(phi)
        rings.append((w,ray_sample(exact(w,edge*(1-math.cos(phi))),240)))
    rings=[[(x,w,z) for x,z in sec] for w,sec in rings]
    tris=[]
    for a,b in zip(rings,rings[1:]):
        for k in range(len(a)):quad(tris,a[k],a[(k+1)%len(a)],b[(k+1)%len(a)],b[k])
    for rg in (rings[0],rings[-1]):
        c=(0,rg[0][1],0)
        for k in range(len(rg)):tris.append((c,rg[k],rg[(k+1)%len(rg)]))
    return outward(tris),exact

def stadium(front_centre,half,back,corner=.004,n=24):
    """Rounded-front outline: a half circle (radius `half`) round the front, flat top
    and bottom at +/-half, and a flat back at x=back with small rounded corners."""
    poly=[(front_centre+half*math.cos(-math.pi/2+math.pi*k/n),half*math.sin(-math.pi/2+math.pi*k/n)) for k in range(n+1)]
    for cx,cz,a0 in ((back+corner,half-corner,math.pi/2),(back+corner,-half+corner,math.pi)):
        poly+=[(cx+corner*math.cos(a0+math.pi/2*k/6),cz+corner*math.sin(a0+math.pi/2*k/6)) for k in range(7)]
    return poly

def shoulder_bump(radius,back,back_half,reach,flange):
    """Shoulder bump on the outer face of the shoulder hub, in a local frame with x
    forward, y outward from the hub face (y=0) and z up the arm, hub axis at x=z=0.
    Returns (gray, dark) triangle lists.

    * A thin base plate against the hub: round the front it follows the hub circle,
      then its top and bottom taper straight back to the flat back.
    * Outward from it the centre section is set in: a rounded-front block 72 mm tall,
      shrinking a little toward its rounded outer edge, carrying the dark panel.
    * A raised pad on its top and bottom next to the hub, centred over the axis,
      standing 7 mm above it.
    * Behind the hub, a flange reaching inward over the back of the hub's end ring,
      with a small block on top."""
    base=bump_profile(radius,back,back_half)
    gray,_=slab(base,-.001,.005,.002)
    block_half=.036
    body,body_at=slab(stadium(.012,block_half,back),.003,reach,.004,end_scale=.92)
    gray+=body
    for sz in (1,-1):                             # raised pads, top and bottom
        pad=[tuple((x-.002,y+.0195,sz*(z+.0365)) for x,y,z in t) for t in rounded_box((.064,.039,.013),.004)]
        gray+=outward(pad)
    def at(x,upper):
        pts=base;best=None
        for a,b in zip(pts,pts[1:]+pts[:1]):
            if (a[0]-x)*(b[0]-x)<=0 and a[0]!=b[0]:
                z=a[1]+(b[1]-a[1])*(x-a[0])/(b[0]-a[0])
                if best is None or (z>best if upper else z<best):best=z
        return best
    centre=np.array([(back-.013)/2,0])
    flange_poly=sorted([(-.013,at(-.013,False)),(-.013,at(-.013,True))]+[p for p in base if p[0]<-.013],
                       key=lambda p:math.atan2(p[1]-centre[1],p[0]-centre[0]))
    gray+=prism_xz(flange_poly,-flange,0)
    bx,bz=-.062,at(-.062,True);slope=math.atan2(at(-.045,True)-at(-.079,True),.034)
    R=np.array([[math.cos(slope),0,-math.sin(slope)],[0,1,0],[math.sin(slope),0,math.cos(slope)]])
    block=[tuple(tuple(R@np.array(v)+np.array([bx,-flange+.008,bz+.003])) for v in t)
           for t in rounded_box((.034,.016,.010),.002)]
    gray+=outward(block)
    # Blue outer panel inside a light frame.
    dark=prism_xz(body_at(reach,.004+.003),reach-.0010,reach+.0004)
    return gray,dark

def apply(root):
    # Retire the old decorative front/back pivot heads completely. They are
    # leaf links, separate from the two functional ShoulderHug joints.
    for side in ('left','right'):
        for fb in ('f','b'):
            name=f'body_shoulder_pivot_head_{side}_{fb}_link'
            link=root.find(f"link[@name='{name}']")
            joint=next((j for j in root.findall('joint')
                        if j.find('child').get('link')==name),None)
            if joint is not None:root.remove(joint)
            if link is not None:root.remove(link)
    links={e.get('name'):e for e in root.findall('link')}
    js={j.find('child').get('link'):j for j in root.findall('joint')}
    def world(n):
        j=js.get(n)
        return np.eye(4) if j is None else world(j.find('parent').get('link'))@transform(j)
    def source(link):
        original=link.find('johnny5_shoulder_detail_source')
        if original is None:
            original=E.SubElement(link,'johnny5_shoulder_detail_source')
            for v in link.findall('visual'):original.append(copy.deepcopy(v))
        return original.find('visual')
    def add(link,label,shape,material,pos,R):
        inv=np.linalg.inv(world(link.get('name')))
        v=E.SubElement(link,'visual',name='cad_shoulder_'+label)
        E.SubElement(v,'origin',xyz=fmt((inv@np.append(pos,1))[:3]),rpy=fmt(angles(inv[:3,:3]@R)))
        E.SubElement(v,'geometry').append(shape);E.SubElement(v,'material',name=material)
    def move_joint(child,position):
        j=js[child]
        children={n:world(n)[:3,3] for n,q in js.items() if q.find('parent').get('link')==child}
        inv=np.linalg.inv(world(j.find('parent').get('link')))
        j.find('origin').set('xyz',fmt((inv@np.append(position,1))[:3]))
        inv=np.linalg.inv(world(child))
        for n,p in children.items():js[n].find('origin').set('xyz',fmt((inv@np.append(p,1))[:3]))
    yoke=links['body_yoke_link']
    radius=float(yoke.find('visual/geometry/cylinder').get('radius'))
    yoke_half=float(yoke.find('visual/geometry/cylinder').get('length'))/2
    for v in list(yoke.findall('visual')):
        if v.get('name','').startswith('cad_shoulder_yoke_'):yoke.remove(v)
    outer_radius=.060*.80
    black_radius=outer_radius*.80
    for side,sy in (('left',1),('right',-1)):
        seam=links[f'body_shoulder_seam_{side}_link'];S=world(seam.get('name'))[:3,3]
        original=source(seam);seamR=(world(seam.get('name'))@transform(original))[:3,:3]
        # Local cylinder Y points up the chest; Z is the lateral hub axis.
        up=seamR[:,1];up/=np.linalg.norm(up)
        across=np.array([0.,1.,0.]);front=np.cross(across,up);front/=np.linalg.norm(front)
        upright=np.column_stack((front,across,up))
        swivel=world(f'body_seg_upper_arm_{side}_link')[:3,3]   # the arm-twist frame, at the old swivel
        center=S.copy();center[1]=swivel[1]
        # Replace the inner quarter of the former hub with a fixed yoke end.
        hub_end=abs(center[1])+radius
        # The original seam position is used so repeated updates cannot grow it.
        old_start=.166+.003
        extension=(hub_end-old_start)*.25
        yoke_end=yoke_half+extension
        hub_start=yoke_end+.003
        pivot_y=yoke_end-.012
        split=abs(center[1])-.012
        axis_origin=world(yoke.get('name'))[:3,3]
        points=recessed_extension(radius+.0002,yoke_half-.0005,yoke_end,
                                  pivot_y,hub_start,hub_end,radius)
        if sy<0:points=[tuple((x,-y,z) for x,y,z in reversed(tri)) for tri in points]
        add(yoke,'yoke_extension_'+side,mesh('shoulder_cad_yoke_extension_'+side,points),GRAY,axis_origin,upright)
        # Retain the original shoulder pitch on its lateral hub axis. The NEW
        # pivot is a separate upright hinge between the yoke indentations.
        # There is no sideways arm-lift joint.
        hinge_origin=axis_origin+across*(sy*pivot_y)+up*.040
        pitch_link=f'body_seg_shoulder_{side}_link'
        pitch=js[pitch_link]
        pivot_link=f'body_seg_shoulder_pivot_{side}_link'
        if pivot_link not in links:
            l=E.Element('link',name=pivot_link)
            j=E.Element('joint',name='ShoulderHug'+side.title(),type='revolute')
            E.SubElement(j,'parent',link=pitch.find('parent').get('link'))
            E.SubElement(j,'child',link=pivot_link)
            E.SubElement(j,'origin',xyz='0 0 0',rpy='0 0 0')
            E.SubElement(j,'axis',xyz='0 0 1')
            E.SubElement(j,'limit',lower='-0.5',upper='0.5',effort='30',velocity='2')
            index=list(root).index(links[pitch_link])
            root.insert(index,l);root.insert(index+1,j)
            links[pivot_link]=l;js[pivot_link]=j
        old_pitch=world(pitch_link)[:3,3]
        pivot=js[pivot_link]
        pivot.set('name','ShoulderHug'+side.title())
        # The yoke clearances are cut for exactly this angular sweep.
        pivot.find('limit').set('lower','-0.5')
        pivot.find('limit').set('upper','0.5')
        inv=np.linalg.inv(world(pivot.find('parent').get('link')))
        pivot.find('origin').set('xyz',fmt((inv@np.append(hinge_origin,1))[:3]))
        pivot.find('axis').set('xyz',fmt(inv[:3,:3]@(-up*sy)))
        # Reparent pitch below the new pivot without moving any geometry at zero.
        pitch.find('parent').set('link',pivot_link)
        inv=np.linalg.inv(world(pivot_link))
        pitch.find('origin').set('xyz',fmt((inv@np.append(old_pitch,1))[:3]))
        pitch.set('name','ShoulderRotate'+side.title())
        # The built sleeve leans 25 degrees; +205 degrees brings it vertically
        # upward. Keep the original backward stop and both pivot axes intact.
        pitch.find('limit').set('upper',str(math.radians(205)))
        pitch_origin=axis_origin+across*(sy*.166)
        move_joint(pitch_link,pitch_origin)
        hinge=pitch
        axis=hinge.find('axis')
        if axis is None:axis=E.SubElement(hinge,'axis')
        parentR=world(hinge.find('parent').get('link'))[:3,:3]
        axis.set('xyz',fmt(parentR.T@(-across)))
        spacer=js[f'body_seg_bracket_{side}_link'];spacer.set('type','fixed')
        for tag in ('axis','limit','mimic'):
            for e in list(spacer.findall(tag)):spacer.remove(e)
        # Upright pin and top socket identify the new hinge between the two
        # recesses. The lateral axle remains the distinct original Pitch axis.
        pivot_visual_link=links[pivot_link]
        for v in list(pivot_visual_link.findall('visual')):pivot_visual_link.remove(v)
        add(pivot_visual_link,side+'_pivot_pin',E.Element('cylinder',radius='.0045',length='.028'),
            LIGHT,hinge_origin+up*.001,upright)
        add(yoke,'yoke_pivot_washer_'+side,E.Element('cylinder',radius='.008',length='.002'),
            LIGHT,hinge_origin+up*.016,upright)
        add(yoke,'yoke_pivot_socket_'+side,E.Element('cylinder',radius='.0033',length='.001'),
            BLACK,hinge_origin+up*.0173,upright)
        # The seam rides on the housing through both shoulder motions. Leaving
        # it on the chest exposes the stationary disk when the housing pivots.
        seam_pos=axis_origin+across*(sy*split)
        seam_joint=js[seam.get('name')]
        seam_world=world(seam.get('name'))
        seam_joint.find('parent').set('link',f'body_shoulder_housing_{side}_link')
        inv=np.linalg.inv(world(seam_joint.find('parent').get('link')))
        seam_joint.find('origin').set('xyz',fmt((inv@np.append(seam_pos,1))[:3]))
        seam_joint.find('origin').set('rpy',fmt(angles(inv[:3,:3]@seam_world[:3,:3])))
        # Reach from the previous lower sleeve to 20 mm below the hub's bottom.
        lower=float((swivel-center)@up)-.080
        upper=-radius-.020
        housing=links[f'body_shoulder_housing_{side}_link'];source(housing)
        for v in list(housing.findall('visual')):housing.remove(v)
        add(housing,side+'_round_sleeve',mesh('shoulder_cad_sleeve_'+side,
            sleeve(lower,upper,outer_radius,outer_radius)),GRAY,center,upright)
        for label,z in (('bottom',lower+.002),('top',upper-.002)):
            add(housing,f'{side}_rim_{label}',mesh(f'shoulder_cad_rim_{side}_{label}',
                sleeve(z-.0015,z+.0015,outer_radius+.001,outer_radius+.001,False)),LIGHT,center,upright)
        # Smaller black cylinder meets the horizontal hub at exactly 90 degrees.
        add(housing,side+'_black_neck',E.Element('cylinder',radius=str(black_radius),length=str(-upper+.002)),
            BLACK,center+up*((upper+.002)/2),upright)
        # Solid end cover closes the previously exposed underside of the sleeve.
        # The flared arm below overlaps this cover rather than stopping inside
        # the housing's open circular rim.
        add(housing,side+'_bottom_cover',E.Element('cylinder',radius=str(outer_radius-.0001),length='.003'),
            GRAY,center+up*(lower+.001),upright)
        add(housing,side+'_top_cover',E.Element('cylinder',radius=str(outer_radius-.0001),length='.003'),
            GRAY,center+up*(upper-.001),upright)
        # Replace the flat outer/tombstone cover and its old exposed mid collar.
        for part in ('shoulder_plate','shoulder_plate_rim','shoulder_swivel','shoulder_swivel_lip',
                     'shoulder_swivel_groove_0','shoulder_swivel_groove_1'):
            n=f'body_{part}_{side}_link'
            # Groove names put the index after the side in the original model.
            if part.startswith('shoulder_swivel_groove_'):
                n=f'body_shoulder_swivel_groove_{side}_{part[-1]}_link'
            l=links.get(n)
            if l is not None:
                source(l)
                for v in list(l.findall('visual')):l.remove(v)
        arm=links[f'body_upper_arm_{side}_link']
        for v in list(arm.findall('visual')):
            if v.get('name') in (f'cad_arm_{side}_sleeve',f'cad_arm_{side}_rim_top',f'cad_arm_{side}_rim_bottom',f'cad_shoulder_{side}_arm_flare'):
                arm.remove(v)
        # No flare: the upper arm's stepped levels run straight up into the housing's
        # opening (see arm_cad_details), so the old conical transition is not rebuilt.
        hub=links[f'body_shoulder_hub_{side}_link'];source(hub)
        for v in list(hub.findall('visual')):hub.remove(v)
        hubcenter=axis_origin+across*(sy*(hub_start+split)/2)
        add(hub,side+'_inner_hub',E.Element('cylinder',radius=str(radius),length=str(split-hub_start-.001)),GRAY,hubcenter,seamR)
        # The hub's outer end lines up with the outer edge of the upper-arm sleeve.
        outer_end=abs(center[1])+outer_radius
        # Blue band between the black seam and the dividing line; the end ring stays gray.
        divide=outer_end-.023
        add(housing,side+'_hub_band',E.Element('cylinder',radius=str(radius),length=str(divide-split-.001)),BLUE,
            axis_origin+across*(sy*(split+.001+divide)/2),seamR)
        add(housing,side+'_outer_hub',E.Element('cylinder',radius=str(radius),length=str(outer_end-divide)),GRAY,
            axis_origin+across*(sy*(divide+outer_end)/2),seamR)
        # Shoulder bump (CAD reference) on the hub's outer face, turning with the
        # housing: a thin base plate that follows the hub circle round the front and
        # tapers back to a flat back 99 mm behind the axis, an inset centre section
        # standing 60 mm proud with a dark panel on its outer face, raised pads on its
        # top and bottom next to the hub, and a flange reaching inward over the back
        # of the hub's end ring (see shoulder_bump).
        parts=shoulder_bump(radius,-.099,.0375,.060,.023)
        face=axis_origin+across*(sy*outer_end)
        # Black dividing line round the hub where its end ring begins, level with
        # the inner edge of the bump's rear flange.
        add(housing,side+'_hub_divide',E.Element('cylinder',radius=str(radius+.0004),length='.0012'),
            BLACK,axis_origin+across*(sy*divide),seamR)
        for label,tris,material in zip(('bump','bump_panel'),parts,(GRAY,BLUE)):
            local=[tuple((x,sy*w,z) for x,w,z in t) for t in tris]
            if sy<0:local=[(a_,c_,b_) for a_,b_,c_ in local]
            add(housing,f'{side}_{label}',mesh(f'shoulder_cad_{label}_{side}',local),material,face,upright)
        bearing_radius=radius-.008
        bearing_length=hub_start-pivot_y+.012
        bearing_center=axis_origin+across*(sy*(pivot_y+hub_start)/2)
        add(hub,side+'_hinge_bearing',E.Element('cylinder',radius=str(bearing_radius),length=str(bearing_length)),
            BLACK,bearing_center,seamR)
        # Start the dome on the exposed face of the black bearing. Its first
        # ring has the bearing's full radius; the old base sat inside the
        # cylinder and left an unwanted flat annulus around the visible cap.
        # A full hemisphere: it carries the bearing's full diameter on into the
        # body yoke, closing the gap between the bearing face and the yoke's
        # relieved end. Its tip stays inside the yoke through the hug sweep.
        bearing_inner_face=bearing_center-across*(sy*bearing_length/2)
        add(hub,side+'_hinge_dome',mesh('shoulder_cad_hinge_dome_'+side,
            shallow_dome(bearing_radius,bearing_radius,sy,rings=24)),BLACK,bearing_inner_face,seamR)
        # Axial bearing faces make the real pivot direction explicit. The
        # smaller front/back recess screws are stationary housing fasteners.
        for label,position,owner in (('inner',hub_start-.001,hub),):   # the outer face is under the bump
            pos=axis_origin+across*(sy*position)
            add(owner,f'{side}_axial_washer_{label}',E.Element('cylinder',radius='.010',length='.002'),LIGHT,pos,seamR)
            add(owner,f'{side}_axial_socket_{label}',E.Element('cylinder',radius='.0045',length='.001'),BLACK,pos+across*(sy*.0012),seamR)
        for v in seam.findall('visual'):
            c=v.find('geometry/cylinder')
            if c is not None:
                c.set('radius',str(radius+.0015));c.set('length','.0015')
if __name__=='__main__':
    path=Path(__file__).resolve().parent.parent/'johnny5_head.urdf'
    update(path,apply)
    # Keep the new pivot and restored pitch beside the arm in ordered Joints
    # panels, including when the focused updater had to rename an old entry.
    import re
    text=path.read_text(encoding='utf-8-sig')
    for side in ('left','right'):
        blocks=[]
        patterns=[r'  <link name="body_seg_shoulder_pivot_'+side+r'_link"(?:\s*/>|[^>]*>.*?</link>)\s*\n',
                  r'  <joint name="ShoulderHug'+side.title()+r'"[^>]*>.*?</joint>\s*\n',
                  r'  <joint name="ShoulderRotate'+side.title()+r'"[^>]*>.*?</joint>\s*\n']
        for pattern in patterns:
            match=re.search(pattern,text,re.S)
            if match is None:raise ValueError('Missing shoulder joint block')
            blocks.append(match.group(0).rstrip())
            text=text[:match.start()]+text[match.end():]
        anchor='  <link name="body_seg_shoulder_'+side+'_link" />'
        text=text.replace(anchor,'\n'.join(blocks[:2])+'\n'+anchor+'\n'+blocks[2],1)
    path.write_text(text,encoding='utf-8')
