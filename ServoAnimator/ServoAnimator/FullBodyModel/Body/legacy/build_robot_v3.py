"""Johnny 5 body v3 - refined from the reference photographs.

Everything here sits below the neck.  The head, neck and every existing link in
johnny5_head.urdf are untouched; the body is appended as fixed links on
base_link.

Frame: X forward through the face, Y robot left, Z up, metres.  All positions
in this file are world (base_link) coordinates.

Fixed constraints carried over from earlier versions
  * Riser cylinder on the neck axis (x = 0.123658, y = 0) with its top face at
    z = 0.134048, touching the underside of the black neck base plate
    (measured from neck_static_black.stl at z = 0.134050).
  * Ground at z = -1.0928 (front-view photo scaled to the model's chest).
  * Front and rear track rollers level, clearing the ground by exactly the belt
    thickness; belt width equals roller width; belt underside on the ground.
  * Caster behind the treads on a beefy arm off the back of the hip.
  * Only materials already defined in the head model are referenced.
"""

import math

# ---------------------------------------------------------------- palette ---
# Every body material maps onto a material already present in the head URDF.
MAT = {
    "gray":    "step_steel_satin",   # #a0a0a0  main shell
    "light":   "cad_b3b3b3",         # #b3b3b3  tread plates, light parts
    "pale":    "cad_e9e9eb",         # #e8e8eb  highlights
    "dark":    "cad_404040",         # #3f3f3f  structure
    "shadow":  "cad_020202",         # #020202  recesses
    "black":   "black",              # #020203  bellows, hoses, rubber
    "blue":    "cad_97a3da",         # #96a2da  blue-grey panels
    "gold":    "cad_e7c863",         # #e6c762  connectors
    "amber":   "neck_gold",          # #d3a02d  indicators
    "pad":     "iris_blue",          # #2d7fe8  gripper pads
}

PARTS = []   # (name, (x, y, z), (roll, pitch, yaw), geometry, material_key)


def _add(name, xyz, rpy, geom, mat):
    PARTS.append((name, tuple(xyz), tuple(rpy), geom, mat))


def box(name, c, size, mat, rpy=(0, 0, 0)):
    _add(name, c, rpy, ("box", tuple(size)), mat)


def cyl(name, c, r, length, mat, axis="z"):
    rpy = {"z": (0, 0, 0), "y": (math.pi / 2, 0, 0), "x": (0, math.pi / 2, 0)}[axis]
    _add(name, c, rpy, ("cylinder", r, length), mat)


def sphere(name, c, r, mat):
    _add(name, c, (0, 0, 0), ("sphere", r), mat)


def _align_z(u):
    """rpy (yaw = 0) that turns local +Z onto unit vector u, keeping local X and Y
    close to world X and Y - right for struts that run mostly up/down or
    fore/aft."""
    ux, uy, uz = u
    roll = -math.asin(max(-1.0, min(1.0, uy)))
    pitch = math.atan2(ux, uz)
    return (roll, pitch, 0.0)


def strut(name, p1, p2, sx, sy, mat):
    """Box running from p1 to p2 (local Z), cross-section sx by sy."""
    d = [b - a for a, b in zip(p1, p2)]
    L = math.sqrt(sum(v * v for v in d))
    u = [v / L for v in d]
    c = [(a + b) / 2 for a, b in zip(p1, p2)]
    _add(name, c, _align_z(u), ("box", (sx, sy, L)), mat)


def rod(name, p1, p2, r, mat):
    d = [b - a for a, b in zip(p1, p2)]
    L = math.sqrt(sum(v * v for v in d))
    u = [v / L for v in d]
    c = [(a + b) / 2 for a, b in zip(p1, p2)]
    _add(name, c, _align_z(u), ("cylinder", r, L), mat)


def bellows(name, p1, p2, r_big, r_small, n, mat):
    """Corrugated tube: alternating discs from p1 to p2."""
    d = [b - a for a, b in zip(p1, p2)]
    L = math.sqrt(sum(v * v for v in d))
    u = [v / L for v in d]
    step = L / (2 * n)
    for i in range(2 * n):
        c = [a + u[k] * step * (i + 0.5) for k, a in enumerate(p1)]
        r = r_big if i % 2 == 0 else r_small
        _add(f"{name}_{i:02d}", c, _align_z(u), ("cylinder", r, step * 1.02), mat)


