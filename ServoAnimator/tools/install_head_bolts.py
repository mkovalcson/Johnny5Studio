"""Install CAD-verified recessed fastener visuals in both managed robot models.

Source geometry stays unchanged. Fasteners are ordinary URDF visuals on the same
link as their surrounding part, so normal playback, disassembly and packaging
work without a renderer-specific attachment system.
"""
from studio_paths import ARCHIVE_ROOT, DOCUMENTATION_ROOT
from studio_paths import CONFIG_ROOT, HEAD_CAD, HARDWARE_CAD, SOLUTION_ROOT
import collections
import hashlib
import json
import math
import pathlib
import re
import shutil
import xml.etree.ElementTree as ET
import numpy as np
from OCP.BRepMesh import BRepMesh_IncrementalMesh
from OCP.StlAPI import StlAPI_Writer
from inspect_head_bolts import read_step
from match_head_recesses import mesh, ROOT

SOURCE = (HARDWARE_CAD / 'HX-SHCS 0.086-56x0.125x0.125-N_HX-SHCS 0.086-56x0.125x0.125-N.step')
BACKUP = ARCHIVE_ROOT / 'head-fasteners-20261103'
MESH_NAME = 'Meshes/Fasteners/stainless_socket_head.step.stl'
MATERIAL = 'head_recess_stainless_steel'
PREFIX = 'recessed_stainless_bolt_'


def rotation(rpy):
    r, p, y = rpy
    cr, sr, cp, sp, cy, sy = math.cos(r), math.sin(r), math.cos(p), math.sin(p), math.cos(y), math.sin(y)
    return np.array([[cy*cp, cy*sp*sr-sy*cr, cy*sp*cr+sy*sr], [sy*cp, sy*sp*sr+cy*cr, sy*sp*cr-cy*sr], [-sp, cp*sr, cp*cr]])


def angles(matrix):
    pitch = math.asin(float(np.clip(-matrix[2,0], -1, 1)))
    if abs(math.cos(pitch)) > 1e-7:
        return [math.atan2(matrix[2,1], matrix[2,2]), pitch, math.atan2(matrix[1,0], matrix[0,0])]
    return [0, pitch, math.atan2(-matrix[0,1], matrix[1,1])]


def numbers(values):
    return ' '.join(f'{v:.12f}' for v in values)


def occupied(face, cache):
    # These meshes include their modeled screws, washers, nuts and pivots.
    if 'arm_hardware' in face['matches'][0][0]:
        return True
    origin, direction = np.array(face['outer']), np.array(face['outward'])
    depth = abs(face['v'][1] - face['v'][0])
    folder = (CONFIG_ROOT / 'Models/Robots/Johnny5Head' / face['matches'][0][0]).parent
    for file in folder.glob('*.stl'):
        if 'collision' in file.name: continue
        if file not in cache: cache[file] = mesh(file)
        points, normals = cache[file]
        delta = points-origin; along = delta @ direction
        radial = delta-along[:,None]*direction
        radius = np.linalg.norm(radial, axis=1)
        inside = (radius > face['radius']*.65) & (radius < face['radius']*.995) & (along > -depth-.05) & (along < .05)
        outer_surface = np.sum(normals*radial, axis=1) > .65*radius
        if np.count_nonzero(inside & outer_surface) >= 24: return True
    return False


