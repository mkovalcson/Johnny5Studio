"""CAD-reference hand detail and independent finger-end and thumb-tip joints.

Postprocesses only hand links, preserving their original frames and silhouettes.
The original solid visual is retained as metadata for repeatable regeneration.
"""
import copy
import math
import xml.etree.ElementTree as E
import numpy as np

BLUE = 'johnny5_hand_blue'
BRASS = 'johnny5_hand_brass'
GRAY = 'step_steel_satin'
LIGHT = 'cad_b3b3b3'
BLACK = 'cad_020202'

def dome_mesh():
    """A single shallow convex cap, avoiding intersecting sphere/cylinder faces."""
    from pathlib import Path
    import struct
    path=Path(__file__).resolve().parent.parent/'Meshes/Body/hand_joint_dome.stl'
    rings=7;segments=40;tri=[]
    def p(i,j):
        r=.0115*i/rings;a=2*math.pi*j/segments
        return np.array([r*math.cos(a),r*math.sin(a),.003*(1-(i/rings)**2)])
    for i in range(rings):
        for j in range(segments):
            a,b,c,d=p(i,j),p(i+1,j),p(i+1,j+1),p(i,j+1)
            tri.append((a,b,c))
            if i:tri.append((a,c,d))
    data=bytearray(b'CAD hand joint dome'.ljust(80,b'\0')+struct.pack('<I',len(tri)))
    for a,b,c in tri:
        n=np.cross(b-a,c-a);n/=np.linalg.norm(n)
        data.extend(struct.pack('<12fH',*n,*a,*b,*c,0))
    if not path.exists() or path.read_bytes()!=data:path.write_bytes(data)
    return 'Meshes/Body/hand_joint_dome.stl'

def vec(s): return np.array([float(v) for v in s.split()])
def fmt(v): return ' '.join(f'{x:.9f}' for x in v)
def rotation(rpy):
    r,p,y=rpy;cr,sr=math.cos(r),math.sin(r);cp,sp=math.cos(p),math.sin(p);cy,sy=math.cos(y),math.sin(y)
    return np.array([[cy,-sy,0],[sy,cy,0],[0,0,1]])@np.array([[cp,0,sp],[0,1,0],[-sp,0,cp]])@np.array([[1,0,0],[0,cr,-sr],[0,sr,cr]])
def angles(R):
    return (math.atan2(R[2,1],R[2,2]),math.atan2(-R[2,0],math.hypot(R[0,0],R[1,0])),math.atan2(R[1,0],R[0,0]))
def transform(e):
    o=e.find('origin');T=np.eye(4)
    if o is not None:T[:3,3]=vec(o.get('xyz','0 0 0'));T[:3,:3]=rotation(vec(o.get('rpy','0 0 0')))
    return T