# ------------------------------------------------------------- constants ---
NECK_X, NECK_Z_BASE = 0.123658, 0.134050
GROUND = -1.0928
CHEST_X = 0.0677                 # chest / spine centre, just behind the head


# ================================================================= NECK MOUNT
cyl("torso_riser", (NECK_X, 0, 0.096548), 0.036, 0.075, "dark")          # top 0.134048
cyl("torso_riser_collar", (NECK_X, 0, 0.057), 0.050, 0.022, "gray")
box("neck_clevis", (NECK_X - 0.004, 0, 0.1095), (0.030, 0.092, 0.014), "gray")
box("neck_clevis_cheek_l", (NECK_X - 0.004, 0.040, 0.094), (0.030, 0.010, 0.030), "gray")
box("neck_clevis_cheek_r", (NECK_X - 0.004, -0.040, 0.094), (0.030, 0.010, 0.030), "gray")

# ============================================================ SHOULDER YOKE
# Flat-fronted yoke with two oval windows; round tube visible inside each.
box("yoke", (0.075, 0, 0.066), (0.120, 0.440, 0.075), "gray")
box("yoke_top_rail", (0.075, 0, 0.1065), (0.110, 0.400, 0.008), "light")
cyl("yoke_rear_tube", (0.012, 0, 0.066), 0.040, 0.400, "light", axis="y")
for side, sy in (("l", 1), ("r", -1)):
    box(f"yoke_window_{side}", (0.1325, sy * 0.105, 0.066), (0.008, 0.115, 0.040), "shadow")
    box(f"yoke_window_end_o_{side}", (0.1325, sy * 0.1625, 0.066), (0.008, 0.010, 0.030), "shadow")
    box(f"yoke_window_end_i_{side}", (0.1325, sy * 0.0475, 0.066), (0.008, 0.010, 0.030), "shadow")
    cyl(f"yoke_window_tube_{side}", (0.124, sy * 0.105, 0.066), 0.013, 0.110, "dark", axis="y")

# =================================================================== CHEST
box("chest_body", (CHEST_X, 0, -0.083), (0.170, 0.230, 0.215), "gray")
box("chest_top", (CHEST_X, 0, 0.019), (0.145, 0.190, 0.070), "dark")
box("chest_slope", (0.128, 0, 0.012), (0.075, 0.200, 0.030), "light", rpy=(0, -0.55, 0))
box("chest_back", (CHEST_X - 0.087, 0, -0.083), (0.012, 0.200, 0.185), "dark")
for side, sy in (("l", 1), ("r", -1)):
    box(f"chest_cheek_{side}", (CHEST_X, sy * 0.121, -0.078), (0.125, 0.012, 0.150), "blue")
    box(f"chest_taper_{side}", (CHEST_X + 0.005, sy * 0.096, -0.178), (0.150, 0.050, 0.060), "gray",
        rpy=(sy * 0.50, 0, 0))

# front panel: recess, two circuit boards, connector row
box("chest_recess", (0.1545, 0, -0.085), (0.012, 0.178, 0.160), "dark")
for side, sy in (("l", 1), ("r", -1)):
    box(f"chest_board_{side}", (0.1625, sy * 0.043, -0.052), (0.006, 0.070, 0.080), "shadow")
    chips = [((0.018, 0.020), (-0.012, -0.030), "gold"),
             ((0.016, 0.012), (0.018, -0.034), "amber"),
             ((0.020, 0.016), (-0.010, 0.000), "blue"),
             ((0.012, 0.024), (0.020, 0.004), "gold"),
             ((0.030, 0.010), (0.000, 0.026), "pale")]
    for i, ((w, h), (dy, dz), m) in enumerate(chips):
        box(f"chest_chip_{side}_{i}", (0.1665, sy * 0.043 + dy, -0.052 + dz), (0.004, w, h), m)
box("chest_connector_base", (0.1625, 0, -0.126), (0.006, 0.150, 0.036), "shadow")
for i in range(8):
    y = -0.063 + i * 0.018
    box(f"chest_connector_{i}", (0.1665, y, -0.126), (0.004, 0.011, 0.028), "gold")

# ============================================ SIDE UNIT + HOSE (robot right)
box("side_unit", (0.105, -0.150, -0.040), (0.085, 0.060, 0.100), "gray")
box("side_unit_face", (0.149, -0.150, -0.040), (0.006, 0.045, 0.080), "dark")
cyl("side_unit_knob", (0.160, -0.150, -0.020), 0.012, 0.020, "light", axis="x")
bellows("side_hose", (0.108, -0.150, -0.090), (0.085, -0.128, -0.186), 0.018, 0.013, 7, "black")

