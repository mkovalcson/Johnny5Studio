"""CAD-inspired upper-arm sleeves and inner/outer link detail.

Visual-only postprocessing in the existing upper-arm frame. No joint changes.
"""
import copy
import math
import struct
from pathlib import Path
import xml.etree.ElementTree as E
import numpy as np
from hand_cad_details import transform, rotation, angles, fmt, vec, update

GRAY='step_steel_satin'
ELBOW_R=.026   # radius of the round elbow end; the side plates' edges run into it tangentially
# Side thickness levels from the CAD top view (half-thickness from the arm's centre plane):
STRIP,BASE,RAISED,PANEL_TOP=.010,.020,.0285,.035
STEP=.006   # depth of the narrow edge strips, front and rear
BAND=.0185  # half-width of the raised band, centred on the arm's axis line
LIGHT='cad_b3b3b3'
DARK='cad_404040'
BLACK='cad_020202'

def mesh(name,triangles):
    rel='Meshes/Body/'+name+'.stl'
    path=Path(__file__).resolve().parent.parent/rel
    data=bytearray(name.encode().ljust(80,b'\0')[:80]+struct.pack('<I',len(triangles)))
    for tri in triangles:
        a,b,c=map(np.array,tri);n=np.cross(b-a,c-a);length=np.linalg.norm(n)
        if length:n/=length
        data.extend(struct.pack('<12fH',*n,*a,*b,*c,0))
    path.write_bytes(data)
    return E.Element('mesh',filename=rel)

def quad(tris,a,b,c,d):tris.extend(((a,b,c),(a,c,d)))

def sleeve(z0,z1,rx,ry,windows=True):
    """Hollow elliptical sleeve with true openings on the inside and outside."""
    tris=[];N=64
    zs=[z0,z0+.018,z1-.014,z1] if windows else [z0,z0+(z1-z0)/3,z0+2*(z1-z0)/3,z1]
    def keep(i,j):
        a=(i+.5)*2*math.pi/N
        return not (windows and j==1 and abs(math.cos(a))<.22)
    def point(i,j,inner=False):
        a=i*2*math.pi/N;th=.0025 if inner else 0
        return ((rx-th)*math.cos(a),(ry-th)*math.sin(a),zs[j])
    for i in range(N):
        for j in range(3):
            if not keep(i,j):continue
            p=[point(i,j),point(i+1,j),point(i+1,j+1),point(i,j+1)]
            q=[point(i,j,True),point(i+1,j,True),point(i+1,j+1,True),point(i,j+1,True)]
            quad(tris,*p);quad(tris,*reversed(q))
            if j==0 or not keep(i,j-1):quad(tris,p[0],q[0],q[1],p[1])
            if j==2 or not keep(i,j+1):quad(tris,p[2],q[2],q[3],p[3])
            if not keep((i-1)%N,j):quad(tris,p[3],q[3],q[0],p[0])
            if not keep((i+1)%N,j):quad(tris,p[1],q[1],q[2],p[2])
    return tris

def clip(poly,a,b,c):
    """Part of a convex (x,z) polygon where a*x+b*z+c >= 0."""
    out=[]
    for k in range(len(poly)):
        p,q=poly[k],poly[(k+1)%len(poly)];fp=a*p[0]+b*p[1]+c;fq=a*q[0]+b*q[1]+c
        if fp>=0:out.append(p)
        if (fp>=0)!=(fq>=0):t=fp/(fp-fq);out.append((p[0]+t*(q[0]-p[0]),p[1]+t*(q[1]-p[1])))
    return out