def apply(root):
    dome=dome_mesh()
    links={e.get('name'):e for e in root.findall('link')}
    joints={e.find('child').get('link'):e for e in root.findall('joint')}
    def world(n):
        j=joints.get(n)
        return np.eye(4) if j is None else world(j.find('parent').get('link'))@transform(j)
    for name,rgba in ((BLUE,'.32 .51 .68 1'),(BRASS,'.52 .43 .21 1')):
        if root.find(f"material[@name='{name}']") is None:
            E.SubElement(E.SubElement(root,'material',name=name),'color',rgba=rgba)
    def visual(link,label,T,pos,shape,mat,rpy=(0,0,0)):
        v=E.SubElement(link,'visual',name='cad_detail_'+link.get('name')+'_'+label)
        E.SubElement(v,'origin',xyz=fmt(T[:3,:3]@pos+T[:3,3]),rpy=fmt(angles(T[:3,:3]@rotation(rpy))))
        E.SubElement(v,'geometry').append(shape);E.SubElement(v,'material',name=mat)
    def box(link,label,T,pos,size,mat):visual(link,label,T,np.array(pos),E.Element('box',size=fmt(size)),mat)
    def cyl(link,label,T,pos,r,l,mat,rpy=(math.pi/2,0,0)):
        visual(link,label,T,np.array(pos),E.Element('cylinder',radius=str(r),length=str(l)),mat,rpy)

    # Preserve the zero pose while introducing a joint exactly at the existing
    # blue thumb bend caps. The old base curl remains the parent control.
    for side in ('left','right'):
        seg=f'body_seg_thumb_{side}_link';tipseg=f'body_seg_thumbtip_{side}_link'
        if tipseg not in links:
            pads=[joints[f'body_thumb_pad_bend_{side}_{s}_base_link'] for s in ('p','n')]
            pivot=sum((transform(j)[:3,3] for j in pads))/2
            link=E.SubElement(root,'link',name=tipseg);links[tipseg]=link
            j=E.SubElement(root,'joint',name='ThumbTipCurl'+side.title(),type='revolute')
            E.SubElement(j,'parent',link=seg);E.SubElement(j,'child',link=tipseg)
            E.SubElement(j,'origin',xyz=fmt(pivot),rpy='0 0 0')
            E.SubElement(j,'axis',xyz=joints[seg].find('axis').get('xyz'))
            E.SubElement(j,'limit',lower='-0.3',upper='0.006650',effort='5',velocity='2')
            joints[tipseg]=j
            for name,old in list(joints.items()):
                if name.startswith((f'body_thumb_tip_{side}_',f'body_thumb_pad_bend_{side}_')) and old.find('parent').get('link')==seg:
                    old.find('parent').set('link',tipseg)
                    old.find('origin').set('xyz',fmt(transform(old)[:3,3]-pivot))
        # Exposed centre bearing between the blue caps, split into silver bands.
        link=links[tipseg]
        for v in list(link.findall('visual')):
            if v.get('name','').startswith('cad_detail_'):link.remove(v)
        T=np.eye(4);T[:3,:3]=transform(links[f'body_thumb_knuckle_{side}_link'].find('visual'))[:3,:3]
        cyl(link,'thumb_bend_bearing',T,(0,0,0),.0085,.020,BRASS,(0,0,0))
        for i,zz in enumerate((-.007,0,.007)):
            cyl(link,f'thumb_bend_ring_{i}',T,(0,0,zz),.009,.002,LIGHT,(0,0,0))

    # The paired fingers also have an outer hinge, beyond the existing middle
    # knuckle. Keep both earlier axes and reparent only the final tip assembly.
    for side in ('left','right'):
        for fi,label in ((0,'Upper'),(1,'Lower')):
            seg=f'body_seg_fingertip{fi}_{side}_link'
            endseg=f'body_seg_fingerend{fi}_{side}_link'
            if endseg not in links:
                pads=[joints[f'body_finger_pad_bend_{side}_{fi}_{s}_base_link'] for s in ('p','n')]
                pivot=sum((transform(j)[:3,3] for j in pads))/2
                link=E.SubElement(root,'link',name=endseg);links[endseg]=link
                j=E.SubElement(root,'joint',name='FingerEndCurl'+label+side.title(),type='revolute')
                E.SubElement(j,'parent',link=seg);E.SubElement(j,'child',link=endseg)
                E.SubElement(j,'origin',xyz=fmt(pivot),rpy='0 0 0')
                E.SubElement(j,'axis',xyz=joints[seg].find('axis').get('xyz'))
                E.SubElement(j,'limit',lower='-0.3',upper='0.007600',effort='5',velocity='2')
                joints[endseg]=j
                for name,old in list(joints.items()):
                    if name.startswith((f'body_finger_tip_{side}_{fi}_',f'body_finger_pad_bend_{side}_{fi}_')) and old.find('parent').get('link')==seg:
                        old.find('parent').set('link',endseg)
                        old.find('origin').set('xyz',fmt(transform(old)[:3,3]-pivot))
            link=links[endseg]
            for v in list(link.findall('visual')):
                if v.get('name','').startswith('cad_detail_'):link.remove(v)
            T=np.eye(4);T[:3,:3]=transform(links[f'body_finger_knuckle_{side}_{fi}_link'].find('visual'))[:3,:3]
            cyl(link,'finger_end_bearing',T,(0,0,0),.0085,.019,BRASS,(0,0,0))
            for i,zz in enumerate((-.0065,0,.0065)):
                cyl(link,f'finger_end_ring_{i}',T,(0,0,zz),.009,.002,LIGHT,(0,0,0))

    for name,link in links.items():
        if name.startswith('body_finger_') or name.startswith('body_thumb_'):
            if '_pad_' in name or name.startswith('body_finger_base_pad_'):
                for mat in link.findall('visual/material'):mat.set('name',BLUE)
                if name.endswith('_dome_link'):
                    v=link.find('visual');base=links.get(name.replace('_dome_link','_base_link'))
                    if v is not None and v.find('geometry/sphere') is not None and base is not None:
                        # Outward points from the old sphere centre toward the
                        # matching blue cylinder. Place the cap on its outer face.
                        W=world(name);B=world(base.get('name'))@transform(base.find('visual'))
                        outward=B[:3,3]-(W@transform(v))[:3,3];outward/=np.linalg.norm(outward)
                        axis=W[:3,:3].T@outward
                        ref=np.array([1.,0,0]) if abs(axis[0])<.9 else np.array([0.,1,0])
                        u=np.cross(ref,axis);u/=np.linalg.norm(u)
                        R=np.column_stack((u,np.cross(axis,u),axis))
                        center=B[:3,3]+outward*.0021
                        v.find('origin').set('xyz',fmt((np.linalg.inv(W)@np.append(center,1))[:3]))
                        v.find('origin').set('rpy',fmt(angles(R)))
                        g=v.find('geometry');g.remove(g.find('sphere'));E.SubElement(g,'mesh',filename=dome)
        isbar=any(name==f'body_{part}_{side}{suffix}_link'
            for side in ('left','right')
            for part,suffix in [('finger_prox','_0'),('finger_prox','_1'),('finger_dist','_0'),('finger_dist','_1'),('finger_tip','_0'),('finger_tip','_1'),('thumb_prox',''),('thumb_tip','')])
        if not isbar:continue
        saved=link.find('johnny5_hand_detail_source')
        if saved is None:
            original=link.find('visual')
            if original is None or original.find('geometry/box') is None:continue
            saved=E.SubElement(link,'johnny5_hand_detail_source');saved.append(copy.deepcopy(original))
        original=saved.find('visual');T=transform(original);sx,sy,length=vec(original.find('geometry/box').get('size'))
        for v in list(link.findall('visual')):link.remove(v)
        side='left' if '_left' in name else 'right'
        other=f'body_finger_dist_{side}_0_link' if 'thumb_' in name else f'body_thumb_prox_{side}_link'
        local=np.linalg.inv(world(name)@T)@np.append(world(other)[:3,3],1)
        grip=1 if local[0]>=0 else -1
        # Thin side plates expose the internal mechanism at each segment end.
        for sign in (-1,1):
            box(link,f'cheek_{sign}',T,(0,sign*(sy/2-.0012),0),(sx,.0024,length),GRAY)
            for i,zz in enumerate((-length*.29,length*.29)):
                cyl(link,f'screw_{sign}_{i}',T,(0,sign*(sy/2+.0003),zz),.0018,.0007,LIGHT)
                box(link,f'screw_slot_{sign}_{i}',T,(0,sign*(sy/2+.00071),zz),(.0005,.0002,.0021),BLACK)
        cover=max(.006,length-.012)
        box(link,'backing',T,(-grip*(sx/2-.001),0,0),(.002,sy-.0048,cover),LIGHT)
        box(link,'grip_carrier',T,(grip*(sx/2-.002),0,0),(.002,sy-.005,cover),BLACK)
        ribs=max(3,int(cover/.0027))
        for i in range(ribs):
            zz=-cover/2+(i+.5)*cover/ribs
            box(link,f'grip_rib_{i}',T,(grip*(sx/2-.0005),0,zz),(.001,sy-.004,.0013),BLACK)
        cyl(link,'internal_rod',T,(0,0,0),.0022,max(.003,length-.004),LIGHT,(0,0,0))
        for i,zz in enumerate((-length/2+.003,length/2-.003)):
            cyl(link,f'hinge_axle_{i}',T,(0,0,zz),.0036,sy-.003,LIGHT)
            cyl(link,f'hinge_bush_{i}',T,(0,0,zz),.0044,sy*.40,BRASS)

    # Replaced coarse ridges used to float over the solid bars. Keep their
    # original links/frames for existing clips, but use the new fine grip pads.
    for name,link in links.items():
        if name.startswith(('body_finger_ridge_','body_thumb_ridge_')):
            for v in list(link.findall('visual')):link.remove(v)