# ================================================ LASER (robot left shoulder)
# Upright on a corrugated column, as in the front view.  Positioned so that its
# nearest point is 0.249 m from the neck axis, outside the head shell's
# 0.236 m turning sweep.
LASER_X, LASER_Y = -0.080, 0.240
box("laser_mount", (LASER_X + 0.030, LASER_Y, 0.090), (0.085, 0.075, 0.045), "dark")
bellows("laser_column", (LASER_X, LASER_Y, 0.112), (LASER_X, LASER_Y, 0.232), 0.031, 0.025, 8, "black")
box("laser_collar", (LASER_X, LASER_Y, 0.2395), (0.090, 0.104, 0.016), "dark")
box("laser_body", (LASER_X, LASER_Y, 0.308), (0.085, 0.100, 0.122), "blue")
box("laser_band", (LASER_X, LASER_Y, 0.338), (0.089, 0.104, 0.012), "gray")
box("laser_window", (LASER_X, LASER_Y, 0.3715), (0.066, 0.080, 0.006), "amber")

# ==================================================================== ARMS
for side, sy in (("left", 1), ("right", -1)):
    S = (0.075, sy * 0.236, 0.066)            # shoulder hub
    E = (0.082, sy * 0.262, -0.172)           # elbow
    W = (0.218, sy * 0.262, -0.302)           # wrist
    cyl(f"shoulder_hub_{side}", S, 0.060, 0.090, "gray", axis="y")
    cyl(f"shoulder_cap_{side}", (S[0], sy * 0.287, S[2]), 0.046, 0.012, "blue", axis="y")
    cyl(f"shoulder_boss_{side}", (S[0], sy * 0.295, S[2]), 0.020, 0.008, "dark", axis="y")

    # upper arm: flat slotted link
    strut(f"upper_arm_{side}", (S[0], sy * 0.262, S[2] - 0.035), E, 0.070, 0.036, "gray")
    strut(f"upper_arm_slot_{side}", (S[0] + 0.001, sy * 0.281, S[2] - 0.070),
          (E[0] + 0.001, sy * 0.281, E[2] + 0.035), 0.022, 0.004, "shadow")
    rod(f"upper_arm_piston_{side}", (S[0] - 0.038, sy * 0.250, S[2] - 0.050),
        (E[0] - 0.032, sy * 0.250, E[2] + 0.015), 0.010, "dark")

    cyl(f"elbow_hub_{side}", E, 0.038, 0.074, "dark", axis="y")
    cyl(f"elbow_cap_{side}", (E[0], sy * 0.302, E[2]), 0.026, 0.008, "gray", axis="y")

    # forearm
    strut(f"forearm_{side}", E, W, 0.046, 0.042, "gray")
    rod(f"forearm_piston_{side}", (E[0] + 0.012, sy * 0.238, E[2] - 0.028),
        (W[0] - 0.020, sy * 0.238, W[2] - 0.018), 0.008, "dark")

    # wrist and gripper
    fx, fz = (W[0] - E[0]), (W[2] - E[2])
    fl = math.hypot(fx, fz); ux, uz = fx / fl, fz / fl
    wrist_end = (W[0] + ux * 0.040, W[1], W[2] + uz * 0.040)
    rod(f"wrist_{side}", W, wrist_end, 0.031, "light")
    G = (wrist_end[0] + 0.020, W[1], wrist_end[2] - 0.004)
    box(f"palm_{side}", G, (0.045, 0.066, 0.052), "dark")
    for fi, dy in ((0, 0.021), (1, -0.021)):
        base = (G[0] + 0.020, W[1] + dy, G[2] + 0.014)
        tip = (G[0] + 0.108, W[1] + dy, G[2] + 0.000)
        strut(f"finger_{side}_{fi}", base, tip, 0.020, 0.019, "gray")
        box(f"finger_tip_{side}_{fi}", (tip[0] + 0.004, tip[1], tip[2] - 0.010), (0.016, 0.019, 0.022), "gray")
        sphere(f"finger_pad_a_{side}_{fi}", (base[0] + 0.022, base[1], base[2] + 0.010), 0.012, "pad")
        sphere(f"finger_pad_b_{side}_{fi}", (base[0] + 0.062, base[1], base[2] + 0.004), 0.011, "pad")
    tb, tt = (G[0] + 0.016, W[1], G[2] - 0.022), (G[0] + 0.090, W[1], G[2] - 0.034)
    strut(f"thumb_{side}", tb, tt, 0.018, 0.020, "gray")
    sphere(f"thumb_pad_{side}", (tb[0] + 0.030, tb[1], tb[2] - 0.012), 0.011, "pad")