def holed_cell(xc,zc,hx,s,r,t,n=40):
    """Closed block |x|<=hx, |z-zc|<=s, |y|<=t with a round through hole (radius r,
    centre xc,zc) along y: each hole-wall point is joined to the block's edge along
    its ray from the hole centre."""
    centre=np.array([xc,zc])
    angles=set(2*math.pi*k/n for k in range(n))
    for cx in (-hx,hx):
        for cz in (-s,s):angles.add(math.atan2(cz,cx-xc)%(2*math.pi))
    angles=sorted(angles);ring=[]
    for a in angles:
        d=np.array([math.cos(a),math.sin(a)]);scale=[]
        if d[0]>1e-12:scale.append((hx-xc)/d[0])
        if d[0]<-1e-12:scale.append((-hx-xc)/d[0])
        if d[1]>1e-12:scale.append(s/d[1])
        if d[1]<-1e-12:scale.append(-s/d[1])
        ring.append((centre+r*d,centre+min(scale)*d))
    def p3(q,y):return (q[0],y,q[1])
    def orient(tri,d):
        a,b,c=(np.array(v) for v in tri)
        return tri if np.cross(b-a,c-a)@d>=0 else (tri[0],tri[2],tri[1])
    tris=[]
    for k in range(len(ring)):
        (c0,q0),(c1,q1)=ring[k],ring[(k+1)%len(ring)]
        mid=(c0+c1)/2;inward=np.array([centre[0]-mid[0],0,centre[1]-mid[1]])
        qm=(q0+q1)/2;outward_=np.array([qm[0]-centre[0],0,qm[1]-centre[1]])
        for y,d in ((t,np.array([0,1,0])),(-t,np.array([0,-1,0]))):
            tris+=[orient((p3(c0,y),p3(q0,y),p3(q1,y)),d),orient((p3(c0,y),p3(q1,y),p3(c1,y)),d)]
        tris+=[orient((p3(c0,-t),p3(c1,-t),p3(c1,t)),inward),orient((p3(c0,-t),p3(c1,t),p3(c0,t)),inward)]
        if np.linalg.norm(q1-q0)>1e-9:
            tris+=[orient((p3(q0,-t),p3(q1,-t),p3(q1,t)),outward_),orient((p3(q0,-t),p3(q1,t),p3(q0,t)),outward_)]
    return tris

def side_prism(poly,y0,y1):
    """Closed plate over a convex (x,z) outline between y0 and y1."""
    c=np.mean(poly,axis=0);tris=[]
    for k in range(len(poly)):
        a,b=poly[k],poly[(k+1)%len(poly)]
        tris+=[((c[0],y1,c[1]),(a[0],y1,a[1]),(b[0],y1,b[1])),((c[0],y0,c[1]),(b[0],y0,b[1]),(a[0],y0,a[1]))]
        quad(tris,(a[0],y0,a[1]),(b[0],y0,b[1]),(b[0],y1,b[1]),(a[0],y1,a[1]))
    v=sum(np.dot(np.array(a),np.cross(np.array(b),np.array(c))) for a,b,c in tris)
    return tris if v>0 else [(a,c,b) for a,b,c in tris]

def stadium_plate(length,width,y0,y1,n=10):
    """Rounded-end slot plate, long in z, centred on the origin."""
    r=width/2;poly=[]
    for cz,a0 in ((length/2-r,0),(-length/2+r,math.pi)):
        poly+=[(r*math.cos(a0+math.pi*k/n),cz+r*math.sin(a0+math.pi*k/n)) for k in range(n+1)]
    return side_prism(poly,y0,y1)

