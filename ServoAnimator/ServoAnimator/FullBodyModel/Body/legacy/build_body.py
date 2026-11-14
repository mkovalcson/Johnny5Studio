"""Build the full Johnny 5 body (primitive geometry) for the head URDF.

Upper body is the v3 torso, unchanged and still aligned to the neck base plate.
The lower body, tracks and hands were derived from the front-view photograph:
the photo was scaled so its chest height matches this model's chest (0.215 m),
giving 1.1944 mm per photo pixel, and every landmark below was measured from
that image.  Track bottom lands at z = -1.093, so the robot stands ~1.47 m from
tracks to the top of the head.

Frame: X forward through the face, Y robot left, Z up, metres.
"""

import build_torso as bt

NX, NY, NZ = bt.NX, bt.NY, bt.NZ
TORSO_DX, TORSO_DY, TORSO_DZ = bt.TORSO_DX, bt.TORSO_DY, bt.TORSO_DZ
HEAD_MATERIALS = dict(bt.HEAD_MATERIALS)
HEAD_MATERIALS.update({
    "track_rubber": "black",
    "track_plate":  "cad_b3b3b3",
    "hub":          "step_steel_satin",
})

# World Z of the neck-relative origin used by the parts tables
ZBASE = NZ + TORSO_DZ          # 0.148548
XBASE = NX + TORSO_DX          # 0.135658


def at(world_x, world_y, world_z):
    """World coordinates -> the neck-relative offsets the parts table uses."""
    return (world_x - XBASE, world_y - NY - TORSO_DY, world_z - ZBASE)


# Measured landmarks (world Z, metres)
Z_BELLOWS_BOTTOM = -0.2746
Z_WAIST_BOTTOM   = -0.5255
Z_HIP_BOTTOM     = -0.6091
Z_TRACK_BOTTOM   = -1.0928

BODY_X = 0.0677          # body column, same as the chest centre
TRACK_Y = 0.1950         # track centre offset from the spine
TRACK_X = 0.0900         # track centre, forward of the spine

LOWER_PARTS = [
    # --- waist column -------------------------------------------------------
    ("waist_column", at(BODY_X, 0, -0.400), (0, 0, 0),
     ("cylinder", 0.070, 0.250), "panel_blue"),
    ("waist_column_front", at(BODY_X + 0.062, 0, -0.400), (0, 0, 0),
     ("box", (0.020, 0.105, 0.230)), "body_gray"),
    ("waist_panel_upper", at(BODY_X + 0.072, 0, -0.318), (0, 0, 0),
     ("box", (0.008, 0.090, 0.048)), "cad_404040_slot"),
    ("waist_panel_chips", at(BODY_X + 0.074, 0, -0.372), (0, 0, 0),
     ("box", (0.006, 0.075, 0.028)), "chip_amber"),
    ("waist_collar", at(BODY_X, 0, -0.520), (0, 0, 0),
     ("cylinder", 0.082, 0.040), "body_gray"),

    # --- hip / drive housing ------------------------------------------------
    ("hip_block", at(BODY_X + 0.010, 0, -0.568), (0, 0, 0),
     ("box", (0.230, 0.200, 0.090)), "body_gray"),
    ("hip_face", at(BODY_X + 0.120, 0, -0.568), (0, 0, 0),
     ("box", (0.016, 0.150, 0.070)), "body_shadow"),
    ("hip_face_chips", at(BODY_X + 0.130, 0, -0.568), (0, 0, 0),
     ("box", (0.006, 0.120, 0.030)), "chip_amber"),
    ("hip_skirt", at(BODY_X + 0.010, 0, -0.630), (0, 0, 0),
     ("box", (0.260, 0.240, 0.060)), "body_gray_dark"),

    # --- leg uprights to the tracks ----------------------------------------
    ("leg_upright_left", at(BODY_X - 0.005, 0.125, -0.725), (-0.16, 0, 0),
     ("box", (0.140, 0.090, 0.230)), "body_gray"),
    ("leg_upright_right", at(BODY_X - 0.005, -0.125, -0.725), (0.16, 0, 0),
     ("box", (0.140, 0.090, 0.230)), "body_gray"),
    ("leg_knee_left", at(BODY_X - 0.005, 0.170, -0.870), (0, 0, 0),
     ("box", (0.130, 0.110, 0.130)), "body_gray_dark"),
    ("leg_knee_right", at(BODY_X - 0.005, -0.170, -0.870), (0, 0, 0),
     ("box", (0.130, 0.110, 0.130)), "body_gray_dark"),
]