# ========================================================= BELLOWS + WAIST
bellows("waist_bellows", (CHEST_X, 0, -0.190), (CHEST_X, 0, -0.262), 0.066, 0.054, 5, "black")
cyl("waist_ring_top", (CHEST_X, 0, -0.278), 0.080, 0.032, "gray")

WAIST_TOP, WAIST_BOT = -0.294, -0.508
wc = (WAIST_TOP + WAIST_BOT) / 2
cyl("waist_column", (CHEST_X, 0, wc), 0.068, WAIST_TOP - WAIST_BOT, "blue")
cyl("waist_band_upper", (CHEST_X, 0, -0.312), 0.071, 0.028, "gray")
cyl("waist_band_lower", (CHEST_X, 0, -0.492), 0.071, 0.028, "gray")
box("waist_front_panel", (CHEST_X + 0.066, 0, -0.350), (0.010, 0.056, 0.050), "dark")
box("waist_front_chip", (CHEST_X + 0.072, 0, -0.350), (0.004, 0.036, 0.018), "gold")
for side, sy in (("l", 1), ("r", -1)):
    box(f"waist_side_panel_{side}", (CHEST_X + 0.008, sy * 0.068, -0.400), (0.070, 0.016, 0.160), "dark")
    for i, z in enumerate((-0.345, -0.372, -0.399, -0.426, -0.453)):
        box(f"waist_side_chip_{side}_{i}", (CHEST_X + 0.008, sy * 0.077, z), (0.050, 0.004, 0.012),
            "gold" if i % 2 == 0 else "amber")

# ==================================================================== PELVIS
cyl("hip_ring", (CHEST_X, 0, -0.522), 0.086, 0.030, "gray")
box("pelvis_upper", (0.075, 0, -0.572), (0.220, 0.186, 0.072), "gray")
cyl("pelvis_drum", (0.075, 0, -0.664), 0.090, 0.186, "gray", axis="y")
box("pelvis_front_panel", (0.158, 0, -0.636), (0.012, 0.120, 0.084), "blue", rpy=(0, 0.30, 0))
box("pelvis_front_chips", (0.166, 0, -0.620), (0.004, 0.080, 0.016), "gold", rpy=(0, 0.30, 0))

# ==================================================================== TRACKS
# Triangular track units: front and rear rollers low and level, a drive
# sprocket high near the hip.  The belt is a closed loop of straight runs plus
# a wrapped end at each roller.  Tread plates sit on the two upper runs only,
# so the belt underside stays exactly on the ground.
BELT_T = 0.012
BELT_W = 0.200                   # belt width == roller width
ROLLER_R = 0.086                 # front and rear rollers
SPROCKET_R = 0.075
TRACK_Y = 0.195
TRACK_X = 0.090
AXLE_SPACING = 0.484
AXLE_Z = GROUND + BELT_T + ROLLER_R                     # -0.9948
TRACK_TOP = -0.609                                      # measured from the photo
SPROCKET_X = TRACK_X - AXLE_SPACING / 2 + 0.63 * AXLE_SPACING
SPROCKET_Z = TRACK_TOP - BELT_T - SPROCKET_R

CLEAT_PITCH = 0.042


def _tangent(c1, r1, c2, r2, centroid):
    """Outer common tangent of two circles in the XZ plane, on the side away from
    centroid.  Returns the two tangent points and the outward normal."""
    dx, dz = c2[0] - c1[0], c2[1] - c1[1]
    L = math.hypot(dx, dz)
    ux, uz = dx / L, dz / L
    s = (r1 - r2) / L
    k = math.sqrt(max(0.0, 1 - s * s))
    best = None
    for sign in (1, -1):
        nx = s * ux + sign * k * (-uz)
        nz = s * uz + sign * k * ux
        p1 = (c1[0] + r1 * nx, c1[1] + r1 * nz)
        p2 = (c2[0] + r2 * nx, c2[1] + r2 * nz)
        mid = ((p1[0] + p2[0]) / 2, (p1[1] + p2[1]) / 2)
        dist = math.hypot(mid[0] - centroid[0], mid[1] - centroid[1])
        if best is None or dist > best[0]:
            best = (dist, p1, p2, (nx, nz))
    return best[1], best[2], best[3]


