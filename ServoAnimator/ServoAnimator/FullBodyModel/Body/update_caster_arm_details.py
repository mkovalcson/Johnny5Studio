"""Update the caster support details without regenerating unrelated model parts."""
from pathlib import Path
import math, re, struct, sys
import xml.etree.ElementTree as E
sys.dont_write_bytecode=True
from caster_arm_details import add_caster_arm_details
root=Path(__file__).resolve().parent.parent
path=root/'johnny5_head.urdf';s=path.read_text(encoding='utf-8-sig')
for tag in ('link','joint'):
    s=re.sub(r'  <'+tag+r' name="body_caster_detail_[^"]+"[^>]*>.*?</'+tag+r'>\s*\n','',s,flags=re.S)
tree=E.fromstring(s)
def xyz(name):return tuple(map(float,tree.find(f"joint[@name='body_{name}_fixed']/origin").get('xyz').split()))
collar=xyz('caster_collar');arm=xyz('caster_arm')
mesh=tree.find("link[@name='body_caster_arm_link']/visual/geometry/mesh").get('filename')
b=(root/mesh).read_bytes();n=struct.unpack_from('<I',b,80)[0];verts=[]
for i in range(n):
    data=struct.unpack_from('<12fH',b,84+i*50)
    verts.extend([data[3:6],data[6:9],data[9:12]])
top=max(v[2] for v in verts)+arm[2];width=max(v[1] for v in verts)-min(v[1] for v in verts)
materials={'gray':'step_steel_satin','light':'cad_b3b3b3','dark':'cad_404040','black':'black','shadow':'cad_020202'}
out=[]
def add(name,c,rpy,geom,material):
    parent='body_caster_collar_link' if name.startswith('caster_detail_collar_') else 'body_root_link'
    pos=[c[i]-collar[i] for i in range(3)] if parent=='body_caster_collar_link' else c
    name='body_'+name
    link=E.Element('link',name=name+'_link');v=E.SubElement(link,'visual',name=name+'_visual')
    E.SubElement(v,'origin',xyz='0 0 0',rpy=' '.join(f'{a:.6f}' for a in rpy));E.SubElement(v,'geometry').append(geom);E.SubElement(v,'material',name=materials[material])
    j=E.Element('joint',name=name+'_fixed',type='fixed');E.SubElement(j,'parent',link=parent);E.SubElement(j,'child',link=name+'_link');E.SubElement(j,'origin',xyz=' '.join(f'{a:.6f}' for a in pos),rpy='0 0 0')
    for e in (link,j):E.indent(e,space='  ');out.append('  '+E.tostring(e,encoding='unicode').replace('\n','\n  '))
def cyl(name,c,r,length,material,axis='z'):
    add(name,c,{'x':(0,math.pi/2,0),'y':(math.pi/2,0,0),'z':(0,0,0)}[axis],E.Element('cylinder',radius=f'{r:.6f}',length=f'{length:.6f}'),material)
def box(name,c,size,material):add(name,c,(0,0,0),E.Element('box',size=' '.join(f'{a:.6f}' for a in size)),material)
def segment(name,a,b,geometry,material):
    d=[b[i]-a[i] for i in range(3)];length=math.sqrt(sum(v*v for v in d));u=[v/length for v in d]
    add(name,[(a[i]+b[i])/2 for i in range(3)],(-math.asin(u[1]),math.atan2(u[0],u[2]),0),geometry(length),material)
def rod(name,a,b,r,material):segment(name,a,b,lambda l:E.Element('cylinder',radius=f'{r:.6f}',length=f'{l:.6f}'),material)
def strut(name,a,b,w,d,material):segment(name,a,b,lambda l:E.Element('box',size=f'{w:.6f} {d:.6f} {l:.6f}'),material)
def panel(name,polygon,ya,yb,material):
    # Convex side profile, extruded into a thin shell cheek.
    verts=[(px,yy,pz) for yy in (ya,yb) for px,pz in polygon]
    n=len(polygon);tri=[]
    for i in range(1,n-1):
        tri.extend([(verts[0],verts[i+1],verts[i]),(verts[n],verts[n+i],verts[n+i+1])])
    for i in range(n):
        j=(i+1)%n
        tri.extend([(verts[i],verts[j],verts[n+j]),(verts[i],verts[n+j],verts[n+i])])
    rel='Meshes/Body/'+name+'.stl'
    with (root/rel).open('wb') as f:
        f.write(b'Caster cylinder shroud'.ljust(80,b'\0'));f.write(struct.pack('<I',len(tri)))
        for t in tri:f.write(struct.pack('<12fH',0,0,0,*[v for p in t for v in p],0))
    add(name,(0,0,0),(0,0,0),E.Element('mesh',filename=rel),material)
add_caster_arm_details(collar,top,width,cyl,rod,strut,box,panel)
path.write_text(s.replace('</robot>','\n  <!-- Caster collar brace, cylinders, hinge and deeper side cheeks -->\n'+'\n'.join(out)+'\n</robot>'),encoding='utf-8')
print(f'Added {len(out)//2} caster detail parts. Arm top {top:.4f} m; width {width:.4f} m.')