# --- tracks ----------------------------------------------------------------
# Level track units: a thin belt wraps both rollers and rests on the ground, so
# each roller clears the ground by exactly the belt thickness.  Belt width
# equals roller width.  The belt's wrapped ends are modelled as cylinders of
# radius ROLLER_R + BELT_T; the drive hubs sit outboard of the belt so they stay
# visible.
BELT_T = 0.012               # belt thickness
BELT_W = 0.200               # belt width == roller width
ROLLER_R = 0.086             # roller radius
AXLE_SPACING = 0.484         # front axle to rear axle
TRACK_Y = 0.1950
TRACK_X = 0.0900

AXLE_Z = Z_TRACK_BOTTOM + BELT_T + ROLLER_R     # rollers clear the ground by BELT_T
BELT_RUN_Z = ROLLER_R + BELT_T / 2              # belt run offset from the axle line

for side, sy in (("left", 1.0), ("right", -1.0)):
    y = TRACK_Y * sy
    LOWER_PARTS += [
        # frame inside the belt
        (f"track_chassis_{side}", at(TRACK_X, y, AXLE_Z), (0, 0, 0),
         ("box", (AXLE_SPACING, BELT_W * 0.62, ROLLER_R * 1.7)), "body_gray_dark"),
        (f"track_frame_top_{side}", at(TRACK_X, y, AXLE_Z + 0.052), (0, 0, 0),
         ("box", (AXLE_SPACING * 0.96, BELT_W * 0.70, 0.026)), "body_gray"),
        # belt: two straight runs plus a wrapped end at each roller
        (f"belt_bottom_{side}", at(TRACK_X, y, AXLE_Z - BELT_RUN_Z), (0, 0, 0),
         ("box", (AXLE_SPACING, BELT_W, BELT_T)), "track_plate"),
        (f"belt_top_{side}", at(TRACK_X, y, AXLE_Z + BELT_RUN_Z), (0, 0, 0),
         ("box", (AXLE_SPACING, BELT_W, BELT_T)), "track_plate"),
        (f"belt_wrap_front_{side}", at(TRACK_X + AXLE_SPACING / 2, y, AXLE_Z), (1.5708, 0, 0),
         ("cylinder", ROLLER_R + BELT_T, BELT_W), "track_plate"),
        (f"belt_wrap_rear_{side}", at(TRACK_X - AXLE_SPACING / 2, y, AXLE_Z), (1.5708, 0, 0),
         ("cylinder", ROLLER_R + BELT_T, BELT_W), "track_plate"),
        # rollers inside the wrap
        (f"track_roller_front_{side}", at(TRACK_X + AXLE_SPACING / 2, y, AXLE_Z), (1.5708, 0, 0),
         ("cylinder", ROLLER_R, BELT_W * 0.96), "track_rubber"),
        (f"track_roller_rear_{side}", at(TRACK_X - AXLE_SPACING / 2, y, AXLE_Z), (1.5708, 0, 0),
         ("cylinder", ROLLER_R, BELT_W * 0.96), "track_rubber"),
        # hubs, outboard of the belt so they read from the side
        (f"track_hub_front_{side}", at(TRACK_X + AXLE_SPACING / 2, y + (BELT_W / 2 + 0.012) * sy, AXLE_Z),
         (1.5708, 0, 0), ("cylinder", 0.046, 0.024), "hub"),
        (f"track_hub_rear_{side}", at(TRACK_X - AXLE_SPACING / 2, y + (BELT_W / 2 + 0.012) * sy, AXLE_Z),
         (1.5708, 0, 0), ("cylinder", 0.046, 0.024), "hub"),
        (f"track_idler_mid_{side}", at(TRACK_X, y + (BELT_W / 2 + 0.010) * sy, AXLE_Z - 0.030),
         (1.5708, 0, 0), ("cylinder", 0.034, 0.020), "hub"),
    ]

# --- rear caster on a beefy trailing arm off the back of the hip skirt -------
# Arm runs from the hip skirt's rear face down and back to the caster pivot,
# clear behind the belts.
HIP_BACK_X = BODY_X + 0.010 - 0.130          # rear face of hip_skirt
BELT_REAR_X = TRACK_X - AXLE_SPACING / 2 - (ROLLER_R + BELT_T)   # rearmost belt point
CASTER_X = BELT_REAR_X - 0.090               # behind the treads
CASTER_R = 0.078
CASTER_Z = Z_TRACK_BOTTOM + CASTER_R         # wheel rests on the ground

import math as _math
_ax, _az = HIP_BACK_X - 0.020, -0.660        # arm start, just behind/below the skirt
_bx, _bz = CASTER_X, CASTER_Z + 0.105        # arm end, above the caster pivot
_dx, _dz = _bx - _ax, _bz - _az
_len = _math.hypot(_dx, _dz)
_pitch = _math.atan2(-_dz, _dx)              # local +X maps to (cos, -sin)

