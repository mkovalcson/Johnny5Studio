# VR pose labels and loading feedback

Side control banks now appear on the opposite viewer sides. Within each bank,
controls retain their anatomical height order. Larger labels extend from the
shoulders down to the track floor; the viewer-right bank has extra clearance.

Layout uses the scene's actual robot floor and height, rather than the placement
control anchor near the neck. The overhead area spans the shoulder width, uses
five cells per row, begins above the head, and places icons beside larger text.
Ganged, central body and X/Y/heading placement controls are all retained.

Pose selection immediately displays loading feedback while starting the editor
or waiting for control metadata. Label and dial artwork prepares on a background
worker, with preparation/opening messages until ready. Selecting an existing live
editor no longer repeats the attachment process. The first trigger press opens
the dial in front of the viewer instead of beyond the side bank. Dial textures
continue updating while values change during preparation.

Validation: Player builds without warnings; VR Editor builds with its existing
unused-field warnings. VR runtime/layout checks: 3,756 assertions passed, including
all 81 actual model controls, side-bank bounds, five-column rows, non-overlap,
icons, first-press dial selection, asynchronous preparation and changing values.
VR Editor integration checks passed. Viewer/editor pipe smoke check is recorded
in Johnny5VR/Diagnostics/editor-smoke-result.txt.

Final headset readability, selection comfort and placement require testing on
the gaming PC. No desktop editor behavior or user configuration was changed.
