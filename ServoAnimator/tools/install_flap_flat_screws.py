"""Copy the existing tiny vent-fin screw into twelve measured flap seats."""
from studio_paths import ARCHIVE_ROOT, DOCUMENTATION_ROOT
from studio_paths import CONFIG_ROOT, HEAD_CAD, HARDWARE_CAD, SOLUTION_ROOT
import collections, hashlib, json, pathlib, re, shutil, xml.etree.ElementTree as ET
import numpy as np
from scipy.sparse import coo_matrix
from scipy.sparse.csgraph import connected_components
from match_head_recesses import ROOT, mesh, STL_DTYPE
from install_head_bolts import rotation, angles, numbers

ARCHIVE=ARCHIVE_ROOT / 'flap-flat-screws-20261103'
ASSET='Meshes/Fasteners/black_flat_head_vent_screw.stl'
PREFIX='flap_black_flat_screw_'
MATERIAL='flap_flat_head_black'
HOSTS={'upper_left_flap_panel':4,'upper_right_flap_panel':4,'lower_left_flap_panel':2,'lower_right_flap_panel':2}

def source_screw(folder):
    path=folder/'static_97a3da.stl';p,_=mesh(path)
    vertices,inv=np.unique(np.round(p,5),axis=0,return_inverse=True);tri=inv.reshape(-1,3)
    row=np.r_[tri[:,0],tri[:,1],tri[:,2]];col=np.r_[tri[:,1],tri[:,2],tri[:,0]]
    _,labels=connected_components(coo_matrix((np.ones(len(row)),(row,col)),shape=(len(vertices),len(vertices))),directed=False)
    seat=json.loads((ROOT/'tools/head-bolt-analysis.json').read_text())['SimplifiedHead2.step']['cylinders'][1453]
    center=np.array(seat['center']);out=np.array(seat['axis'])
    label=labels[np.argmin(np.linalg.norm(vertices-(center-out*.4),axis=1))]
    selection=labels[tri[:,0]]==label;points=p.reshape(-1,3,3)[selection]
    x=np.array([0.,0,1]);y=np.cross(out,x);basis=np.column_stack([x,y,out])
    transformed=(points-center)@basis;top=float(transformed[:,:,2].max());transformed[:,:,2]-=top
    radius=float(np.linalg.norm(transformed[:,:,:2],axis=2).max());length=float(-transformed[:,:,2].min())
    assert 1.24<radius<1.28 and 3.16<length<3.19,(radius,length)
    data=np.zeros(len(points),dtype=STL_DTYPE);data['p']=transformed
    normals=np.cross(transformed[:,1]-transformed[:,0],transformed[:,2]-transformed[:,0]);normals/=np.linalg.norm(normals,axis=1)[:,None];data['n']=normals
    content=b'Copied vent fin screw; head face Z=0; shank -Z'.ljust(80,b' ')+np.uint32(len(data)).tobytes()+data.tobytes()
    return content,dict(source=str(path),sourceSha256=hashlib.sha256(path.read_bytes()).hexdigest(),cadCylinder=1453,headRadiusMm=radius,lengthMm=length,triangles=len(data))

def holes(path,expected):
    p,_=mesh(path);p=p.reshape(-1,3,3);cross=np.cross(p[:,1]-p[:,0],p[:,2]-p[:,0]);normal=cross[np.argmax(np.linalg.norm(cross,axis=1))];normal/=np.linalg.norm(normal)
    if normal[1]<0:normal=-normal
    x=np.array([1.,0,0]);x-=normal*(x@normal);x/=np.linalg.norm(x);y=np.cross(normal,x);dot=p@normal
    found=[]
    for side in [-1,1]:
        level=dot.min() if side<0 else dot.max();ts=p[np.max(abs(dot-level),axis=1)<.003]
        if not len(ts):continue
        v,inv=np.unique(np.round(ts.reshape(-1,3),5),axis=0,return_inverse=True);tri=inv.reshape(-1,3)
        edges=collections.Counter(tuple(sorted(e)) for t in tri for e in [(t[0],t[1]),(t[1],t[2]),(t[2],t[0])]);es=np.array([e for e,c in edges.items() if c==1])
        if not len(es):continue
        used=np.unique(es);_,labels=connected_components(coo_matrix((np.ones(len(es)),(es[:,0],es[:,1])),shape=(len(v),len(v))),directed=False)
        for label in set(labels[used]):
            points=v[np.intersect1d(used,np.where(labels==label)[0])]
            if len(points)<20:continue
            q=np.column_stack([points@x,points@y]);a=np.column_stack([2*q,np.ones(len(q))]);u,w,k=np.linalg.lstsq(a,(q*q).sum(1),rcond=None)[0];radius=np.sqrt(max(0,k+u*u+w*w));error=abs(np.linalg.norm(q-[u,w],axis=1)-radius).max()
            if abs(radius-1.5113)<.002 and error<.0001:
                found.append(dict(centerCadMm=(x*u+y*w+normal*level).tolist(),outwardCad=(normal*side).tolist(),holeRadiusMm=float(radius),fitErrorMm=float(error)))
    assert len(found)==expected,(path,found)
    return sorted(found,key=lambda s:tuple(s['centerCadMm']))

