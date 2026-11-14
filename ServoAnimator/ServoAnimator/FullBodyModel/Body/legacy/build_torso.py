"""Build a primitive-geometry torso for the Johnny 5 head URDF.

Coordinate system (matches johnny5_head.urdf):
  X forward through the face, Y robot left, Z up, metres.
  Neck disc (NeckTurn origin) sits at (0.123658, 0, 0.134448) from base_link.
"""

NX, NY, NZ = 0.123658, 0.0, 0.134448

# Whole-torso trim, applied to every part. Set so the riser cylinder's axis lands
# on the neck axis (x = NX, y = 0) and its top face touches the underside of the
# black neck base plate, measured from neck_static_black.stl at z = 0.13405.
TORSO_DX, TORSO_DY, TORSO_DZ = 0.012000, 0.0, 0.014100

# Torso parts reference materials that already exist in johnny5_head.urdf, so the
# torso shares the head's STEP-derived palette and defines no colours of its own.
# (Nothing ending in "_dynamic" is used - those are driven by the LED simulation.)
HEAD_MATERIALS = {
    "body_gray":      "step_steel_satin",   # #a0a0a0 main shell
    "body_light":     "cad_b3b3b3",         # #b3b3b3 shoulder tubes
    "body_gray_dark": "cad_404040",         # #3f3f3f structure / shadowed blocks
    "body_shadow":    "cad_020202",         # #020202 pivots, recesses
    "panel_blue":     "cad_97a3da",         # #96a2da the head's blue-grey panel colour
    "bellows_black":  "black",              # #020203 bellows
    "chip_amber":     "cad_e7c863",         # #e6c762 gold connector strip
    "chip_blue":      "iris_blue",          # #2d7fe8 indicator
    "chip_red":       "neck_gold",          # #d3a02d indicator
}

# name, parent, xyz (relative to neck disc), rpy, geometry, material
PARTS = [
    # --- spine / neck riser -------------------------------------------------
    ("torso_riser", (-0.012, 0, -0.052), (0, 0, 0),
     ("cylinder", 0.036, 0.075), "body_gray_dark"),
    ("torso_riser_collar", (-0.012, 0, -0.092), (0, 0, 0),
     ("cylinder", 0.050, 0.022), "body_gray"),

    # --- shoulder yoke ------------------------------------------------------
    ("shoulder_bar_rear", (-0.118, 0, -0.078), (1.5708, 0, 0),
     ("cylinder", 0.043, 0.440), "body_light"),
    ("shoulder_bar_front", (-0.008, 0, -0.090), (1.5708, 0, 0),
     ("cylinder", 0.032, 0.380), "body_light"),
    ("shoulder_web_left", (-0.065, 0.100, -0.083), (0, 0, 0),
     ("box", (0.140, 0.070, 0.072)), "body_gray_dark"),
    ("shoulder_web_right", (-0.065, -0.100, -0.083), (0, 0, 0),
     ("box", (0.140, 0.070, 0.072)), "body_gray_dark"),

    # --- chest --------------------------------------------------------------
    ("chest_body", (-0.068, 0, -0.232), (0, 0, 0),
     ("box", (0.170, 0.230, 0.215)), "body_gray"),
    ("chest_shoulders", (-0.068, 0, -0.130), (0, 0, 0),
     ("box", (0.145, 0.190, 0.070)), "body_gray_dark"),
    ("chest_face", (0.022, 0, -0.234), (0, 0, 0),
     ("box", (0.020, 0.165, 0.150)), "body_shadow"),
    ("chest_strip", (0.028, 0, -0.154), (0, 0, 0),
     ("box", (0.018, 0.145, 0.032)), "chip_amber"),
    ("chest_chip_left", (0.034, 0.043, -0.214), (0, 0, 0),
     ("box", (0.008, 0.050, 0.030)), "chip_blue"),
    ("chest_chip_right", (0.034, -0.043, -0.214), (0, 0, 0),
     ("box", (0.008, 0.050, 0.030)), "chip_red"),
    ("chest_vent", (0.034, 0, -0.274), (0, 0, 0),
     ("box", (0.008, 0.115, 0.045)), "body_gray_dark"),
    ("chest_side_left", (-0.068, 0.120, -0.228), (0, 0, 0),
     ("box", (0.125, 0.014, 0.150)), "panel_blue"),
    ("chest_side_right", (-0.068, -0.120, -0.228), (0, 0, 0),
     ("box", (0.125, 0.014, 0.150)), "panel_blue"),
    ("chest_back", (-0.150, 0, -0.232), (0, 0, 0),
     ("box", (0.020, 0.195, 0.180)), "body_gray_dark"),

    # --- waist bellows (mostly below frame) ---------------------------------
    ("waist_bellows", (-0.068, 0, -0.374), (0, 0, 0),
     ("cylinder", 0.062, 0.078), "bellows_black"),
    ("waist_drum", (-0.068, 0, -0.436), (0, 0, 0),
     ("cylinder", 0.080, 0.062), "body_gray"),

    # --- left arm -----------------------------------------------------------
    ("shoulder_pod_left", (-0.075, 0.222, -0.083), (0, 0, 0),
     ("box", (0.140, 0.105, 0.122)), "body_gray"),
    ("shoulder_plate_left", (-0.075, 0.280, -0.083), (0, 0, 0),
     ("box", (0.105, 0.010, 0.092)), "panel_blue"),
    ("shoulder_pivot_left", (-0.020, 0.222, -0.108), (1.5708, 0, 0),
     ("cylinder", 0.040, 0.100), "body_shadow"),
    ("upper_arm_left", (-0.062, 0.238, -0.232), (-0.10, 0, 0),
     ("cylinder", 0.037, 0.215), "body_gray"),
    ("elbow_left", (-0.062, 0.252, -0.338), (0, 0, 0),
     ("box", (0.092, 0.078, 0.078)), "body_gray_dark"),
    ("forearm_left", (0.005, 0.252, -0.398), (0, -0.85, 0),
     ("cylinder", 0.031, 0.185), "body_gray"),

    # --- right arm ----------------------------------------------------------
    ("shoulder_pod_right", (-0.075, -0.222, -0.083), (0, 0, 0),
     ("box", (0.140, 0.105, 0.122)), "body_gray"),
    ("shoulder_plate_right", (-0.075, -0.280, -0.083), (0, 0, 0),
     ("box", (0.105, 0.010, 0.092)), "panel_blue"),
    ("shoulder_pivot_right", (-0.020, -0.222, -0.108), (1.5708, 0, 0),
     ("cylinder", 0.040, 0.100), "body_shadow"),
    ("upper_arm_right", (-0.062, -0.238, -0.232), (0.10, 0, 0),
     ("cylinder", 0.037, 0.215), "body_gray"),
    ("elbow_right", (-0.062, -0.252, -0.338), (0, 0, 0),
     ("box", (0.092, 0.078, 0.078)), "body_gray_dark"),
    ("forearm_right", (0.005, -0.252, -0.398), (0, -0.85, 0),
     ("cylinder", 0.031, 0.185), "body_gray"),
]