def apply(root):
    for side,sign in (('left',1),('right',-1)):
        link=root.find(f"link[@name='body_upper_arm_{side}_link']")
        saved=link.find('johnny5_arm_detail_source')
        if saved is None:
            saved=E.SubElement(link,'johnny5_arm_detail_source');saved.append(copy.deepcopy(link.find('visual')))
        original=saved.find('visual');T=transform(original)
        length=vec(original.find('geometry/box').get('size'))[2]
        for v in list(link.findall('visual')):link.remove(v)
        outward=sign if T[1,1]>0 else -sign
        def visual(label,shape,mat,pos=(0,0,0),rpy=(0,0,0)):
            v=E.SubElement(link,'visual',name=f'cad_arm_{side}_{label}')
            E.SubElement(v,'origin',xyz=fmt(T[:3,:3]@np.array(pos)+T[:3,3]),rpy=fmt(angles(T[:3,:3]@rotation(rpy))))
            E.SubElement(v,'geometry').append(shape);E.SubElement(v,'material',name=mat)
        def box(label,pos,size,mat):visual(label,E.Element('box',size=fmt(size)),mat,pos)
        def cyl(label,pos,r,l,mat,axis='y'):
            visual(label,E.Element('cylinder',radius=str(r),length=str(l)),mat,pos,(math.pi/2,0,0) if axis=='y' else (0,0,0))
        z0=-length/2;end=length/2;sleeve_end=z0+.080
        visual('sleeve',mesh(f'arm_cad_sleeve_{side}',sleeve(z0,sleeve_end,.045,.032)),GRAY)
        # Rolled rims meet the existing swivel collar and lower link cage.
        for label,zz in (('top',z0+.002),('bottom',sleeve_end-.002)):
            visual('rim_'+label,mesh(f'arm_cad_rim_{label}_{side}',sleeve(zz-.002,zz+.002,.046,.033,False)),LIGHT)
        # Side view (CAD reference). The elbow end is a round disc on the elbow pivot
        # (z=end). The side plates' top (front) edge runs straight from the shoulder
        # housing's opening to the disc; the bottom (rear) edge runs straight, parallel
        # to the arm, for half the way, then straight to the disc. Both meet the disc
        # tangentially, so the outline blends smoothly into the elbow. The narrow edge
        # strips sit outside the plates' edges and taper away before the elbow, leaving
        # the slot the forearm folds through. At the opening every level stays inside
        # the opening's 48 mm radius. The outline changes only in this side view; the
        # thickness levels stay constant, and every level runs straight up into the
        # opening (its bottom cover sits about 10 mm above sleeve_end).
        zo=sleeve_end-.016;zw=sleeve_end-.004   # zw: where the inner indentation is measured from
        zb=zo+(end-zo)/2                         # the bend in the bottom edge, halfway down
        EDGE=.040                                # strip edges at the opening
        def arc(cx,cz,r,a0,a1,n=16):return [(cx+r*math.cos(a0+(a1-a0)*k/n),cz+r*math.sin(a0+(a1-a0)*k/n)) for k in range(n+1)]
        def tangent(px,pz,rear):
            d=math.hypot(px,pz-end);a=math.atan2(pz-end,px);b=math.acos(ELBOW_R/d)
            pts=[(ELBOW_R*math.cos(a+s_*b),end+ELBOW_R*math.sin(a+s_*b)) for s_ in (-1,1)]
            return max(pts) if rear else min(pts)
        px=EDGE-STEP                             # plate edges at the opening
        tf=tangent(-px,zo,False);tr=tangent(px,zb,True)
        def line(p,q,z):return p[0]+(q[0]-p[0])*(z-p[1])/(q[1]-p[1])
        def plate_f(z):return line((-px,zo),tf,z)
        def plate_r(z):return px if z<=zb else line((px,zb),tr,z)
        a0,a1=math.atan2(tr[1]-end,tr[0]),math.atan2(tf[1]-end,tf[0])
        if a1<a0:a1+=2*math.pi                   # round the far (elbow) side of the disc
        outline=[(-px,zo),(px,zo),(px,zb),tr]+arc(0,end,ELBOW_R,a0,a1,24)[1:-1]+[tf]
        # Narrow edge strips (STRIP), full depth, then tapering into the plates.
        strips=(('f',plate_f,-1,[zo,end-.080,end-.050]),('r',plate_r,1,[zo,zb,end-.050,end-.025]))
        def depth(z,zl):return STEP if z<=zl[-2] else STEP*(zl[-1]-z)/(zl[-1]-zl[-2])
        def pieces(edge,sgn,zl,width):
            return [[(edge(z1),z1),(edge(z1)+sgn*width(z1),z1),(edge(z2)+sgn*width(z2),z2),(edge(z2),z2)] for z1,z2 in zip(zl,zl[1:])]
        tris=[]
        for tag,edge,sgn,zl in strips:
            for poly in pieces(edge,sgn,zl,lambda z,zl=zl:depth(z,zl)):tris+=side_prism(poly,-STRIP,STRIP)
        visual('edge_strips',mesh(f'arm_cad_edge_strips_{side}',tris),GRAY)
        for k,yy in enumerate((-.0085,0,.0085)):      # longitudinal seams along the full-depth strips
            for tag,edge,sgn,zl in strips:
                for m,(za,zb_) in enumerate(zip(zl[:-2],zl[1:-1])):
                    a=np.array([edge(za)+sgn*(STEP+.0004),yy,za]);b=np.array([edge(zb_)+sgn*(STEP+.0004),yy,zb_]);delta=b-a
                    visual(f'seam_{tag}{m}_{k}',E.Element('cylinder',radius='.0008',length=str(np.linalg.norm(delta))),
                        LIGHT if k!=1 else DARK,(a+b)/2,(0,math.atan2(delta[0],delta[2]),0))
        # Base plates (BASE): the front region with the triangular window, the bottom
        # ledge and the round elbow end, cut from the outline into convex pieces.
        win_top,win_low=zo+.006,end-.110
        cx_,cz_=plate_f(win_top)+.003,win_top;bx_,bz_=-BAND-.002,win_low   # window corners on its long side
        front=clip(outline,-1,0,-(BAND+.002))
        middle=clip(clip(front,0,1,-win_top),0,-1,win_low)
        nx,nz=bz_-cz_,-(bx_-cx_)                       # normal of the window's long side
        if nx*(-BAND-.002-cx_)+nz*(win_top-cz_)>0:nx,nz=-nx,-nz
        plate=[clip(outline,1,0,BAND+.002),clip(front,0,-1,win_top),clip(front,0,1,-win_low),
               clip(middle,nx,nz,-(nx*cx_+nz*cz_))]
        plate=[poly for poly in plate if len(poly)>=3]
        band=[(-BAND,zo),(BAND,zo),(BAND,end)]+arc(0,end,BAND,0,math.pi)[1:-1]+[(-BAND,end)]
        lift=RAISED-BASE
        for face in (-1,1):
            yy=face*.0185;base=face*BASE
            tris=[]
            for poly in plate:tris+=side_prism(poly,face*.017,base)
            visual('plate_'+str(face),mesh(f'arm_cad_plate_{side}_{face}',tris),GRAY)
            tris=[]                                    # risers: the step from the strips out to the plates
            for tag,edge,sgn,zl in strips:
                for poly in pieces(edge,-sgn,zl,lambda z:.002):tris+=side_prism(poly,face*(STRIP-.0005),base)
            visual('risers_'+str(face),mesh(f'arm_cad_risers_{side}_{face}',tris),GRAY)
            # Raised level (RAISED): a band on the axis line ending in a half circle
            # round the elbow pivot. On the inner face it leaves an L-shaped indentation
            # at the shoulder end: a strip beside the opening and a block along the bottom.
            if face==outward:
                visual('centre_'+str(face),mesh(f'arm_cad_centre_{side}',side_prism(band,base-face*.0005,face*RAISED)),GRAY)
            else:
                z1,zbi=zw+.008,zw+.045
                inner=[(-BAND,zbi),(BAND,zbi),(BAND,end)]+arc(0,end,BAND,0,math.pi)[1:-1]+[(-BAND,end)]
                tris=side_prism([(-BAND,z1),(-.002,z1),(-.002,zbi),(-BAND,zbi)],base-face*.0005,face*RAISED)
                tris+=side_prism(inner,base-face*.0005,face*RAISED)
                visual('inner_face_'+str(face),mesh(f'arm_cad_inner_face_{side}',tris),GRAY)
            # Silver pivot boss on the elbow pivot.
            cyl(f'pivot_washer_{face}',(0,face*(RAISED+.001),end),.0095,.002,LIGHT)
            visual(f'pivot_dome_{face}',E.Element('sphere',radius='.0075'),LIGHT,(0,face*(RAISED+.0015),end))
        for face in (-1,1):
            yy=face*.0185
            # Dark equipment tray and bright fittings visible through the sleeve.
            box('tray_'+str(face),(0,face*.025,z0+.044),(.017,.002,.040),DARK)
            box('board_'+str(face),(-.003,face*.0265,z0+.044),(.007,.001,.027),BLACK)
            for k in range(3):
                cyl(f'connector_{face}_{k}',(.003,face*.028,z0+.032+k*.010),.0024,.003,LIGHT)
            for k,zz in enumerate((z0+.020,sleeve_end-.015)):
                cyl(f'window_screw_{face}_{k}',(.012,face*.030,zz),.0015,.002,DARK)
            if face==outward:
                # Raised black panel on the band, on the axis line from under the shoulder
                # housing to 46 mm short of the elbow pivot, carrying the light slot.
                top=face*RAISED;tip=face*PANEL_TOP;p0,p1=zo+.002,end-.046
                visual('slot_panel',mesh(f'arm_cad_slot_panel_{side}',[tuple((x,y,z+(p0+p1)/2) for x,y,z in t)
                    for t in stadium_plate(p1-p0,.019,top-face*.0005,tip)]),BLACK)
                visual('slot',mesh(f'arm_cad_slot_{side}',[tuple((x,y,z+end-.0965) for x,y,z in t)
                    for t in stadium_plate(.063,.006,tip-face*.0002,tip+face*.0006)]),LIGHT)
            else:
                for k in range(3):
                    zz=sleeve_end+.055+k*.011
                    cyl(f'inner_socket_rim_{k}',(.008,yy+face*(.002+lift),zz),.004,.002,LIGHT)
                    cyl(f'inner_socket_{k}',(.008,yy+face*(.0032+lift),zz),.0027,.001,BLACK)
                for k,xx in enumerate((-.006,.006)):   # on the raised band
                    cyl(f'lower_recess_rim_{k}',(xx,yy+face*(.002+lift),end-.050),.0045,.002,LIGHT)
                    cyl(f'lower_recess_{k}',(xx,yy+face*(.0032+lift),end-.050),.0035,.001,BLACK)
        cyl('internal_piston',(0,0,sleeve_end+.045),.006,.100,DARK,'z')
        # The hydraulic shaft runs from inside the piston body right to the elbow pivot,
        # so it always disappears into the elbow (and the forearm's rounded end).
        shaft0=sleeve_end+.055
        cyl('internal_shaft',(0,0,(shaft0+end)/2),.003,end-shaft0,LIGHT,'z')
        cyl('internal_upper_bearing',(0,0,z0+.024),.018,.044,DARK)
        # The former solid link's circular painted holes and oversized piston
        # are superseded by the covered frame. Keep their frames for saved clips.
        for old in root.findall('link'):
            if old.get('name','').startswith(f'body_upper_arm_hole_{side}_') or old.get('name') in (f'body_upper_arm_piston_{side}_link',f'body_upper_arm_hub_{side}_link'):
                for v in list(old.findall('visual')):old.remove(v)
        # The elbow drum, its cap and the forearm piston are removed; their links stay for saved clips.
        for name in (f'body_elbow_hub_{side}_link',f'body_elbow_cap_{side}_link',f'body_forearm_piston_{side}_link'):
            old=root.find(f"link[@name='{name}']")
            if old is not None:
                for v in list(old.findall('visual')):old.remove(v)
        fore=root.find(f"link[@name='body_forearm_{side}_link']")
        saved=fore.find('johnny5_arm_detail_source')
        if saved is None:
            saved=E.SubElement(fore,'johnny5_arm_detail_source');saved.append(copy.deepcopy(fore.find('visual')))
        src=saved.find('visual');hx,hy,hz=vec(src.find('geometry/box').get('size'))/2
        for v in list(fore.findall('visual')):fore.remove(v)
        v=copy.deepcopy(src);v.set('name',f'cad_arm_{side}_forearm_plate')
        g=v.find('geometry')
        for old in list(g):g.remove(old)
        # 28 mm thick, so the plate clears the inside of the upper arm's elbow plates
        # (the elbow axis is tilted 3.6 degrees from the upper arm).
        thick=.014
        # Real round holes through the plate replace the painted black discs. The
        # hole centres come from the generator's hole links the first time and are
        # kept with the forearm, so the emptied hole links can be pruned.
        joints={j.find('child').get('link'):j for j in root.findall('joint')}
        def world(n):
            j=joints.get(n)
            return np.eye(4) if j is None else world(j.find('parent').get('link'))@transform(j)
        to_plate=np.linalg.inv(world(fore.get('name'))@transform(src))
        holes=[(float(h.get('z')),float(h.get('x')),float(h.get('r'))) for h in saved.findall('johnny5_forearm_hole')]
        for hole in [] if holes else sorted((l for l in root.findall('link') if l.get('name','').startswith(f'body_forearm_hole_{side}_')),key=lambda l:l.get('name')):
            kept=hole.find('johnny5_arm_detail_source')
            if kept is None:
                kept=E.SubElement(hole,'johnny5_arm_detail_source');kept.append(copy.deepcopy(hole.find('visual')))
            for old in list(hole.findall('visual')):hole.remove(old)
            p_=(to_plate@world(hole.get('name')))[:3,3]
            holes.append((p_[2],p_[0],float(kept.find('visual/geometry/cylinder').get('radius'))))
            E.SubElement(saved,'johnny5_forearm_hole',x=f'{p_[0]:.6f}',z=f'{p_[2]:.6f}',r=f'{holes[-1][2]:.6f}')
        holes.sort()
        # The plate in pieces: the rounded elbow end (the pivot is the centre of the
        # box's elbow end, local z=-hz), a square cell round each hole, and plain
        # lengths between.
        cell=min([hx]+[(b_[0]-a_[0])/2 for a_,b_ in zip(holes,holes[1:])])
        tris=[];z_at=-hz
        for k,(zc,xc,r) in enumerate(holes):
            z0_=zc-cell
            if k==0:
                tris+=side_prism([(hx*math.cos(math.pi+math.pi*i/16),-hz+hx*math.sin(math.pi+math.pi*i/16)) for i in range(17)]+[(hx,z0_),(-hx,z0_)],-thick,thick)
            elif z0_-z_at>1e-6:
                tris+=side_prism([(-hx,z_at),(hx,z_at),(hx,z0_),(-hx,z0_)],-thick,thick)
            tris+=holed_cell(xc,zc,hx,cell,r,thick);z_at=zc+cell
        if not holes:
            tris+=side_prism([(hx*math.cos(math.pi+math.pi*i/16),-hz+hx*math.sin(math.pi+math.pi*i/16)) for i in range(17)]+[(hx,hz),(-hx,hz)],-thick,thick)
        elif hz-z_at>1e-6:
            tris+=side_prism([(-hx,z_at),(hx,z_at),(hx,hz),(-hx,hz)],-thick,thick)
        g.append(mesh(f'arm_cad_forearm_{side}',tris))
        fore.insert(list(fore).index(saved),v)

if __name__=='__main__':
    update(Path(__file__).resolve().parent.parent/'johnny5_head.urdf',apply)
