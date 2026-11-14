"""CAD-reference detail from the neck box down to the yoke mounts.

Only visuals are changed. Original link frames and neck linkage remain intact.
"""
import copy
import math
import struct
from pathlib import Path
import xml.etree.ElementTree as E
import numpy as np
from hand_cad_details import transform, rotation, angles, fmt, vec, update
from arm_cad_details import mesh, quad
from neck_linkage_layout import apply as apply_layout

GRAY='step_steel_satin';LIGHT='cad_b3b3b3';DARK='cad_404040';BLACK='cad_020202'

def bevel_box(size,bevel):
    x,y,z=np.array(size)/2;b=min(bevel,x*.4,y*.4)
    poly=[(-x+b,-y),(x-b,-y),(x,-y+b),(x,y-b),(x-b,y),(-x+b,y),(-x,y-b),(-x,-y+b)]
    tris=[]
    for i in range(8):
        j=(i+1)%8;a=poly[i];c=poly[j]
        tris.extend((((0,0,-z),(c[0],c[1],-z),(a[0],a[1],-z)),((0,0,z),(a[0],a[1],z),(c[0],c[1],z))))
        quad(tris,(a[0],a[1],-z),(c[0],c[1],-z),(c[0],c[1],z),(a[0],a[1],z))
    return tris

def ribbed_cover(length,radius,rings):
    tris=[];N=28;start=-length/2+.009;end=length/2-.009
    profile=[(start,radius*.85)]
    for i in range(rings):
        z=start+(end-start)*i/rings;step=(end-start)/rings
        profile.extend(((z,radius*.85),(z+step*.25,radius),(z+step*.60,radius),(z+step*.85,radius*.85)))
    profile.append((end,radius*.85))
    for (za,ra),(zb,rb) in zip(profile,profile[1:]):
        for i in range(N):
            a=i*2*math.pi/N;b=(i+1)*2*math.pi/N
            quad(tris,(ra*math.cos(a),ra*math.sin(a),za),(ra*math.cos(b),ra*math.sin(b),za),
                 (rb*math.cos(b),rb*math.sin(b),zb),(rb*math.cos(a),rb*math.sin(a),zb))
    return tris

def rounded_lug(radius,reach,thickness):
    # Rounded crown concentric with the pin, with a straight spring-side foot.
    poly=[(radius*math.cos(math.pi+i*math.pi/24),radius*math.sin(math.pi+i*math.pi/24)) for i in range(25)]
    poly.extend(((radius,reach),(-radius,reach)))
    tris=[];n=len(poly)
    for i in range(n):
        j=(i+1)%n;a=poly[i];b=poly[j]
        tris.extend((((0,-thickness/2,reach*.4),(a[0],-thickness/2,a[1]),(b[0],-thickness/2,b[1])),
                     ((0,thickness/2,reach*.4),(b[0],thickness/2,b[1]),(a[0],thickness/2,a[1]))))
        quad(tris,(a[0],-thickness/2,a[1]),(a[0],thickness/2,a[1]),(b[0],thickness/2,b[1]),(b[0],-thickness/2,b[1]))
    return tris

def rounded_box(size, radius, steps=5):
    """Closed box with every edge and corner rounded to `radius`."""
    half = np.array(size) / 2
    inner = half - radius
    def ticks(h, r):
        c = (h - r) / h
        ramp = [c + (1 - c) * math.sin(math.pi / 2 * i / steps) for i in range(steps + 1)]
        return [-t for t in reversed(ramp)] + ramp
    axes = [ticks(half[i], radius) for i in range(3)]
    def vertex(p):
        q = np.array(p) * half; core = np.clip(q, -inner, inner); n = q - core
        return tuple(core + radius * n / np.linalg.norm(n)) if np.linalg.norm(n) > 1e-12 else tuple(q)
    tris = []
    for axis in range(3):
        u, v = [a for a in range(3) if a != axis]
        for sign in (-1, 1):
            for i in range(len(axes[u]) - 1):
                for j in range(len(axes[v]) - 1):
                    corners = []
                    for a, b in ((i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1)):
                        p = [0.0, 0.0, 0.0]; p[axis] = sign; p[u] = axes[u][a]; p[v] = axes[v][b]
                        corners.append(vertex(p))
                    for t in ((corners[0], corners[1], corners[2]), (corners[0], corners[2], corners[3])):
                        a_, b_, c_ = (np.array(x) for x in t)
                        n = np.cross(b_ - a_, c_ - a_)
                        if np.linalg.norm(n) < 1e-14: continue
                        tris.append(t if n @ (a_ + b_ + c_) > 0 else (t[0], t[2], t[1]))   # outward: the box is convex
    return tris

def plan_prism(poly, z0, z1):
    """Prism between z0 and z1 over a polygon that is star-shaped about its first vertex."""
    area = sum(poly[k][0] * poly[(k + 1) % len(poly)][1] - poly[(k + 1) % len(poly)][0] * poly[k][1] for k in range(len(poly)))
    P = poly if area > 0 else [poly[0]] + poly[:0:-1]
    tris = []
    for k in range(1, len(P) - 1):
        a, b, c = P[0], P[k], P[k + 1]
        tris += [((a[0], a[1], z1), (b[0], b[1], z1), (c[0], c[1], z1)), ((a[0], a[1], z0), (c[0], c[1], z0), (b[0], b[1], z0))]
    for k in range(len(P)):
        a, b = P[k], P[(k + 1) % len(P)]
        quad(tris, (a[0], a[1], z0), (b[0], b[1], z0), (b[0], b[1], z1), (a[0], a[1], z1))
    return tris

