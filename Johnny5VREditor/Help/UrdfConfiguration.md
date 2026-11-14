# URDF Configuration and Calibration

URDF Configuration controls the visual/model-space calibration. It does not change physical Maestro PWM calibration and does not drive physical hardware by itself.

## Rotational calibration in degrees

Degree-based rotating bodies use a circular calibration control.

- The outlined circle represents the angular calibration reference.
- The **Zero** line is always horizontal and points to the right. Its on-screen direction does not rotate when the numeric Zero value changes.
- **Maximum** is shown above the Zero line in normal direction.
- **Minimum** is shown below the Zero line in normal direction.
- Checking **Reversed** swaps those displayed sides: Minimum appears above Zero and Maximum below Zero.
- Minimum and Maximum have draggable endpoint handles. Drag a handle around the circle to change that angular extent relative to Zero.
- The exact Minimum, Zero and Maximum degree values remain editable as text.
- Zero also retains an ordinary slider for fine adjustment.
- Servo Position Preview is displayed to the right of the Maximum-value row and moves only the URDF preview.

The circular lines visualize offsets from the calibrated Zero point. The numeric values remain the actual URDF angles.

Centered URDF controls continue to use the piecewise mapping:

```text
logical -100 -> Minimum Extent
logical    0 -> Zero Point
logical +100 -> Maximum Extent
```

This allows the physical Zero value to be off-center while the visual Zero reference remains horizontal.

## Linear calibration in millimetres

Millimetre-based motion uses one horizontal line with two draggable endpoint handles.

- **Minimum** has an editable value on the left.
- **Maximum** has an editable value on the right.
- Both endpoints can also be changed by dragging their handles on the same line.
- In normal direction, **Zero = Minimum**.
- When **Reversed** is checked, **Zero = Maximum** and positive logical travel runs back toward Minimum.
- Zero is therefore derived for linear motion rather than being a third independent calibration point.

The stored numeric extents remain ordered low-to-high. Reversed changes which endpoint acts as logical zero and the direction of travel; this preserves the existing URDF mapping and JSON format.

## Shared and individual ranges

Every physical URDF servo retains its own Direction and Zero behavior. Normal gangs share Minimum/Maximum extents and Servo Position test movement. The four flap Open/Close servos retain independent Minimum/Maximum calibration while their upper and lower pairs remain coordinated for test movement.

## Positive-only controls

Positive-only controls begin at their Zero point and travel toward the appropriate endpoint according to Direction. For linear positive-only controls, normal direction begins at Minimum and reversed direction begins at Maximum. The embedded URDF defaults include a 2 mm minimum/zero for both Eye Pop controls before reversal is applied.

## Zero Flaps

After selecting a timeline pose with the desired NoseBody and NoseBasket values, Zero Flaps calculates flap zero values intended to make the flap surfaces horizontal for the current nose pose.

## Audio LED Gain

Audio LED Gain ranges from `0.5x` to `2.0x`. It multiplies the audio amplitude before driving the orange lip lights and red/green side-mouth LEDs. `1.0x` preserves the normal response.

## Eye and Vent light intensity

**Eye Light Intensity** and **Vent Light Intensity** independently control only the rendered URDF NeoPixel brightness. Each ranges from `1.0x` to `20.0x`; `1.0x` preserves the calibrated lighting appearance. These multipliers do not change RGBCommand values sent to the Arduino. Higher values also scale WPF light attenuation so the rendered light can continue becoming brighter above RGB saturation, while the configured light range remains fixed to limit spill.

## Renderer and appearance

The separate section below Vent Light Intensity selects **WPF** or **Direct3D 11** and
provides independent appearance settings for each. Changes preview immediately. Switch
renderers to compare their saved-in-memory profiles without losing the slider values.

**Weathering / distress** is shared by both renderers:

While a finish is being prepared, its progress percentage appears in the main
editor's bottom-right status area as well as below the weathering selector.
It remains visible with the Movie Timeline collapsed and clears when preparation
finishes. Other hardware and control status messages remain visible.

