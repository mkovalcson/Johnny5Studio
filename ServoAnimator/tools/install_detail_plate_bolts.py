"""Add the smaller counterbored fasteners in the side/underside detail plates."""
from studio_paths import ARCHIVE_ROOT, DOCUMENTATION_ROOT
from studio_paths import CONFIG_ROOT, HEAD_CAD, HARDWARE_CAD, SOLUTION_ROOT
import collections
import json
import math
import re
import shutil
import xml.etree.ElementTree as ET
import numpy as np
from install_head_bolts import ROOT, BACKUP, PREFIX, MATERIAL, MESH_NAME, rotation, angles, numbers, occupied
from select_head_bolt_seats import seat, DATA
from match_head_recesses import mesh

archive = ARCHIVE_ROOT / 'detail-plate-fasteners-20261103'
archive.mkdir(parents=True, exist_ok=True)
manifest_path = BACKUP/'fastener-placement-manifest.json'
manifest = json.loads(manifest_path.read_text())
if manifest.get('detailPlateSeats'):
    raise RuntimeError('Detail-plate bolts are already installed')
shutil.copy2(manifest_path,archive/'fastener-placement-manifest-before.json')
source_mesh = 'Meshes/SimplifiedHead2/static_020202.stl'
points, normals = mesh(CONFIG_ROOT / 'Models/Robots/Johnny5Head'/source_mesh)
seats = []; cache = {}
for index, cylinder in enumerate(DATA['SimplifiedHead2.step']['cylinders']):
    # This uniform recess size belongs to the black detail plates, not the
    # larger eye/panel hardware, through bores or rounded corners.
    if not cylinder['inward'] or abs(cylinder['radius']-1.651) > .001 or cylinder['u'][1]-cylinder['u'][0] < 6.2:
        continue
    if abs(abs(cylinder['v'][1]-cylinder['v'][0])-1.905) > .001: continue
    center, axis = np.array(cylinder['center']), np.array(cylinder['axis'])
    delta = points-center; v = delta@axis; radial = delta-v[:,None]*axis
    match = (abs(np.linalg.norm(radial,axis=1)-cylinder['radius'])<.015) & (v>min(cylinder['v'])-.015) & (v<max(cylinder['v'])+.015) & (np.sum(normals*radial,axis=1)<-.6*cylinder['radius'])
    count = int(match.sum())
    if count < 12: continue
    face = {'step':'SimplifiedHead2.step','face':index,**cylinder,'matches':[(source_mesh,count)]}
    fitted = seat(face)
    if fitted is None: continue
    outer, outward, ends = fitted
    face.update(outer=outer.tolist(),outward=outward.tolist(),shaft_radii=ends)
    if occupied(face,cache): continue
    seats.append(face)
assert len(seats) == 53, f'Unexpected detail-plate recess count: {len(seats)}'

for model, rows in manifest['models'].items():
    path = CONFIG_ROOT / f'Models/Robots/{model}/johnny5_head.urdf'
    shutil.copy2(path,archive/(model+'-before.urdf'))
    text = path.read_text(); doc = ET.fromstring(text); fragments = collections.defaultdict(list)
    for face in seats:
        axis = np.array(face['outward']); axis /= np.linalg.norm(axis)
        up = np.array([0.,0.,1.]) if abs(axis[2]) < .9 else np.array([0.,1.,0.])
        tangent = np.cross(up,-axis); tangent /= np.linalg.norm(tangent)
        bolt_rotation = np.column_stack((-axis,tangent,np.cross(-axis,tangent)))
        depth = abs(face['v'][1]-face['v'][0])
        radial_scale = face['radius']*.9/1.778
        axial_scale = min(radial_scale,depth/2.1844)
        for link in doc.findall('link'):
            for host in link.findall('visual'):
                geom = host.find('geometry/mesh')
                if geom is None or geom.get('filename','').replace('\\','/') != source_mesh: continue
                origin = host.find('origin'); offset = np.fromstring(origin.get('xyz','0 0 0'),sep=' ')
                host_rotation = rotation(np.fromstring(origin.get('rpy','0 0 0'),sep=' '))
                rim = offset+host_rotation@(np.array(face['outer'])/1000)
                direction = host_rotation@axis
                name = f'{PREFIX}{len(rows)+1:03d}'
                visual = ET.Element('visual',{'name':name})
                ET.SubElement(visual,'origin',{'xyz':numbers(rim),'rpy':numbers(angles(host_rotation@bolt_rotation))})
                ET.SubElement(ET.SubElement(visual,'geometry'),'mesh',{'filename':MESH_NAME,'scale':numbers([axial_scale/1000,radial_scale/1000,radial_scale/1000])})
                ET.SubElement(visual,'material',{'name':MATERIAL})
                ET.indent(visual,space='  ',level=2)
                fragments[link.get('name')].append('    '+ET.tostring(visual,encoding='unicode').rstrip()+'\n')
                rows.append({'name':name,'hostVisual':host.get('name'),'link':link.get('name'),
                    'cadStep':face['step'],'cadCylinderIndex':face['face'],'recessRadiusMm':face['radius'],
                    'recessDepthMm':depth,'radialScale':radial_scale,'axialScale':axial_scale,
                    'rimLinkMetres':rim.tolist(),'outwardLink':direction.tolist(),'detailPlate':True})
    for link, content in fragments.items():
        pattern = re.compile(r'(<link\s+name="'+re.escape(link)+r'"[^>]*>)(.*?)(  </link>)',re.S)
        text,count = pattern.subn(lambda m:m[1]+m[2]+''.join(content)+m[3],text)
        assert count == 1
    ET.fromstring(text);path.write_text(text,encoding='utf-8',newline='\n')
    assert len(rows) == 86
    print(model, 'total',len(rows),'detail plate',sum(bool(row.get('detailPlate')) for row in rows),flush=True)
manifest['emptySeats'] += len(seats)
manifest['detailPlateSeats'] = len(seats)
manifest_path.write_text(json.dumps(manifest,indent=2))
(archive/'detail-plate-seats.json').write_text(json.dumps(seats,indent=2))