def update(path, processor=apply):
    import re
    text=path.read_text(encoding='utf-8-sig');root=E.fromstring(text)
    def serialized(element):
        # ElementTree includes a root element's following whitespace in
        # tostring(). Excluding that tail prevents each focused regeneration
        # from copying and multiplying the space between URDF entries.
        tail=element.tail;element.tail=None
        try:return E.tostring(element)
        finally:element.tail=tail
    old={(e.tag,e.get('name')):serialized(e) for e in root if e.get('name')}
    processor(root)
    # A renamed joint must replace its old entry, not leave two joints with
    # the same child link in the model.
    current={(e.tag,e.get('name')) for e in root if e.get('name')}
    for tag,name in old.keys()-current:
        pattern=r'  <'+tag+r' name="'+re.escape(name)+r'"(?:[^>]*?/>|[^>]*>.*?</'+tag+r'>)\s*\n'
        text,n=re.subn(pattern,'',text,count=1,flags=re.S)
        if n!=1:raise ValueError('Cannot retire '+name)
    additions=[]
    for e in root:
        key=(e.tag,e.get('name'))
        if not key[1] or old.get(key)==serialized(e):continue
        E.indent(e,space='  ')
        tail=e.tail;e.tail=None
        try:out='  '+E.tostring(e,encoding='unicode').replace('\n','\n  ')
        finally:e.tail=tail
        if key in old:
            pattern=r'  <'+e.tag+r' name="'+re.escape(key[1])+r'"(?:[^>]*?/>|[^>]*>.*?</'+e.tag+r'>)'
            text,n=re.subn(pattern,lambda _:out,text,count=1,flags=re.S)
            if n!=1:raise ValueError('Cannot replace '+str(key))
        else:additions.append(out)
    text=text.replace('</robot>','\n'+'\n'.join(additions)+'\n</robot>')
    path.write_text(re.sub(r'(?m)^[ \t]*\n','',text),encoding='utf-8')

if __name__=='__main__':
    from pathlib import Path
    update(Path(__file__).resolve().parent.parent/'johnny5_head.urdf')