**Robot finish** selects **Original colors** or **Gold Johnny**. Gold Johnny uses
a warm bronze-gold finish (#AB7936) for the body, including the eye tubes, support brackets,
gimbal frames, hands and track housings, while preserving the lenses, irises, front eye trim,
piston shafts, black neck/waist bellows and black track treads.
Mouth LEDs and their circuit board retain their original colors. Lights and screens keep working.
Weathering is paused in Gold Johnny; returning to Original colors restores the
remembered wear level. Both choices preview immediately and save with **Save Default**.

**Default accessory** selects **None** (the default), **Toolbox**, or **Laser**.
This is the starting state and the fallback before any accessory commands.
Toolbox Swap and Hide Laser commands still control the accessory during a movie
or sequence. An explicit Toolbox Swap shows the chosen accessory even when the
default is None, unless a Hide Laser command is in effect.

Workshop Used includes separated paint chips on exposed rims and flap tips,
subtle seam dirt, matte charcoal/navy paint and softer metal highlights. The
same approach extends to the body for both Light Wear and Workshop Used:
shoulder housings, chest, waist panels, hands/finger disks, track housings and
metal tread plates. Light Wear is restrained; Workshop Used adds stronger
contact scuffs, paint chips and metal/brass patina. Rubber bellows, hoses and
track pads stay black; electronics, lights, screens and polished shafts retain
their colors. The approved head finishes and Outdoor Aged treatment are unchanged.

- **0 — Clean:** restores the original model surfaces exactly (the default).
- **1 — Light wear:** small silver chips along black/blue painted edges and fine scratches; mild metal tarnish.
- **2 — Workshop used:** localized paint chips and scratches, subdued highlights and metal patina; refined head/neck treatment as described above.
- **3 — Outdoor aged:** stronger paint loss and scratching, localized reddish-brown corrosion at exposed edges, and darker metal/brass patina.

Edge wear follows exposed outer boundaries and convex corners. Recessed and
hidden edges are protected; surface scratches are concentrated near exposed
edges. Built-in shoulder, finger and finger-disk shapes receive wear as well as
imported meshes. Outdoor aged adds downward rust streaks at bolt sites.
Rubber does not chip to silver
or rust, and stainless fasteners do not rust. Lenses, irises, screens and lights
retain their original appearance. These are visual aging treatments, not a
simulation of the exact alloy used by every CAD part.

Changes preview automatically. A new wear level is prepared in the background;
the previous appearance stays visible and the window remains usable. A progress
message appears below the wear selector. Prepared levels are cached for fast
switching during this session. Clean and Gold Johnny apply immediately, and a
new selection cancels an older pending choice. **Save Default** saves the selected level;
**Back** restores the level present when this window opened. **Reset this
renderer** leaves the shared weathering level alone. Resetting configuration
defaults restores Clean. Old configuration files also use Clean.

The finish follows individual robot parts during motion and disassembly. Clear
lenses, irises, pupils, animated LEDs, screens and laser effects are protected.
Wear is generated in memory; original model files are not modified, and there
are no additional texture files to distribute. Switching to Clean restores the
original material and mesh references. Weathering preparation occurs when a
level is selected, with mapped geometry reused for animation and other levels.

| Control | Effect |
| --- | --- |
| Brightness | Overall rendered scene brightness. |
| Shadow fill / ambient light | Studio ambient lighting, including shadowed surfaces. |
| Key / directional lighting | Studio directional and point lighting. |
| Highlights | Bright-area strength; lower values soften intense highlights. |
| Gamma / midtones | Midtone brightness. |
| Contrast | Separation between light and dark areas. |
| Lens reflectivity | Eye lens sheen and highlights: 0× = Off, center 1× = the existing appearance, rightmost 2× = stronger reflections. Previews immediately and remembers a separate value for each renderer. The spherical lens shape and iris opening stay unchanged. |
| Anti-aliasing type | Direct3D 11: select MSAA for geometry edges or FXAA to smooth the finished 3D image. Each type remembers its own quality. Switching types previews immediately. |
| Anti-aliasing / edge smoothing | MSAA: Off, 2×, 4× or 8×, limited by hardware support. FXAA: Off, Low, Medium, High or Ultra; may soften fine detail. Only the selected type is active. WPF displays Auto and manages its own smoothing. |

Lighting and tone sliders initially use **1.00×**, preserving each renderer's original appearance. Anti-aliasing initially uses **MSAA, 4×**, the existing Direct3D 11 default. FXAA starts at **High** when first selected. Save Default stores the type and both quality settings. Try **8× MSAA** for finer geometry edges around eyes and small parts, or compare FXAA for fine image detail. This does not change model geometry or remove all shimmer from very fine details.
Highlights adjusts bright pixels rather than changing material specular properties.
Lighting and tone controls affect the 3D scene; background images, video and interface
overlays retain their own appearance. Eye/Vent intensity and Audio LED Gain remain separate.

**Reset this renderer** resets only the selected renderer's lighting, tone and lens reflectivity sliders to 1.00× and its anti-aliasing quality to the default.
**Save Default** saves both profiles and the selected renderer to `URDFconfig.json`.
That renderer is then used by URDF views, including reopened and undocked views.
**Back** restores both profiles and the renderer choice to their values when the window opened.
Direct3D 11 remains experimental and falls back to WPF if unavailable.

## Embedded defaults and JSON override

Baseline calibration is embedded in the URDF. If `URDFconfig.json` exists in the configuration folder it is layered on top as an override. Save Default writes that override file.

## Window position within the list

During one application session, URDF Configuration remembers its vertical scroll position when the window is closed and restores that position the next time the window is opened.

## Back

**Back** restores every URDF Configuration value to the in-memory values that were present when the configuration window was opened, including Min/Zero/Max calibration, Direction overrides, Audio LED Gain, Eye Light Intensity, Vent Light Intensity, renderer choice and both appearance profiles. It updates the URDF preview immediately but does not write `URDFconfig.json`; use **Save Default** if the restored values should be persisted.

## Background and framing

Background controls have moved to **Config > URDF Scene Setup**, on the **Background** tab. That window also offers named background/framing presets. Calibration stays in this window.

## Head and Body sections

Current Head configuration and Body expand/collapse independently. Body contains the 30 independent B_ controls for the full-body URDF: waist, chest, waist swivel, neck lift, shoulders, arms, wrists and fingers. Commands use −100 to +100 with 0 at the built pose; each row offers minimum/maximum travel in degrees, reversal, a URDF preview slider and Zero position. Save Default stores these settings; Back and Reset configuration defaults include them. Missing joints in a head-only model do not move.

B_NeckLiftLevel and B_NeckLiftFrontRod are automatic followers of B_NeckLift using the model's mimic metadata, shown below the independent controls. They have no independent timeline targets. See [URDF body controls](UrdfBody.md) for the complete list and default ranges. These controls are not physical hardware mappings and are excluded from face tracking.

## 3D URDF stage

In **URDF Scene Setup > Background**, choose **3D URDF** as the background type to place the robot in a room, with Robot Centric or Room Centric movement. **Robot Position and Camera** sets robot position, heading, floor, pan, zoom and view. Use a **Load Scene** timeline command and **Choose Scene…** to load a saved complete scene during a sequence. Older embedded Load Backdrop commands remain readable. See [3D URDF Backdrops and Stage Movement](UrdfBackdrops.md).
