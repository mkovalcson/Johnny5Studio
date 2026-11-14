"""Locations relative to this script, independent of the caller's directory."""
from pathlib import Path
SOLUTION_ROOT = Path(__file__).resolve().parents[1]
STUDIO_ROOT = SOLUTION_ROOT.parent
CONFIG_ROOT = STUDIO_ROOT / 'Config'
if not CONFIG_ROOT.is_dir():
    CONFIG_ROOT = SOLUTION_ROOT / 'Config'
CAD_ROOT = STUDIO_ROOT / 'CAD'
HEAD_CAD = CAD_ROOT / 'Robots/Johnny5/Head/SourceCAD'
if not HEAD_CAD.is_dir():
    HEAD_CAD = SOLUTION_ROOT / 'ServoAnimator/Models/SourceCAD'
HARDWARE_CAD = CAD_ROOT / 'Robots/Johnny5/Hardware'

PREVIEWS_ROOT = SOLUTION_ROOT / 'DevelopmentArtifacts/Previews'
def preview_directory(category):
    if Path(category).name != category or not category.endswith('-previews'):
        raise ValueError('Expected a preview category folder')
    path = PREVIEWS_ROOT / category
    path.mkdir(parents=True, exist_ok=True)
    return path

ARCHIVE_ROOT = STUDIO_ROOT / 'Archive/DesktopChanges'
DOCUMENTATION_ROOT = STUDIO_ROOT / 'Documentation/Desktop'