if __name__ == '__main__':
    BACKUP.mkdir(parents=True, exist_ok=True)
    source_copy = BACKUP / SOURCE.name
    if not source_copy.exists(): shutil.copy2(SOURCE, source_copy)
    shape = read_step(SOURCE)
    BRepMesh_IncrementalMesh(shape, .04, False, .3, True).Perform()
    bolt_file = BACKUP / 'bolt-render.stl'
    writer = StlAPI_Writer(); writer.ASCIIMode = False
    if not writer.Write(shape, str(bolt_file)): raise RuntimeError('STEP tessellation failed')
    points, _ = mesh(bolt_file)
    top = float(points[:,0].min())
    assert abs(top) < .001 and abs(points[:,0].max()-5.3594) < .001 and abs(points[:,1].max()-1.778) < .01
    seats = json.loads((ROOT/'tools/head-bolt-seats.json').read_text())
    cache = {}; selected = []; existing = []
    for face in seats['accepted']:
        (existing if occupied(face, cache) else selected).append(face)
    print('Verified empty seats', len(selected), 'Existing hardware seats left intact', len(existing), flush=True)
    manifest = {'source': str(SOURCE), 'sourceSha256': hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
                'meshSha256': hashlib.sha256(bolt_file.read_bytes()).hexdigest(),
                'meshTopXmm': top, 'sourceHeadRadiusMm': 1.778, 'sourceHeadDepthMm': 2.1844,
                'emptySeats': len(selected), 'existingHardwareSeats': len(existing), 'models': {}}
    for name in ['Johnny5Head', 'Johnny5FullBody']:
        root = CONFIG_ROOT / 'Models/Robots' / name
        path = root/'johnny5_head.urdf'
        original = path.read_text(encoding='utf-8')
        backup_file = BACKUP/(name+'-before.urdf')
        if not backup_file.exists(): shutil.copy2(path, backup_file)
        doc = ET.fromstring(original)
        if any(v.get('name','').startswith(PREFIX) for v in doc.iter('visual')):
            raise RuntimeError('Fasteners already installed; refuse duplicate installation')
        additions = collections.defaultdict(list); rows = []
        for seat_index, face in enumerate(selected):
            source_mesh = face['matches'][0][0]
            axis = np.array(face['outward']); axis /= np.linalg.norm(axis)
            up = np.array([0.,0.,1.]) if abs(axis[2]) < .9 else np.array([0.,1.,0.])
            # The supplied STEP has its socket at X=0 facing -X; its shank
            # extends along +X. Map that negative-X head normal to outward.
            tangent = np.cross(up, -axis); tangent /= np.linalg.norm(tangent)
            bolt_rotation = np.column_stack((-axis, tangent, np.cross(-axis,tangent)))
            depth = abs(face['v'][1]-face['v'][0])
            radial_scale = 1. if 1.80 < face['radius'] < 2.20 else face['radius']*.9/1.778
            axial_scale = min(radial_scale, depth/2.1844)
            local_tip = np.array(face['outer'])+axis*top*axial_scale
            for link in doc.findall('link'):
                for host in list(link.findall('visual')):
                    geometry = host.find('geometry/mesh')
                    if geometry is None or geometry.get('filename','').replace('\\','/') != source_mesh: continue
                    origin = host.find('origin')
                    host_translation = np.fromstring(origin.get('xyz','0 0 0'), sep=' ')
                    host_rotation = rotation(np.fromstring(origin.get('rpy','0 0 0'), sep=' '))
                    tip = host_translation+host_rotation @ (local_tip/1000)
                    orientation = host_rotation @ bolt_rotation
                    identifier = f'{PREFIX}{len(rows)+1:03d}'
                    visual = ET.Element('visual', {'name': identifier})
                    ET.SubElement(visual,'origin', {'xyz':numbers(tip), 'rpy':numbers(angles(orientation))})
                    geom = ET.SubElement(visual,'geometry')
                    ET.SubElement(geom,'mesh', {'filename':MESH_NAME, 'scale':numbers([axial_scale/1000,radial_scale/1000,radial_scale/1000])})
                    ET.SubElement(visual,'material', {'name':MATERIAL})
                    ET.indent(visual, space='  ', level=2)
                    additions[link.get('name')].append('    '+ET.tostring(visual,encoding='unicode').rstrip()+'\n')
                    rows.append({'name':identifier,'hostVisual':host.get('name'),'link':link.get('name'),
                        'cadStep':face['step'],'cadCylinderIndex':face['face'], 'recessRadiusMm':face['radius'],
                        'recessDepthMm':depth, 'radialScale':radial_scale,'axialScale':axial_scale,
                        'rimLinkMetres':(host_translation+host_rotation@(np.array(face['outer'])/1000)).tolist(),
                        'outwardLink':(host_rotation@axis).tolist()})
        updated = original
        for link, fragments in additions.items():
            pattern = re.compile(r'(<link\s+name="'+re.escape(link)+r'"[^>]*>)(.*?)(  </link>)', re.S)
            updated, count = pattern.subn(lambda m:m[1]+m[2]+''.join(fragments)+m[3],updated)
            if count != 1: raise RuntimeError('Ambiguous link: '+link)
        material = f'  <material name="{MATERIAL}">\n    <color rgba="0.72 0.75 0.78 1" />\n  </material>\n'
        updated = re.sub(r'(<robot\b[^>]*>\s*)',lambda m:m[1]+material,updated,count=1)
        ET.fromstring(updated)
        destination = root/MESH_NAME; destination.parent.mkdir(parents=True,exist_ok=True)
        shutil.copy2(bolt_file,destination)
        path.write_text(updated,encoding='utf-8',newline='\n')
        manifest['models'][name] = rows
        print(name, len(rows), 'fasteners', collections.Counter(r['link'] for r in rows), flush=True)
    (BACKUP/'fastener-placement-manifest.json').write_text(json.dumps(manifest,indent=2))
    print('Manifest',BACKUP/'fastener-placement-manifest.json')