LOWER_PARTS += [
    ("caster_arm_mount", at(HIP_BACK_X - 0.012, 0, -0.645), (0, 0, 0),
     ("box", (0.060, 0.130, 0.080)), "body_gray_dark"),
    ("caster_arm", at((_ax + _bx) / 2, 0, (_az + _bz) / 2), (0, _pitch, 0),
     ("box", (_len, 0.085, 0.080)), "body_gray"),
    ("caster_arm_rib", at((_ax + _bx) / 2, 0, (_az + _bz) / 2 + 0.048), (0, _pitch, 0),
     ("box", (_len * 0.88, 0.055, 0.022)), "body_gray_dark"),
    ("caster_yoke", at(CASTER_X, 0, CASTER_Z + 0.075), (0, 0, 0),
     ("box", (0.080, 0.105, 0.095)), "body_gray_dark"),
    ("caster_wheel", at(CASTER_X, 0, CASTER_Z), (1.5708, 0, 0),
     ("cylinder", CASTER_R, 0.062), "track_rubber"),
    ("caster_hub", at(CASTER_X, 0, CASTER_Z), (1.5708, 0, 0),
     ("cylinder", 0.032, 0.070), "hub"),
]

# --- hands (three-finger grippers on the existing forearms) ----------------
HAND_PARTS = []
for side, sy in (("left", 1.0), ("right", -1.0)):
    y = 0.252 * sy
    HAND_PARTS += [
        (f"wrist_{side}", at(0.212, y, -0.318), (0, -0.85, 0),
         ("box", (0.070, 0.070, 0.060)), "body_gray_dark"),
        (f"palm_{side}", at(0.252, y, -0.340), (0, -0.85, 0),
         ("box", (0.080, 0.075, 0.035)), "body_gray"),
        (f"finger_upper_{side}", at(0.300, y + 0.022 * sy, -0.330), (0, -0.55, 0),
         ("box", (0.090, 0.026, 0.020)), "panel_blue"),
        (f"finger_lower_{side}", at(0.300, y - 0.022 * sy, -0.352), (0, -0.55, 0),
         ("box", (0.090, 0.026, 0.020)), "panel_blue"),
        (f"finger_thumb_{side}", at(0.292, y, -0.372), (0, -0.30, 0),
         ("box", (0.080, 0.024, 0.018)), "panel_blue"),
    ]

PARTS = list(bt.PARTS) + LOWER_PARTS + HAND_PARTS

# one extra palette entry used above
HEAD_MATERIALS["cad_404040_slot"] = "cad_404040"


def build_links():
    out = []
    for name, xyz, rpy, geom, mat in PARTS:
        x = NX + xyz[0] + TORSO_DX
        y = NY + xyz[1] + TORSO_DY
        z = NZ + xyz[2] + TORSO_DZ
        out.append(f'  <link name="{name}_link">')
        out.append(f'    <visual name="{name}_visual">')
        out.append(f'      <origin xyz="0 0 0" rpy="{bt.fmt(rpy[0])} {bt.fmt(rpy[1])} {bt.fmt(rpy[2])}" />')
        out.append("      <geometry>")
        out.append(bt.geometry_xml(geom, 8))
        out.append("      </geometry>")
        out.append(f'      <material name="{HEAD_MATERIALS[mat]}" />')
        out.append("    </visual>")
        out.append("  </link>")
        out.append(f'  <joint name="{name}_fixed" type="fixed">')
        out.append('    <parent link="base_link" />')
        out.append(f'    <child link="{name}_link" />')
        out.append(f'    <origin xyz="{bt.fmt(x)} {bt.fmt(y)} {bt.fmt(z)}" rpy="0 0 0" />')
        out.append("  </joint>")
    return out


def material_defs(head_urdf):
    import xml.etree.ElementTree as ET
    defined = {m.get("name"): m.find("color").get("rgba")
               for m in ET.parse(head_urdf).getroot().findall("material")}
    out = []
    for name in sorted(set(HEAD_MATERIALS.values())):
        out.append(f'  <material name="{name}">')
        out.append(f'    <color rgba="{defined[name]}" />')
        out.append("  </material>")
    return out


def body_block(head_urdf=None):
    lines = ["  <!-- ==== Johnny 5 full body v1 - primitive geometry, head palette ==== -->"]
    if head_urdf:
        lines += material_defs(head_urdf)
    lines += build_links()
    lines.append("  <!-- ==== end body ==== -->")
    return "\n".join(lines)


if __name__ == "__main__":
    print(f"{len(PARTS)} parts "
          f"({len(bt.PARTS)} torso + {len(LOWER_PARTS)} lower + {len(HAND_PARTS)} hands)")