def fmt(v):
    return f"{v:.6f}"


def geometry_xml(geom, indent):
    pad = " " * indent
    if geom[0] == "box":
        x, y, z = geom[1]
        return f'{pad}<box size="{fmt(x)} {fmt(y)} {fmt(z)}" />'
    r, l = geom[1], geom[2]
    return f'{pad}<cylinder radius="{fmt(r)}" length="{fmt(l)}" />'


def build_links():
    out = []
    for name, xyz, rpy, geom, mat in PARTS:
        x = NX + xyz[0] + TORSO_DX
        y = NY + xyz[1] + TORSO_DY
        z = NZ + xyz[2] + TORSO_DZ
        out.append(f'  <link name="{name}_link">')
        out.append(f'    <visual name="{name}_visual">')
        out.append(f'      <origin xyz="0 0 0" rpy="{fmt(rpy[0])} {fmt(rpy[1])} {fmt(rpy[2])}" />')
        out.append("      <geometry>")
        out.append(geometry_xml(geom, 8))
        out.append("      </geometry>")
        out.append(f'      <material name="{HEAD_MATERIALS[mat]}" />')
        out.append("    </visual>")
        out.append("  </link>")
        out.append(f'  <joint name="{name}_fixed" type="fixed">')
        out.append('    <parent link="base_link" />')
        out.append(f'    <child link="{name}_link" />')
        out.append(f'    <origin xyz="{fmt(x)} {fmt(y)} {fmt(z)}" rpy="0 0 0" />')
        out.append("  </joint>")
    return out


def head_material_defs(head_urdf):
    """Copy the definitions of the materials the torso uses out of the head URDF,
    for the standalone torso file which has no head content."""
    import xml.etree.ElementTree as ET
    root = ET.parse(head_urdf).getroot()
    defined = {m.get("name"): m.find("color").get("rgba") for m in root.findall("material")}
    out = []
    for name in sorted(set(HEAD_MATERIALS.values())):
        out.append(f'  <material name="{name}">')
        out.append(f'    <color rgba="{defined[name]}" />')
        out.append("  </material>")
    return out


def torso_block(head_urdf=None):
    """Torso XML. Pass head_urdf to also emit the material definitions (only needed
    for the standalone torso file; the merged file already has them)."""
    lines = ["  <!-- ==== Johnny 5 torso v2 - primitive geometry, head palette ==== -->"]
    if head_urdf:
        lines += head_material_defs(head_urdf)
    lines += build_links()
    lines.append("  <!-- ==== end torso ==== -->")
    return "\n".join(lines)


if __name__ == "__main__":
    print(torso_block())
