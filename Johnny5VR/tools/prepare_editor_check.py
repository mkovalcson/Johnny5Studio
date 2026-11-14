from pathlib import Path
import shutil
import xml.etree.ElementTree as ET

root=Path(__file__).resolve().parents[1]
source=root.parent/'Config' if (root.parent/'Config').is_dir() else root.parent/'ServoAnimator'/'Config'
target=root/'packaging'/'editor-check'/'Config'
target.mkdir(parents=True,exist_ok=True)
for directory in ('Projects','Library','LibraryScenes','LibraryBackgrounds','Backdrops','UrdfBackgrounds'):
    if (source/directory).is_dir():
        shutil.copytree(source/directory,target/directory,dirs_exist_ok=True)
for name in ('URDFconfig.json','ServoConfig.json','UrdfModelSettings.json','SpeedCalibration.json'):
    shutil.copy2(source/name,target/name)
for urdf in list((source/'Models'/'Robots').rglob('*.urdf'))+list((source/'Models'/'Scenery').rglob('*.urdf')):
    if (urdf.parent/'Meshes'/'Body'/'Treads').is_dir():
        shutil.copytree(urdf.parent/'Meshes'/'Body'/'Treads',target/urdf.parent.relative_to(source)/'Meshes'/'Body'/'Treads',dirs_exist_ok=True)
    for file in {urdf}|{(urdf.parent/m.attrib['filename']).resolve() for m in ET.parse(urdf).findall('.//mesh')}:
        if file.is_file():
            dest=target/file.relative_to(source)
            dest.parent.mkdir(parents=True,exist_ok=True)
            shutil.copy2(file,dest)
print(target)
