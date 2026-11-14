"""Rebuild only photo-derived outer track details; preserve unrelated URDF data."""
from pathlib import Path
import math, struct, re, json, sys
import xml.etree.ElementTree as E
sys.dont_write_bytecode=True
from track_outer_details import add_outer_track_details
root=Path(__file__).resolve().parent.parent
path=root/'johnny5_head.urdf'
s=path.read_text(encoding='utf-8-sig')
# Only replace the detail links owned by this script.
for tag in ('link','joint'):
    s=re.sub(r'  <'+tag+r' name="body_(?:track_outer_guard_|track_front_hub_detail_)[^"]+"[^>]*>.*?</'+tag+r'>\s*\n','',s,flags=re.S)
tree=E.fromstring(s);out=[];hub_links={'left':[],'right':[]}
mats={'gray':'step_steel_satin','dark':'cad_404040','light':'cad_b3b3b3','shadow':'cad_020202'}
def add(name,c,rpy,geometry,material):
    if name.startswith('track_front_hub_detail_'):hub_links[name.rsplit('_',1)[1]].append('body_'+name+'_link')
    name='body_'+name
    link=E.Element('link',name=name+'_link');visual=E.SubElement(link,'visual',name=name+'_visual')
    E.SubElement(visual,'origin',xyz='0 0 0',rpy=' '.join(f'{v:.6f}' for v in rpy))
    g=E.SubElement(visual,'geometry');g.append(geometry);E.SubElement(visual,'material',name=mats[material])
    joint=E.Element('joint',name=name+'_fixed',type='fixed')
    E.SubElement(joint,'parent',link='body_root_link');E.SubElement(joint,'child',link=name+'_link')
    E.SubElement(joint,'origin',xyz=' '.join(f'{v:.6f}' for v in c),rpy='0 0 0')
    for el in (link,joint):E.indent(el,space='  ');out.append('  '+E.tostring(el,encoding='unicode').replace('\n','\n  '))
def cyl(name,c,radius,length,material,axis='y'):
    add(name,c,(math.pi/2,0,0),E.Element('cylinder',radius=f'{radius:.6f}',length=f'{length:.6f}'),material)
def strut(name,a,b,width,depth,material):
    d=[q-p for p,q in zip(a,b)];length=math.sqrt(sum(v*v for v in d));u=[v/length for v in d]
    add(name,[(a[i]+b[i])/2 for i in range(3)],(-math.asin(u[1]),math.atan2(u[0],u[2]),0),E.Element('box',size=f'{width:.6f} {depth:.6f} {length:.6f}'),material)
def triangulate(poly):
    cross=lambda a,b,c:(b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0])
    ids=list(range(len(poly)));tri=[]
    while len(ids)>3:
        for k,b in enumerate(ids):
            a=ids[k-1];c=ids[(k+1)%len(ids)]
            if cross(poly[a],poly[b],poly[c])<=1e-12:continue
            if any(all(cross(poly[q],poly[t],poly[j])>=-1e-12 for q,t in ((a,b),(b,c),(c,a))) for j in ids if j not in (a,b,c)):continue
            tri.append((a,b,c));ids.pop(k);break
        else:raise ValueError('Cannot triangulate panel')
    return tri+[tuple(ids)]
def panel(name,poly,ya,yb,material,offset):
    if sum(poly[i][0]*poly[(i+1)%len(poly)][1]-poly[(i+1)%len(poly)][0]*poly[i][1] for i in range(len(poly)))<0:poly=list(reversed(poly))
    lo,hi=sorted((ya,yb));a=[(x,lo+offset(x),z) for x,z in poly];b=[(x,hi+offset(x),z) for x,z in poly];tris=[]
    for i,j,k in triangulate(poly):tris.extend([(a[i],a[j],a[k]),(b[i],b[k],b[j])])
    for i in range(len(poly)):
        j=(i+1)%len(poly);tris.extend([(a[i],b[i],b[j]),(a[i],b[j],a[j])])
    mesh=root/'Meshes/Body'/f'{name}.stl'
    with mesh.open('wb') as f:
        f.write(b'Johnny 5 photo-matched outer track details'.ljust(80,b' '));f.write(struct.pack('<I',len(tris)))
        for t in tris:
            u=[t[1][i]-t[0][i] for i in range(3)];v=[t[2][i]-t[0][i] for i in range(3)];n=[u[1]*v[2]-u[2]*v[1],u[2]*v[0]-u[0]*v[2],u[0]*v[1]-u[1]*v[0]];length=math.sqrt(sum(v*v for v in n)) or 1
            f.write(struct.pack('<12fH',*[v/length for v in n],*[v for p in t for v in p],0))
    add(name,(0,0,0),(0,0,0),E.Element('mesh',filename='Meshes/Body/'+mesh.name,scale='1 1 1'),material)
for side,sign in (('left',1),('right',-1)):
    x,y,z=map(float,tree.find(f"joint[@name='body_track_front_roller_{side}_fixed']/origin").get('xyz').split())
    # The front barrel is recessed independently; use the unchanged rear
    # roller to locate the belt centre, rather than the shortened barrel centre.
    y=float(tree.find(f"joint[@name='body_track_rear_roller_{side}_fixed']/origin").get('xyz').split()[1])
    add_outer_track_details(side,sign,(x,z),y,.170,cyl,strut,panel)
path.write_text(s.replace('</robot>','\n'+'\n'.join(out)+'\n</robot>'),encoding='utf-8')
manifest=root/'Meshes/Body/Treads/tread_animation.json';m=json.loads(manifest.read_text(encoding='utf-8-sig'))
for side in ('left','right'):
    rot=m['sides'][side]['rotators'][0]
    rot['urdfLinks']=sorted(set([n for n in rot['urdfLinks'] if not n.startswith('body_track_front_hub_detail_')]+hub_links[side]))
manifest.write_text(json.dumps(m,indent=2)+'\n',encoding='utf-8')
print(f'Rebuilt {len(out)//2} links; exposed hubs follow tread rotation, supports remain fixed.')