def rounded_loft(sections, top_radius=0.0, steps=5, open_back=None):
    """Closed solid lofted through rounded rectangles (z, x_back, x_front, half_width, corner_radius),
    listed bottom to top; the top edge is rounded over `top_radius`. Every section has the
    same vertex count, so the profile may flare (it need not be convex). open_back=(i0,i1)
    leaves the flat back face out between rings i0 and i1, for a face built separately."""
    def ring(z, x0, x1, w, r, n=6):
        corners = ((x1 - r, w - r), (x0 + r, w - r), (x0 + r, -w + r), (x1 - r, -w + r))
        return [(c[0] + r * math.cos(math.pi / 2 * (q + k / n)), c[1] + r * math.sin(math.pi / 2 * (q + k / n)), z)
                for q, c in enumerate(corners) for k in range(n + 1)]
    z, x0, x1, w, r = sections[-1]
    rings = [ring(*sec) for sec in sections[:-1]]
    for k in range(steps + 1):                       # quarter-round over the top edge
        phi = math.pi / 2 * k / steps; i = top_radius * (1 - math.cos(phi))
        rings.append(ring(z - top_radius + top_radius * math.sin(phi), x0 + i, x1 - i, w - i, max(r - i, 1e-4)))
    tris = []
    for i, (a, b) in enumerate(zip(rings, rings[1:])):
        for k in range(len(a)):
            if open_back and open_back[0] <= i < open_back[1] and k == 13:continue   # the back face (6 points per corner)
            quad(tris, a[k], a[(k + 1) % len(a)], b[(k + 1) % len(a)], b[k])
    for rg, top in ((rings[0], False), (rings[-1], True)):
        c = tuple(np.mean(rg, axis=0))
        for k in range(len(rg)):
            t = (c, rg[k], rg[(k + 1) % len(rg)])
            tris.append(t if top else (t[0], t[2], t[1]))
    return tris

def centre_prism(poly, z0, z1):
    """Prism over a polygon that is star-shaped about the origin (e.g. a toothed wheel)."""
    area = sum(poly[k][0] * poly[(k + 1) % len(poly)][1] - poly[(k + 1) % len(poly)][0] * poly[k][1] for k in range(len(poly)))
    P = poly if area > 0 else poly[::-1]
    tris = []
    for k in range(len(P)):
        a, b = P[k], P[(k + 1) % len(P)]
        tris += [((0, 0, z1), (a[0], a[1], z1), (b[0], b[1], z1)), ((0, 0, z0), (b[0], b[1], z0), (a[0], a[1], z0))]
        quad(tris, (a[0], a[1], z0), (b[0], b[1], z0), (b[0], b[1], z1), (a[0], a[1], z1))
    return tris

def outward(tris):
    """Wind a closed mesh so its normals point out (positive signed volume)."""
    v = sum(np.dot(np.array(a), np.cross(np.array(b), np.array(c))) for a, b, c in tris)
    return tris if v > 0 else [(a, c, b) for a, b, c in tris]