for side, sy in (("left", 1), ("right", -1)):
    y = sy * TRACK_Y
    front = (TRACK_X + AXLE_SPACING / 2, AXLE_Z)
    rear = (TRACK_X - AXLE_SPACING / 2, AXLE_Z)
    top = (SPROCKET_X, SPROCKET_Z)
    rollers = [("front", front, ROLLER_R), ("sprocket", top, SPROCKET_R), ("rear", rear, ROLLER_R)]
    centroid = ((front[0] + rear[0] + top[0]) / 3, (front[1] + rear[1] + top[1]) / 3)

    # rollers, belt wraps and outboard hubs
    for rname, (rx, rz), r in rollers:
        cyl(f"track_{rname}_roller_{side}", (rx, y, rz), r, BELT_W * 0.96, "dark", axis="y")
        cyl(f"belt_wrap_{rname}_{side}", (rx, y, rz), r + BELT_T, BELT_W, "black", axis="y")
        cyl(f"track_{rname}_hub_{side}", (rx, y + sy * (BELT_W / 2 + 0.012), rz), 0.046, 0.024, "gray", axis="y")
        cyl(f"track_{rname}_hub_cap_{side}", (rx, y + sy * (BELT_W / 2 + 0.026), rz), 0.022, 0.006, "dark", axis="y")

    # belt runs: bottom (rear->front), front upper (front->sprocket), rear upper (sprocket->rear)
    runs = [("bottom", rear, ROLLER_R, front, ROLLER_R, False),
            ("front_upper", front, ROLLER_R, top, SPROCKET_R, True),
            ("rear_upper", top, SPROCKET_R, rear, ROLLER_R, True)]
    rc = BELT_T / 2
    for rname, c1, r1, c2, r2, cleats in runs:
        p1, p2, (nx, nz) = _tangent(c1, r1 + rc, c2, r2 + rc, centroid)
        dx, dz = p2[0] - p1[0], p2[1] - p1[1]
        L = math.hypot(dx, dz)
        pitch = -math.atan2(dz, dx)
        mid = ((p1[0] + p2[0]) / 2, (p1[1] + p2[1]) / 2)
        box(f"belt_{rname}_{side}", (mid[0], y, mid[1]), (L, BELT_W, BELT_T), "black", rpy=(0, pitch, 0))
        if cleats:
            n = int(L / CLEAT_PITCH)
            for i in range(n):
                t = (i + 0.5) / n
                px, pz = p1[0] + dx * t, p1[1] + dz * t
                cx, cz = px + nx * (BELT_T / 2 + 0.005), pz + nz * (BELT_T / 2 + 0.005)
                box(f"tread_{rname}_{side}_{i:02d}", (cx, y, cz), (0.026, BELT_W * 0.94, 0.010),
                    "light", rpy=(0, pitch, 0))

    # side frames inside the belt loop (inboard and outboard)
    for fname, fy in (("in", y - sy * 0.086), ("out", y + sy * 0.086)):
        box(f"track_frame_low_{fname}_{side}", (TRACK_X, fy, AXLE_Z + 0.010),
            (AXLE_SPACING * 0.92, 0.012, 0.120), "gray")
        strut(f"track_frame_fore_{fname}_{side}", (front[0] - 0.050, fy, AXLE_Z + 0.030),
              (top[0], fy, top[1] - 0.020), 0.090, 0.012, "gray")
        strut(f"track_frame_aft_{fname}_{side}", (rear[0] + 0.060, fy, AXLE_Z + 0.030),
              (top[0] - 0.010, fy, top[1] - 0.020), 0.110, 0.012, "gray")
    box(f"track_frame_panel_{side}", (TRACK_X - 0.020, y + sy * 0.093, AXLE_Z + 0.050),
        (0.200, 0.004, 0.070), "blue")

    # axle housing from the pelvis drum out to the sprocket
    cyl(f"hip_axle_{side}", (SPROCKET_X, sy * 0.090, SPROCKET_Z), 0.048, 0.030, "dark", axis="y")

