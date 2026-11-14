# Controller artwork

Generated with the built-in image generation tool from the approved realistic Xbox preview.

- `xbox-face.png`: graphite controller face, transparent background; remove all UI labels, leader lines and highlights while preserving physical markings.
- `xbox-shoulders.png`: matching unfolded shoulder-edge view, transparent background; left trigger/bumper on the left and right trigger/bumper on the right, with triggers above bumpers.

ControllerDiagram.Xbox.cs draws interactive mapping labels, hit regions, leader anchors and live highlights separately. Both controller configuration and main-screen mappings share this renderer. Images are cached and frozen, not decoded per input update. Steam artwork is unchanged.

## Steam Controller

The new dual-stick controller layout was checked against Valve's current product page: https://store.steampowered.com/hardware/steamcontroller

Generated using the built-in image-generation tool, matching the graphite Xbox style:

- `steam-face.png`: dual symmetric sticks, D-pad and ABXY below, two lower square trackpads; transparent background, physical markings only.
- `steam-shoulders.png`: unfolded shoulder edge with left/right preserved, triggers above bumpers; transparent background.
- `steam-underside.png`: rear view with four distinct rear buttons and grip surfaces; transparent background.

ControllerDiagram.Steam.cs aligns interactive overlays to the generated artwork. Gyro/accelerometer controls remain in a separate section. Both MUX and No MUX shoulder modes are retained. These are readable illustrative assets, not dimensionally exact product photographs. Original generated PNGs are preserved without raster editing.