def apply(root):
    apply_layout(root)
    links={e.get('name'):e for e in root.findall('link')}
    joints={e.find('child').get('link'):e for e in root.findall('joint')}
    def world(n):
        j=joints.get(n)
        return np.eye(4) if j is None else world(j.find('parent').get('link'))@transform(j)
    def link(n):return links['body_'+n+'_link']
    def point(n):return world('body_'+n+'_link')[:3,3]
    # Remove only this helper's additions when run again.
    for l in links.values():
        for v in list(l.findall('visual')):
            if v.get('name','').startswith('cad_neck_'):l.remove(v)
    def add(parent,label,geometry,mat,pos=(0,0,0),rpy=(0,0,0)):
        v=E.SubElement(link(parent),'visual',name='cad_neck_'+label)
        E.SubElement(v,'origin',xyz=fmt(pos),rpy=fmt(rpy))
        E.SubElement(v,'geometry').append(geometry);E.SubElement(v,'material',name=mat)
    def box(parent,label,pos,size,mat=GRAY):add(parent,label,E.Element('box',size=fmt(size)),mat,pos)
    def bevel(parent,label,pos,size,mat=GRAY):add(parent,label,mesh('neck_cad_'+label,bevel_box(size,.003)),mat,pos)
    def cyl(parent,label,pos,r,l,mat=LIGHT,axis='y'):
        add(parent,label,E.Element('cylinder',radius=str(r),length=str(l)),mat,pos,
            {'x':(0,math.pi/2,0),'y':(math.pi/2,0,0),'z':(0,0,0)}[axis])
    def world_shape(parent,label,geometry,mat,pos,rpy=(0,0,0)):
        W=np.linalg.inv(world('body_'+parent+'_link'));R=W[:3,:3]@rotation(rpy)
        add(parent,label,geometry,mat,(W@np.append(pos,1))[:3],angles(R))

    # Box-side end guards and central inset panels. These move with NeckLiftLevel.
    p='neck_box';original=link(p).find('visual');size=vec(original.find('geometry/box').get('size'))
    cx=transform(original)[0,3]
    hx,hy,hz=size/2
    bevel(p,'box_top_cap',(cx,0,hz+.0006),(size[0]+.004,size[1]+.004,.001),GRAY)
    # Front shroud (CAD reference): a rounded cover over the front 41 mm of the box,
    # 42 mm tall with its top flush with the box top, so it encloses the front rod's
    # pivot. A rounded raised pad sits centred on its front face, at the bottom,
    # just reaching below the shroud's lower edge.
    sl,sw,sh=.041,size[1]+.004,.042
    front=cx+hx+.005;scx=front-sl/2;scz=hz-sh/2
    add(p,'front_shroud',mesh('neck_cad_front_shroud',rounded_box((sl,sw,sh),.005)),GRAY,(scx,0,scz))
    pw,pt,ph=.031,.008,.021
    add(p,'front_shroud_pad',mesh('neck_cad_front_shroud_pad',rounded_box((pt,pw,ph),.003)),GRAY,
        (front,0,scz-sh/2-.001+ph/2))
    # Neck yaw pulley (CAD reference): a disc on the box top, turned by the neck
    # servo through a toothed belt, with the belt teeth round its underside. It is
    # a body link hung from the head's neck_yaw_link, so it turns with NeckTurn
    # while the head's own URDF entries stay untouched.
    pr=.036
    pulley='body_neck_yaw_pulley_link'
    if pulley not in links:
        links[pulley]=E.SubElement(root,'link',name=pulley)
        j=E.SubElement(root,'joint',name='body_neck_yaw_pulley_fixed',type='fixed')
        E.SubElement(j,'parent',link='neck_yaw_link');E.SubElement(j,'child',link=pulley)
        E.SubElement(j,'origin',xyz='0 0 0',rpy='0 0 0');joints[pulley]=j
    B=world('body_neck_box_link');top=(B@np.array([0,0,hz,1]))[:3]
    Y=world('neck_yaw_link');Yi=np.linalg.inv(Y)
    joints[pulley].find('origin').set('xyz',fmt((Yi@np.append(top,1))[:3]))
    joints[pulley].find('origin').set('rpy',fmt(angles(Yi[:3,:3]@B[:3,:3])))
    teeth=[];n=48
    for k in range(n*4):
        r=(.0345,.036,.036,.0345)[k%4];a=2*math.pi*k/(n*4)
        teeth.append((r*math.cos(a),r*math.sin(a)))
    add('neck_yaw_pulley','yaw_pulley_teeth',mesh('neck_cad_yaw_pulley_teeth',plan_prism(teeth,0,.008)),BLACK,(0,0,0))
    add('neck_yaw_pulley','yaw_pulley_disc',E.Element('cylinder',radius=str(pr),length='.008'),BLACK,(0,0,.008))
    # Rear shroud (CAD reference): a rounded cover over the back of the box, from its
    # rear end up to the pulley. On top, a raised rim leaves a V-shaped opening toward
    # the pulley so the belt has room to move; the neck servo's box hangs beneath.
    rear=cx-hx-.005;rfront=-pr-.002;rl=rfront-rear;rcx=(rear+rfront)/2
    add(p,'rear_shroud',mesh('neck_cad_rear_shroud',rounded_box((rl,sw,sh),.005)),GRAY,(rcx,0,scz))
    # Belt drive: the servo's output pulley sits in the V, level with the yaw pulley's
    # teeth, and a plain belt loops the two; the V is sized round the belt below.
    r1,r2,sx=pr-.0008,.008,-.056                              # belt pitch radii; servo pulley centre (x)
    d=-sx;tilt=math.asin((r1-r2)/d)
    def ring(cxy,r,a0,a1,n):
        return [(cxy[0]+r*math.cos(a0+(a1-a0)*k/n),cxy[1]+r*math.sin(a0+(a1-a0)*k/n)) for k in range(n+1)]
    path=ring((0,0),r1,-(math.pi/2+tilt),math.pi/2+tilt,40)+ring((sx,0),r2,math.pi/2+tilt,3*math.pi/2-tilt,16)
    x0,x1,w=rear+.005,rfront-.004,sw/2-.005
    # The V's point is 6 mm behind the belt; its front half-width keeps every point of
    # the belt (outer face) at least 2 mm from the walls.
    apex=min(x for x,y in path)-.0012-.006;dn=x1-apex
    a=max(.020,max((abs(y)+.0012+.002)*dn/(x-apex) for x,y in path if apex<x<=x1))
    rim=[(x0,0),(x0,-w),(x1,-w),(x1,-a),(x1-dn,0),(x1,a),(x1,w),(x0,w)]
    add(p,'rear_shroud_rim',mesh('neck_cad_rear_shroud_rim',plan_prism(rim,hz,hz+.010)),GRAY,(0,0,0))
    add(p,'servo_pulley',mesh('neck_cad_servo_pulley',plan_prism(
        [(r2*(1.0 if k%2 else .88)*math.cos(math.pi*k/12),r2*(1.0 if k%2 else .88)*math.sin(math.pi*k/12)) for k in range(24)],0,.008)),
        DARK,(sx,0,hz))
    add(p,'servo_shaft',E.Element('cylinder',radius='.003',length=str(sh+.012)),LIGHT,(sx,0,hz-sh/2+.006))
    belt=[]
    for k in range(len(path)):
        (ax,ay),(bx,by)=path[k],path[(k+1)%len(path)]
        L_=math.hypot(bx-ax,by-ay)
        if L_<1e-9:continue
        nx,ny=(by-ay)/L_,-(bx-ax)/L_                            # outward for this anticlockwise loop
        for zz0,zz1 in ((hz+.0002,hz+.0074),):                  # 7.2 mm wide, within the pulleys' tooth bands
            o0=(ax+nx*.0012,ay+ny*.0012);o1=(bx+nx*.0012,by+ny*.0012);i0=(ax-nx*.0008,ay-ny*.0008);i1=(bx-nx*.0008,by-ny*.0008)
            for (u0,u1) in ((o0,o1),(i1,i0)):
                quad(belt,(u0[0],u0[1],zz0),(u1[0],u1[1],zz0),(u1[0],u1[1],zz1),(u0[0],u0[1],zz1))
            quad(belt,(o0[0],o0[1],zz1),(o1[0],o1[1],zz1),(i1[0],i1[1],zz1),(i0[0],i0[1],zz1))
            quad(belt,(i0[0],i0[1],zz0),(i1[0],i1[1],zz0),(o1[0],o1[1],zz0),(o0[0],o0[1],zz0))
    add(p,'neck_belt',mesh('neck_cad_neck_belt',belt),BLACK,(0,0,0))
    bw,bd,bh=.062,.040,.029
    sz=hz-sh-bh/2
    add(p,'servo_box',mesh('neck_cad_servo_box',rounded_box((bd,bw,bh),.002)),LIGHT,(rcx,0,sz))
    for sy in (-1,1):
        box(p,'servo_box_slot_'+str(sy),(rcx-.006,sy*(bw/2+.0003),sz-.004),(.010,.001,.0028),BLACK)

    for sd,sy in (('l',1),('r',-1)):
        # Densely ribbed rear boots replace the coarse spring-ring visuals.
        p='neck_rear_core_'+sd;v=link(p).find('visual')
        T=transform(v);length=float(v.find('geometry/cylinder').get('length'))
        boot_radius=.0102
        rod_centre=(world('body_'+p+'_link')@T)[:3,3];rod_axis=(world('body_'+p+'_link')@T)[:3,2]
        v.find('geometry/cylinder').set('radius','.0082')
        add(p,'rear_boot_'+sd,mesh('neck_cad_rear_boot_'+sd,ribbed_cover(length,.0102,28)),BLACK,T[:3,3],angles(T[:3,:3]))
        for old in links.values():
            if old.get('name','').startswith('body_neck_rear_spring_'+sd+'_'):
                for visual in list(old.findall('visual')):old.remove(visual)
        # A rounded spring-side lug rotates with the rear rod around the top pin.
        # As in the CAD it spans the spring: centred on the rear rod where it meets
        # the pin, just wider than the ribbed spring boot (1 mm beyond each side).
        pivot=point('neck_rear_pin_'+sd);parent='neck_rear_lug_'+sd
        at_pin=rod_centre+rod_axis*((pivot-rod_centre)@rod_axis)
        lug_width=2*boot_radius+.002;lug_out=abs(at_pin[1])+lug_width/2
        pivot[1]=at_pin[1]
        for original in list(link(parent).findall('visual')):link(parent).remove(original)
        bottom=point('neck_rear_eye_bot_'+sd)
        direction=bottom-pivot;direction[1]=0;direction/=np.linalg.norm(direction)
        lug_angle=math.atan2(direction[0],direction[2])
        world_shape(parent,'upper_cheek_'+sd,mesh('neck_cad_upper_cheek_'+sd,rounded_lug(.013,.026,lug_width)),LIGHT,pivot,(0,lug_angle,0))
        # The pin head sits on the lug's outer face.
        head=joints['body_neck_rear_pin_head_'+sd+'_link'];hp=world(head.find('parent').get('link'))
        hpos=point('neck_rear_pin_head_'+sd);hpos[1]=sy*(lug_out+.002)
        head.find('origin').set('xyz',fmt((np.linalg.inv(hp)@np.append(hpos,1))[:3]))
        for k,amount in enumerate((0,.019)):
            pos=pivot+direction*amount;pos[1]=sy*(lug_out+.0015)
            world_shape(parent,f'upper_socket_{sd}_{k}',E.Element('cylinder',radius='.0032' if k==0 else '.0022',length='.002'),BLACK,pos,(math.pi/2,0,0))
        # Lower heel plates and washers follow the stationary chest/yoke brackets.
        bottom=point('neck_rear_eye_bot_'+sd);parent='neck_axle_bracket_'+sd+'_0'
        plate=bottom.copy();plate[1]=sy*.071
        world_shape(parent,'lower_heel_'+sd,mesh('neck_cad_lower_heel_'+sd,rounded_lug(.017,.034,.008)),GRAY,plate,(0,lug_angle,0))
        cap=plate.copy();cap[1]=sy*.076
        world_shape(parent,'lower_shroud_face_'+sd,mesh('neck_cad_lower_shroud_face_'+sd,rounded_lug(.014,.030,.002)),LIGHT,cap,(0,lug_angle,0))
        for k,delta in enumerate((np.array([0,0,0]),np.array([.009,0,-.009]))):
            pos=bottom+delta;pos[1]=sy*.074
            world_shape(parent,f'lower_washer_{sd}_{k}',E.Element('cylinder',radius='.005' if k==0 else '.003',length='.002'),LIGHT,pos,(math.pi/2,0,0))
            pos[1]+=sy*.0015
            world_shape(parent,f'lower_socket_{sd}_{k}',E.Element('cylinder',radius='.0025' if k==0 else '.0015',length='.001'),BLACK,pos,(math.pi/2,0,0))

    # Passive front rod: silver gland at the top, black ribbed cover below it.
    p='neck_front_sleeve';v=link(p).find('visual');v.find('material').set('name',LIGHT)
    T=transform(v);length=float(v.find('geometry/cylinder').get('length'))
    for i,z in enumerate((-length/2+.002,length/2-.002)):
        pos=T[:3,:3]@np.array([0,0,z])+T[:3,3]
        add(p,'front_gland_'+str(i),E.Element('cylinder',radius='.0125',length='.004'),LIGHT,pos,angles(T[:3,:3]))
    p='neck_front_rod';v=link(p).find('visual');T=transform(v)
    length=float(v.find('geometry/cylinder').get('length'));v.find('geometry/cylinder').set('radius','.008')
    add(p,'front_boot',mesh('neck_cad_front_boot',ribbed_cover(length,.010,34)),BLACK,T[:3,3],angles(T[:3,:3]))
    p='neck_rod_housing';v=link(p).find('visual');g=v.find('geometry')
    for old in list(g):g.remove(old)
    g.append(mesh('neck_cad_front_lower_shroud',bevel_box((.042,.055,.028),.006)))
    # The forward socket is carried by a covered saddle back onto the yoke.
    bevel(p,'front_mount_bridge',(-.023,0,-.008),(.065,.052,.016),GRAY)

    # Neck drive gearbox (CAD reference): the housing behind the rear axle reaches out to
    # the outer axle brackets at the top, and its sides run straight down from there,
    # without a bend, to the top edges of the chest shell; every edge and corner is
    # rounded. A rear detail box rises out of the back of the gearbox and the top half of
    # the chest shell, inset from their outer edges and divided at the top of the chest
    # shell: 1.5 times the gearbox's depth below, tapering to its depth above; below it
    # a wide raised ramp, with a narrower raised pad on it, start flush at the bottom of
    # the chest shell and slope out to meet it at 75% of its depth. A 12-tooth sprocket in the middle of the rear axle
    # turns with NeckLift; its roller chain leaves it back and down at 45 degrees, so the
    # gearbox top sits below the sprocket: the upper run enters through a slot in the
    # top, the lower run (after dipping through the yoke tube's slot) through the front.
    # Built in the chest collar's (tilted) frame, K: x forward, y left, z up.
    K=world('body_chest_collar_link')@transform(links['body_chest_collar_link'].find('visual'));Ki=np.linalg.inv(K)
    def k_point(n):return (Ki@world(n))[:3,3]
    def k_mesh(parent,label,tris,mat):
        T=np.linalg.inv(world('body_'+parent+'_link'))@K
        add(parent,label,mesh('neck_cad_'+label,tris),mat,T[:3,3],angles(T[:3,:3]))
    def stl_points(name):
        d=(Path(__file__).resolve().parent.parent/'Meshes/Body'/name).read_bytes();n=struct.unpack('<I',d[80:84])[0]
        return np.frombuffer(d[84:84+50*n],dtype=np.uint8).reshape(n,50)[:,12:48].copy().view('<f4').reshape(-1,3).astype(float)
    collar_pts=stl_points('chest_collar.stl');shell_pts=stl_points('chest_shell.stl')
    bottom=collar_pts[:,2].min()+.001                              # collar foot, just into the shell top
    back=shell_pts[:,0].min()                                      # flush with the shell's back face
    outer_bracket=links['body_neck_axle_bracket_l_1_link'].find('visual')
    reach=k_point('body_neck_axle_bracket_l_1_link')[1]-float(outer_bracket.find('geometry/box').get('size').split()[1])/2
    shell_edge=shell_pts[shell_pts[:,2]>shell_pts[:,2].max()-1e-4][:,1].max()   # half-width of the shell's top face
    axle=k_point('body_neck_drive_hub_link');ax_,az_=axle[0],axle[2]
    tube=k_point('body_yoke_link');tube_r=float(links['body_yoke_link'].find('visual/geometry/cylinder').get('radius'))
    teeth,pitch=12,.00635;pr_=pitch/(2*math.sin(math.pi/teeth))   # #25 chain on a 12-tooth sprocket
    front=ax_-pr_-.010
    top=az_-pr_-.005                                              # below the sprocket
    sections=[(bottom,back,front,shell_edge,.010),(top,back,front,reach,.008)]   # straight sides
    gear=links['body_neck_drive_gearbox_link'].find('visual')
    T=np.linalg.inv(world('body_neck_drive_gearbox_link'))@K
    gear.find('origin').set('xyz',fmt(T[:3,3]));gear.find('origin').set('rpy',fmt(angles(T[:3,:3])))
    g=gear.find('geometry')
    for old in list(g):g.remove(old)
    g.append(mesh('neck_cad_drive_gearbox',rounded_loft(sections,top_radius=.006)))
    gear.find('material').set('name',GRAY)
    def k_prism(poly,y0,y1,u,v,o,n=np.array([0,1.,0])):
        return outward([tuple(tuple(o+a*u+b*v+c*n) for a,b,c in t) for t in plan_prism(poly,y0,y1)])
    def rounded(poly,r,n=4):
        """Convex polygon with its corners rounded to r (the outline itself unchanged)."""
        out=[]
        for k in range(len(poly)):
            a,b,c=np.array(poly[k-1]),np.array(poly[k]),np.array(poly[(k+1)%len(poly)])
            e1=(b-a)/np.linalg.norm(b-a);e2=(c-b)/np.linalg.norm(c-b)
            half=math.acos(max(-1,min(1,-e1@e2)))/2;d=r/math.tan(half)
            p0,p1=b-e1*d,b+e2*d;centre=p0+np.array([-e1[1],e1[0]])*r
            a0,a1=math.atan2(*(p0-centre)[::-1]),math.atan2(*(p1-centre)[::-1])
            if a1<a0:a1+=2*math.pi
            out+=[tuple(centre+r*np.array([math.cos(a0+(a1-a0)*t/n),math.sin(a0+(a1-a0)*t/n)])) for t in range(n+1)]
        return out
    def solid(rings):
        """Closed solid through rings of (x,y,z) points with matching counts; convex caps."""
        tris=[]
        for r0,r1 in zip(rings,rings[1:]):
            for k in range(len(r0)):quad(tris,r0[k],r0[(k+1)%len(r0)],r1[(k+1)%len(r0)],r1[k])
        for rg in (rings[0],rings[-1]):
            c=tuple(np.mean(rg,axis=0))
            for k in range(len(rg)):tris.append((c,rg[k],rg[(k+1)%len(rg)]))
        return outward(tris)
    # Rear detail box: one body standing out from the back of the gearbox and the top
    # half of the chest shell (x=back), inset 6 mm from the gearbox sides and narrowing
    # with them, its edges rounded. A black dividing line runs round it level with the
    # top of the chest shell. Below the line it stands out 1.5 times the gearbox's depth;
    # above, its back face slopes in to the gearbox's depth at the top edge.
    depth=front-back;deep=1.5*depth;edge=.004
    box_top,box_bottom=top-.008,(shell_pts[:,2].min()+shell_pts[:,2].max())/2
    divide=shell_pts[:,2].max()                                   # top of the chest shell
    def gear_half(z):return shell_edge+(reach-shell_edge)*(z-bottom)/(top-bottom)
    wb_,wt_=shell_edge-.006,gear_half(box_top)-.006
    def half(z):return wb_ if z<=bottom else wb_+(wt_-wb_)*(z-bottom)/(box_top-bottom)
    def stand(z):return deep if z<=divide else deep+(depth-deep)*(z-divide)/(box_top-divide)
    sections=[]
    for k in range(6):                                            # rounded bottom edge
        phi=math.pi/2*k/5;z=box_bottom+edge-edge*math.cos(phi);i_=edge*(1-math.sin(phi))
        sections.append((z,back-deep+i_,back+.003,wb_-i_,max(.008-i_,1e-4)))
    for z in (bottom,divide):sections.append((z,back-stand(z),back+.003,half(z),.008))
    sections.append((box_top,back-depth,back+.003,wt_,.008))
    flip=outward(rounded_loft(sections,top_radius=edge))!=rounded_loft(sections,top_radius=edge)
    rear=rounded_loft(sections,top_radius=edge,open_back=(5,8))
    if flip:rear=[(a,c,b) for a,b,c in rear]
    # The flat back face, rebuilt with two radiused window recesses (CAD reference): the
    # upper one holds black blocks with gold details, the lower one gold and silver parts
    # on black. Face points: rings 5-8 of the loft (vertical below the divide, sloping above).
    zr=[box_bottom+edge,bottom,divide,box_top-edge]
    xr=[back-deep,back-deep,back-deep,back-depth]
    hr=[wb_-.008,wb_-.008,half(divide)-.008,wt_-.008]
    def face_x(z):return float(np.interp(z,zr,xr))
    def face_h(z):return float(np.interp(z,zr,hr))
    def face_pt(y,z,inward=0.):return (face_x(z)+inward,y,z)
    def orient(tri,d):
        a,b,c=(np.array(v) for v in tri)
        return tri if np.cross(b-a,c-a)@d>=0 else (tri[0],tri[2],tri[1])
    def rrect(cy,cz,a,b,r,n=6):
        cs=((cy+a-r,cz+b-r),(cy-a+r,cz+b-r),(cy-a+r,cz-b+r),(cy+a-r,cz-b+r))
        return [(c[0]+r*math.cos(math.pi/2*(q+k/n)),c[1]+r*math.sin(math.pi/2*(q+k/n))) for q,c in enumerate(cs) for k in range(n+1)]
    def ray_hit(poly,c,d):
        best=None
        for p0,p1 in zip(poly,poly[1:]+poly[:1]):
            e=np.array(p1)-np.array(p0);m=np.array([[d[0],-e[0]],[d[1],-e[1]]])
            if abs(np.linalg.det(m))<1e-14:continue
            t,u=np.linalg.solve(m,np.array(p0)-c)
            if t>0 and -1e-9<=u<=1+1e-9 and (best is None or t<best):best=t
        return c+best*d
    def band(z0,z1):
        return [(-face_h(z0),z0),(face_h(z0),z0),(face_h(z1),z1),(-face_h(z1),z1)]
    back_n=np.array([-1.,0,0])
    def plain(poly):
        c=np.mean(poly,axis=0);t=[]
        for k in range(len(poly)):t.append(orient((face_pt(*c),face_pt(*poly[k]),face_pt(*poly[(k+1)%len(poly)])),back_n))
        return t
    def holed(outer,hole,c):
        c=np.array(c);angles=set()
        for p_ in hole+outer:angles.add(math.atan2(p_[1]-c[1],p_[0]-c[0])%(2*math.pi))
        pts=[]
        for a_ in sorted(angles):
            d_=np.array([math.cos(a_),math.sin(a_)]);pts.append((ray_hit(hole,c,d_),ray_hit(outer,c,d_)))
        t=[]
        for k in range(len(pts)):
            (h0,o0),(h1,o1)=pts[k],pts[(k+1)%len(pts)]
            for tri in ((h0,o0,o1),(h0,o1,h1)):
                if np.linalg.norm(np.cross(np.append(tri[1]-tri[0],0),np.append(tri[2]-tri[0],0)))>1e-12:
                    t.append(orient(tuple(face_pt(*v) for v in tri),back_n))
        return t
    def recess(cy,cz,a,b,r,deep_,lip=.003):
        rings=[];t=[];floor=[]
        for k in range(5):
            phi=math.pi/2*k/4;o=lip*(1-math.sin(phi));dd=lip*(1-math.cos(phi))
            rings.append([face_pt(y,z,dd) for y,z in rrect(cy,cz,a+o,b+o,r+o)])
        rings.append([face_pt(y,z,deep_) for y,z in rrect(cy,cz,a,b,r)])
        for r0,r1 in zip(rings,rings[1:]):
            for k in range(len(r0)):
                m=(np.array(r0[k])+np.array(r0[(k+1)%len(r0)]))/2;d=np.array([0,cy-m[1],cz-m[2]])
                for tri in ((r0[k],r0[(k+1)%len(r0)],r1[(k+1)%len(r0)]),(r0[k],r1[(k+1)%len(r0)],r1[k])):t.append(orient(tri,d))
        ring=rings[-1];c=face_pt(cy,cz,deep_)
        for k in range(len(ring)):floor.append(orient((c,ring[k],ring[(k+1)%len(ring)]),back_n))
        return t,floor,rrect(cy,cz,a+lip,b+lip,r+lip)
    lo_c,lo_a,lo_b,lo_d=(zr[0]+zr[1])/2,.052,.030,.015          # lower window: centre z, half sizes, depth
    up_c,up_a,up_b,up_d=(zr[2]+zr[3])/2,.036,.022,.010          # upper window
    walls_lo,floor_lo,hole_lo=recess(0,lo_c,lo_a,lo_b,.008,lo_d)
    walls_up,floor_up,hole_up=recess(0,up_c,up_a,up_b,.007,up_d)
    m=.006;levels=[zr[0],lo_c-lo_b-m,lo_c+lo_b+m,zr[1],zr[2],up_c-up_b-m,up_c+up_b+m,zr[3]]
    face=plain(band(levels[0],levels[1]))+holed(band(levels[1],levels[2]),hole_lo,(0,lo_c))+plain(band(levels[2],levels[3]))
    face+=plain(band(levels[3],levels[4]))+plain(band(levels[4],levels[5]))+holed(band(levels[5],levels[6]),hole_up,(0,up_c))
    face+=plain(band(levels[6],levels[7]))
    rear+=face+walls_lo+walls_up
    # Window contents, standing off each recess floor (which follows the face's slope).
    GOLD,PALE='cad_e7c863','step_opaque_202_209_238'
    parts={BLACK:floor_lo+floor_up,GOLD:[],PALE:[],LIGHT:[]}
    def on_floor(z_ref,deep_):
        z0_,z1_=(zr[0],zr[2]) if z_ref<=divide else (zr[2],zr[3])
        s_=(face_x(z1_)-face_x(z0_))/(z1_-z0_);x0_=face_x(z0_)-s_*z0_+deep_
        return np.array([x0_,0,0]),np.array([s_,0,1.])
    def plate(mat,poly,c0,c1,z_ref,deep_):
        o,v=on_floor(z_ref,deep_)
        parts[mat].extend(k_prism(poly,c0,c1,np.array([0,1.,0]),v,o,np.array([-1.,0,0])))
    def blk(mat,y0,y1,z0,z1,c0,c1,deep_,r=.0008):
        plate(mat,rrect((y0+y1)/2,(z0+z1)/2,(y1-y0)/2,(z1-z0)/2,min(r,(y1-y0)/2-1e-5,(z1-z0)/2-1e-5),2),c0,c1,(z0+z1)/2,deep_)
    # Upper window: two black blocks, each with a gold tab (dark hole) and a black side
    # bar carrying a gold slot.
    for sy in (1,-1):
        blk(BLACK,sy*.0015,sy*.0335,up_c-.019,up_c+.019,-.001,.005,up_d) if sy>0 else blk(BLACK,-.0335,-.0015,up_c-.019,up_c+.019,-.001,.005,up_d)
        blk(GOLD,sy*.0175-.006,sy*.0175+.006,up_c-.003,up_c+.011,.005,.0058,up_d)
        plate(BLACK,[(sy*.0175+.0018*math.cos(2*math.pi*k/12),up_c+.0065+.0018*math.sin(2*math.pi*k/12)) for k in range(12)],.0058,.0062,up_c,up_d)
        y0_,y1_=sorted((sy*.026,sy*.0332))
        blk(BLACK,y0_,y1_,up_c-.017,up_c+.017,-.001,.0065,up_d)
        blk(GOLD,sy*.0296-.0015,sy*.0296+.0015,up_c-.008,up_c+.008,.0065,.0070,up_d,.0014)
        blk(BLACK,sy*.0296-.0006,sy*.0296+.0006,up_c-.0065,up_c+.0065,.0070,.0073,up_d,.0005)
    # Lower window: fluted gold rollers between pale blocks on top, ten gold blocks with
    # silver pins below, all on black.
    zr_=lo_c+.019;R=.0065
    flute=[((R if k%2 else .88*R)*math.cos(math.pi*k/12),(R if k%2 else .88*R)*math.sin(math.pi*k/12)) for k in range(24)]
    o,v=on_floor(zr_,lo_d)
    for y0_,y1_ in ((-.049,-.027),(-.011,.011),(.027,.049)):
        centre=o+v*zr_+np.array([-R,0,0])
        parts[GOLD].extend(outward([tuple(tuple(centre+a*np.array([-1.,0,0])+b*np.array([0,0,1.])+c*np.array([0,1.,0])) for a,b,c in t) for t in centre_prism(flute,y0_,y1_)]))
    for y0_,y1_ in ((-.025,-.013),(.013,.025)):blk(PALE,y0_,y1_,zr_-.0065,zr_+.0065,-.001,.0125,lo_d,.0015)
    for k in range(10):
        y0_=-.0479+k*.0097
        blk(GOLD,y0_,y0_+.0085,lo_c-.026,lo_c+.009,-.001,.007,lo_d,.0010)
        for dy in (.002,.0065):blk(LIGHT,y0_+dy-.0007,y0_+dy+.0007,lo_c-.0285,lo_c-.026,-.001,.0015,lo_d,.0002)
    for mat,tag in ((BLACK,'rear_window_black'),(GOLD,'rear_window_gold'),(PALE,'rear_window_pale'),(LIGHT,'rear_window_pins')):
        k_mesh('neck_drive_gearbox',tag,parts[mat],mat)
    line=[(divide-.0006,back-deep-.0004,back+.003,half(divide)+.0004,.0084),
          (divide+.0006,back-stand(divide+.0006)-.0004,back+.003,half(divide+.0006)+.0004,.0084)]
    k_mesh('neck_drive_gearbox','rear_detail_line',outward(rounded_loft(line)),BLACK)
    # Two raised areas below it: a wide ramp and a narrower pad on it, starting flush at
    # the bottom of the chest shell and sloping out to 75% of the box's lower depth at its foot.
    z_low=shell_pts[:,2].min()
    def ramp(half,z0,z1,lift):
        def out(z):return back-.001-lift-(.75*deep-.001)*(z-z_low)/(box_bottom-z_low)
        poly=rounded([(-half,z0),(half,z0),(half,z1),(-half,z1)],.006)
        return solid([[(back+.003,y,z) for y,z in poly],[(out(z),y,z) for y,z in poly]])
    rear+=ramp(wb_-.014,z_low,box_bottom+.004,0)
    rear+=ramp(wb_-.028,z_low+.012,box_bottom+.004,.002)
    k_mesh('neck_drive_gearbox','rear_detail',rear,GRAY)
    # The rear detail box and ramps replace the chest's old dark back panel.
    for j in [j for j in root.findall('joint') if j.find('child').get('link')=='body_chest_back_link']:root.remove(j)
    for l in [l for l in root.findall('link') if l.get('name')=='body_chest_back_link']:root.remove(l)
    # Sprocket (with a slim hub collar beside it) on the rear axle; turns with NeckLift.
    hub=links['body_neck_drive_hub_link'].find('visual')
    hub.find('geometry/cylinder').set('radius','0.008500');hub.find('geometry/cylinder').set('length','0.010000')
    hub.find('origin').set('xyz','0 0.006700 0');hub.find('material').set('name',BLACK)
    profile=(.0104,.0142,.0142,.0104)
    teeth_ring=[(profile[k%4]*math.cos(2*math.pi*(k-1.5)/(teeth*4)),profile[k%4]*math.sin(2*math.pi*(k-1.5)/(teeth*4))) for k in range(teeth*4)]   # teeth between the rollers
    add('neck_drive_hub','drive_sprocket',mesh('neck_cad_drive_sprocket',centre_prism(teeth_ring,-.0017,.0017)),BLACK,(0,0,0),
        vec(hub.find('origin').get('rpy')))
    # Roller chain: round the sprocket's top and front, then back and down at 45 degrees
    # into the gearbox (the upper run through its top, the lower run through its front).
    d=np.array([-1.,-1.])/math.sqrt(2)
    wrap=[np.array([ax_+pr_*math.cos(math.radians(135-30*k)),az_+pr_*math.sin(math.radians(135-30*k))]) for k in range(7)]
    up=[];q=wrap[0]
    while q[1]>top-.006:q=q+pitch*d;up.append(q)
    low=[];q=wrap[-1]
    while q[0]>front-.006:q=q+pitch*d;low.append(q)
    pins=up[::-1]+wrap+low
    chain=[];Y=np.array([0,1.,0])
    for a,b in zip(pins,pins[1:]):
        a3,b3=np.array([a[0],0,a[1]]),np.array([b[0],0,b[1]]);L=np.linalg.norm(b3-a3);u=(b3-a3)/L;v=np.cross(Y,u);rr=.0029
        plate=[(L+rr*math.cos(t),rr*math.sin(t)) for t in np.linspace(-math.pi/2,math.pi/2,7)]+\
              [(rr*math.cos(t),rr*math.sin(t)) for t in np.linspace(math.pi/2,3*math.pi/2,7)]
        for y0 in (-.0034,.0020):                                  # side plates
            chain+=k_prism(plate,y0,y0+.0014,u,v,a3)
    for a in pins:                                                 # rollers
        circle=[(.0018*math.cos(2*math.pi*k/8),.0018*math.sin(2*math.pi*k/8)) for k in range(8)]
        chain+=k_prism(circle,-.0020,.0020,np.array([1.,0,0]),np.array([0,0,1.]),np.array([a[0],0,a[1]]))
    k_mesh('neck_drive_gearbox','drive_chain',chain,DARK)
    # The chain's entry slots: in the top for the upper run, in the front for the lower.
    x_top=wrap[0][0]-(wrap[0][1]-top);z_front=wrap[-1][1]-(wrap[-1][0]-front)
    add('neck_drive_gearbox','chain_slot',mesh('neck_cad_chain_slot',rounded_box((.016,.012,.002),.0008)),BLACK,
        (T@np.array([x_top,0,top+.0003,1]))[:3],angles(T[:3,:3]))
    add('neck_drive_gearbox','chain_slot_front',mesh('neck_cad_chain_slot_front',rounded_box((.002,.012,.016),.0008)),BLACK,
        (T@np.array([front+.0003,0,z_front,1]))[:3],angles(T[:3,:3]))
    # Slot in the yoke tube where the sprocket and the lower run dip into it.
    near=pr_+.0035;span=[];a_,b_=wrap[-1],np.array([front,z_front])
    for t in np.linspace(0,math.pi,721):
        q=np.array([tube[0]+tube_r*math.cos(t),tube[2]+tube_r*math.sin(t)])
        s_=max(0,min(1,(q-a_)@(b_-a_)/((b_-a_)@(b_-a_))))
        if np.hypot(*(q-[ax_,az_]))<near or np.linalg.norm(q-(a_+s_*(b_-a_)))<.0045:span.append(t)
    if span:
        rs=tube_r+.0003;ts=np.linspace(min(span)-.03,max(span)+.03,12);slot=[]
        def on_tube(t,y):return (tube[0]+rs*math.cos(t),y,tube[2]+rs*math.sin(t))
        for ta,tb in zip(ts,ts[1:]):
            quad(slot,on_tube(ta,-.0045),on_tube(tb,-.0045),on_tube(tb,.0045),on_tube(ta,.0045))
            quad(slot,on_tube(ta,.0045),on_tube(tb,.0045),on_tube(tb,-.0045),on_tube(ta,-.0045))
        k_mesh('neck_drive_gearbox','yoke_chain_slot',slot,BLACK)

if __name__=='__main__':
    update(Path(__file__).resolve().parent.parent/'johnny5_head.urdf',apply)