# ==================================================================== CASTER
# Trailing caster behind the treads on a beefy arm off the back of the pelvis,
# running up the centreline between the tracks.
BELT_REAR_X = TRACK_X - AXLE_SPACING / 2 - (ROLLER_R + BELT_T)     # -0.250
CASTER_X = BELT_REAR_X - 0.090                                      # -0.340
CASTER_R = 0.078
CASTER_Z = GROUND + CASTER_R

arm_a = (0.075 - 0.110 - 0.010, 0, -0.615)          # rear of pelvis
arm_b = (CASTER_X, 0, -0.822)
box("caster_arm_mount", (arm_a[0] + 0.012, 0, arm_a[2] + 0.005), (0.050, 0.130, 0.090), "dark")
strut("caster_arm", arm_a, arm_b, 0.080, 0.085, "gray")
strut("caster_arm_rib", (arm_a[0], 0, arm_a[2] + 0.046), (arm_b[0] + 0.010, 0, arm_b[2] + 0.046),
      0.020, 0.055, "dark")
box("caster_head", (CASTER_X, 0, -0.828), (0.080, 0.100, 0.040), "dark")
bellows("caster_spring", (CASTER_X, 0, -0.848), (CASTER_X, 0, -0.920), 0.032, 0.025, 4, "black")
box("caster_yoke", (CASTER_X, 0, -0.935), (0.080, 0.105, 0.030), "dark")
for side, sy in (("l", 1), ("r", -1)):
    box(f"caster_fork_{side}", (CASTER_X, sy * 0.045, -0.978), (0.050, 0.010, 0.085), "gray")
cyl("caster_wheel", (CASTER_X, 0, CASTER_Z), CASTER_R, 0.062, "black", axis="y")
cyl("caster_hub", (CASTER_X, 0, CASTER_Z), 0.034, 0.072, "gray", axis="y")


# ================================================================ URDF out
def _fmt(v):
    return f"{v:.6f}"


def _geom(g):
    if g[0] == "box":
        return f'<box size="{_fmt(g[1][0])} {_fmt(g[1][1])} {_fmt(g[1][2])}" />'
    if g[0] == "cylinder":
        return f'<cylinder radius="{_fmt(g[1])}" length="{_fmt(g[2])}" />'
    return f'<sphere radius="{_fmt(g[1])}" />'


def links_xml():
    out = []
    for name, (x, y, z), (r, p, yw), g, m in PARTS:
        out += [f'  <link name="body_{name}_link">',
                f'    <visual name="body_{name}_visual">',
                f'      <origin xyz="0 0 0" rpy="{_fmt(r)} {_fmt(p)} {_fmt(yw)}" />',
                f'      <geometry>',
                f'        {_geom(g)}',
                f'      </geometry>',
                f'      <material name="{MAT[m]}" />',
                f'    </visual>',
                f'  </link>',
                f'  <joint name="body_{name}_fixed" type="fixed">',
                f'    <parent link="base_link" />',
                f'    <child link="body_{name}_link" />',
                f'    <origin xyz="{_fmt(x)} {_fmt(y)} {_fmt(z)}" rpy="0 0 0" />',
                f'  </joint>']
    return out


def material_defs(head_urdf):
    import xml.etree.ElementTree as ET
    defined = {m.get("name"): m.find("color").get("rgba")
               for m in ET.parse(head_urdf).getroot().findall("material")}
    return [line for name in sorted(set(MAT.values()))
            for line in (f'  <material name="{name}">',
                         f'    <color rgba="{defined[name]}" />',
                         '  </material>')]


def body_block(head_urdf=None):
    lines = ["  <!-- ==== Johnny 5 body v3 - refined from photographs, below the neck only ==== -->"]
    if head_urdf:
        lines += material_defs(head_urdf)
    lines += links_xml()
    lines.append("  <!-- ==== end body ==== -->")
    return "\n".join(lines)


if __name__ == "__main__":
    print(f"{len(PARTS)} parts")
    print(f"riser top z = {0.096548 + 0.075 / 2:.6f}")
    print(f"roller axle z = {AXLE_Z:.4f}, roller bottom = {AXLE_Z - ROLLER_R:.4f} "
          f"(clearance {AXLE_Z - ROLLER_R - GROUND:.4f})")
    print(f"sprocket ({SPROCKET_X:.4f}, {SPROCKET_Z:.4f}), track top {SPROCKET_Z + SPROCKET_R + BELT_T:.4f}")
    print(f"caster x = {CASTER_X:.4f}, wheel bottom = {CASTER_Z - CASTER_R:.4f}")
