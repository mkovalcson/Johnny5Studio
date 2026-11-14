"""Create portable configuration copies without changing the live files."""
import argparse
import json
import shutil
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('--source', required=True)
parser.add_argument('--output', required=True)
parser.add_argument('--clean', action='store_true')
parser.add_argument('--model-prefix', default='..')
args = parser.parse_args()
source, output = Path(args.source).resolve(), Path(args.output).resolve()
if source == output or source in output.parents or output in source.parents:
    raise SystemExit('Source and output configuration folders must be separate.')
if output.exists() and any(output.iterdir()):
    raise SystemExit('Use an empty output folder; existing files are never removed.')

required = {'servoconfig.json', 'servoconfig.default.json', 'urdfconfig.json',
            'speedcalibration.json', 'facetracking.json', 'focuscontrol.json',
            'meetingsettings.json', 'controllerrecordingsettings.json',
            'xboxcontrollermapping.json', 'steamcontrollermapping.json',
            'xboxcontrollermapping.selected.txt', 'steamcontrollermapping.selected.txt',
            'urdfmodelsettings.json'}

def model_path(value):
    normalized = value.replace('\\', '/')
    folded = normalized.lower()
    for category in ('robots', 'scenery'):
        marker = 'models/' + category + '/'
        if folded.startswith(marker):
            return normalized
        offset = folded.rfind('/' + marker)
        if offset >= 0:
            return normalized[offset + 1:]
    for folder in ('FullBodyModel', 'Models'):
        marker = '/' + folder.lower() + '/'
        if marker in folded:
            return args.model_prefix.rstrip('/') + '/' + folder + '/' + normalized[folded.rfind(marker) + len(marker):]
    return normalized

def portable(value, key=''):
    if isinstance(value, dict):
        result = {}
        for name, item in value.items():
            if name.lower() == 'selectedfile':
                continue  # Derived property; older files unnecessarily persisted it.
            name = model_path(name) if key.lower() == 'framing' else name
            result[name] = portable(item, name)
        return result
    if isinstance(value, list):
        return [portable(item) for item in value]
    if isinstance(value, str):
        if key.lower() in ('modelfile', 'robotmodelfile'):
            return model_path(value)
        if 'sylogist' in value.lower():
            return ''
        if value.lstrip().startswith(('{', '[')):
            try:
                embedded = json.loads(value)
            except json.JSONDecodeError:
                pass
            else:
                return json.dumps(portable(embedded), ensure_ascii=False)
    return value

def studio(presentation):
    if not isinstance(presentation, dict):
        return
    for key in list(presentation):
        if key.lower() == 'backgroundmode': presentation[key] = 'Studio'
        elif key.lower() == 'backgroundimage': presentation[key] = ''
        elif key.lower() == 'cleanundockedview': presentation[key] = False

count = 0
for file in source.rglob('*'):
    if not file.is_file():
        continue
    relative = file.relative_to(source)
    if 'sylogist' in str(relative).lower() or file.name.lower() == 'editorrecovery.json':
        continue
    if args.clean and not (len(relative.parts) == 1 and file.name.lower() in required
                           or relative.parts[0].lower() in ('tic', 'arduinoopensaintrgb', 'models')):
        continue
    target = output / relative
    target.parent.mkdir(parents=True, exist_ok=True)
    if file.suffix.lower() == '.json':
        original = json.loads(file.read_text(encoding='utf-8-sig'))
        data = portable(original)
        if file.name.lower() == 'urdfconfig.json':
            presentation = next((v for k, v in data.items() if k.lower() == 'presentation'), None)
            if args.clean or 'sylogist' in json.dumps(original).lower(): studio(presentation)
        if file.name.lower() == 'urdfmodelsettings.json':
            if args.clean:
                default_model = 'Models/Robots/Johnny5FullBody/johnny5_head.urdf'
                data = {'ActiveSceneFile': '', 'ModelFile': default_model,
                        'Framing': {default_model: {
                            'HorizontalPercent': 0, 'VerticalPercent': 0, 'SpinDegrees': 0,
                            'RotateDegrees': 0, 'Yaw': 0, 'Pitch': 0, 'Distance': 1.15}}}
        if args.clean and file.name.lower() == 'meetingsettings.json':
            data['Microphone'] = None
            data['OverrideBackground'] = False
            data['Image'] = ''
            studio(data.get('Motion'))
        target.write_text(json.dumps(data, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
    else:
        shutil.copy2(file, target)
    count += 1

assert not any('sylogist' in str(file).lower() for file in output.rglob('*'))
if args.clean:
    assert not any(file.is_dir() and file.name.lower().startswith('library') for file in output.iterdir())
print(f'Prepared {count} portable configuration files ({"clean repository" if args.clean else "deployment with libraries"}); live files untouched.')
