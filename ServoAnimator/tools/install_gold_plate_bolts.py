"""Fit the four gold whip-antenna plate counterbores in both managed models."""
from studio_paths import ARCHIVE_ROOT, DOCUMENTATION_ROOT
from studio_paths import CONFIG_ROOT, HEAD_CAD, HARDWARE_CAD, SOLUTION_ROOT
import collections
import json
import re
import shutil
import xml.etree.ElementTree as ET
import numpy as np
from audit_gold_plate_bolts import gold_seats, SOURCE
from install_head_bolts import ROOT, BACKUP, PREFIX, MATERIAL, MESH_NAME, rotation, angles, numbers

manifest_path=BACKUP/'fastener-placement-manifest.json'
manifest=json.loads(manifest_path.read_text())
if manifest.get('goldWhipPlateSeats'):raise RuntimeError('Gold plate fasteners already installed')
seats=gold_seats()
assert len(seats)==4 and {s['face'] for s in seats}=={1290,1292,1294,1296}
archive=ARCHIVE_ROOT / 'gold-whip-plate-fasteners-20261103'
archive.mkdir(parents=True,exist_ok=True)
saved_manifest=archive/'fastener-placement-manifest-before.json'
if saved_manifest.exists():
    assert saved_manifest.read_bytes()==manifest_path.read_bytes(), 'Manifest changed since backup; refuse an uncertain retry'
else:shutil.copy2(manifest_path,saved_manifest)
for model,rows in manifest['models'].items():
    path=CONFIG_ROOT / f'Models/Robots/{model}/johnny5_head.urdf'
    assert len(rows)==86
    saved=archive/(model+'-before.urdf')
    if saved.exists():assert saved.read_bytes()==path.read_bytes(), 'Model changed since backup; refuse an uncertain retry'
    else:shutil.copy2(path,saved)
    text=path.read_text(encoding='utf-8-sig');doc=ET.fromstring(text);fragments=collections.defaultdict(list)
    for face in seats:
        axis=np.array(face['outward']);axis/=np.linalg.norm(axis)
        up=np.array([0.,0.,1.]) if abs(axis[2])<.9 else np.array([0.,1.,0.])
        tangent=np.cross(up,-axis);tangent/=np.linalg.norm(tangent)
        bolt_rotation=np.column_stack((-axis,tangent,np.cross(-axis,tangent)))
        depth=abs(face['v'][1]-face['v'][0]);radial_scale=face['radius']*.9/1.778
        axial_scale=min(radial_scale,depth/2.1844)
        hosts=0
        for link in doc.findall('link'):
            for host in link.findall('visual'):
                geometry=host.find('geometry/mesh')
                if geometry is None or geometry.get('filename','').replace('\\','/')!=SOURCE:continue
                hosts+=1;origin=host.find('origin');offset=np.fromstring(origin.get('xyz','0 0 0'),sep=' ')
                host_rotation=rotation(np.fromstring(origin.get('rpy','0 0 0'),sep=' '))
                rim=offset+host_rotation@(np.array(face['outer'])/1000);direction=host_rotation@axis
                name=f'{PREFIX}{len(rows)+1:03d}'
                visual=ET.Element('visual',{'name':name})
                ET.SubElement(visual,'origin',{'xyz':numbers(rim),'rpy':numbers(angles(host_rotation@bolt_rotation))})
                ET.SubElement(ET.SubElement(visual,'geometry'),'mesh',{'filename':MESH_NAME,'scale':numbers([axial_scale/1000,radial_scale/1000,radial_scale/1000])})
                ET.SubElement(visual,'material',{'name':MATERIAL});ET.indent(visual,space='  ',level=2)
                fragments[link.get('name')].append('    '+ET.tostring(visual,encoding='unicode').rstrip()+'\n')
                rows.append({'name':name,'hostVisual':host.get('name'),'link':link.get('name'),'cadStep':face['step'],
                    'cadCylinderIndex':face['face'],'recessRadiusMm':face['radius'],'recessDepthMm':depth,
                    'radialScale':radial_scale,'axialScale':axial_scale,'rimLinkMetres':rim.tolist(),
                    'outwardLink':direction.tolist(),'goldWhipPlate':True})
        assert hosts==1
    for link,content in fragments.items():
        pattern=re.compile(r'(<link\s+name="'+re.escape(link)+r'"[^>]*>)(.*?)(  </link>)',re.S)
        text,count=pattern.subn(lambda m:m[1]+m[2]+''.join(content)+m[3],text);assert count==1
    ET.fromstring(text);path.write_text(text,encoding='utf-8',newline='\n')
    assert len(rows)==90
    print(model,'gold plate bolts',4,'total',len(rows),flush=True)
manifest['emptySeats']+=4;manifest['goldWhipPlateSeats']=4
manifest_path.write_text(json.dumps(manifest,indent=2))
(archive/'gold-plate-seats.json').write_text(json.dumps(seats,indent=2))