def main():
    assert not ARCHIVE.exists(),'Already installed; refuse duplicate installation'
    base=CONFIG_ROOT / 'Models/Robots/Johnny5Head';folder=base/'Meshes/SimplifiedHead2';asset,source=source_screw(folder)
    seats={name:holes(folder/(name+'.stl'),count) for name,count in HOSTS.items()};prepared=[];manifest=dict(mesh=ASSET,material=MATERIAL,source=source,models={})
    for model in ['Johnny5Head','Johnny5FullBody']:
        root=CONFIG_ROOT / 'Models/Robots'/model;path=root/'johnny5_head.urdf';text=path.read_text(encoding='utf-8-sig');doc=ET.fromstring(text)
        assert not any(v.get('name','').startswith(PREFIX) for v in doc.iter('visual'))
        rows=[]
        for host,group in seats.items():
            assert hashlib.sha256((folder/(host+'.stl')).read_bytes()).digest()==hashlib.sha256((root/'Meshes/SimplifiedHead2'/(host+'.stl')).read_bytes()).digest()
            visual=next(v for v in doc.iter('visual') if v.get('name')==host);link=next(l for l in doc.findall('link') if visual in list(l));origin=visual.find('origin');r=rotation([float(x) for x in origin.get('rpy').split()]);offset=np.array([float(x) for x in origin.get('xyz').split()]);additions=[]
            for index,seat in enumerate(group,1):
                n=r@np.array(seat['outwardCad']);pos=r@(np.array(seat['centerCadMm'])*.001)+offset
                x=r@np.array([1.,0,0]);x-=n*(x@n);x/=np.linalg.norm(x);y=np.cross(n,x);rpy=angles(np.column_stack([x,y,n]));name=PREFIX+host+'_'+str(index)
                additions.append(f'    <visual name="{name}">\n      <origin xyz="{numbers(pos)}" rpy="{numbers(rpy)}" />\n      <geometry><mesh filename="{ASSET}" scale="0.001 0.001 0.001" /></geometry>\n      <material name="{MATERIAL}" />\n    </visual>\n')
                rows.append(dict(name=name,link=link.get('name'),hostVisual=host,seatLinkMetres=pos.tolist(),outwardLink=n.tolist(),**seat))
            pattern=r'(<link name="'+re.escape(link.get('name'))+r'">[\s\S]*?)(  </link>)'
            text,count=re.subn(pattern,lambda m:m[1]+''.join(additions)+m[2],text,count=1);assert count==1
        text=text.replace('  <link ',f'  <material name="{MATERIAL}"><color rgba="0.018 0.018 0.018 1" /></material>\n  <link ',1)
        ET.fromstring(text);prepared.append((model,path,text));manifest['models'][model]=rows
    ARCHIVE.mkdir(parents=True)
    for model,path,text in prepared:
        shutil.copy2(path,ARCHIVE/(model+'-before.urdf'));target=path.parent/ASSET;target.parent.mkdir(exist_ok=True);target.write_bytes(asset);path.write_text(text,encoding='utf-8');print(model, '12 black flat-head screws installed',flush=True)
    manifest['meshSha256']=hashlib.sha256(asset).hexdigest();(ARCHIVE/'placement-manifest.json').write_text(json.dumps(manifest,indent=2))

if __name__=='__main__':main()
