"""Johnny 5 body v6 - posed like the side photograph, with the three-rod neck
linkage.  Everything here sits below the neck; the head, neck and every
existing link in johnny5_head.urdf are untouched.

Frame: X forward through the face, Y robot left, Z up, metres.

Posing (all angles off vertical, in the side view)
  * pelvis drum = hinge, coaxial with the track sprockets
  * waist column runs from the drum to the bellows, tilted BACK 21 deg
  * bellows = hinge; chest (with yoke, arms, side unit) tilted FORWARD 25 deg
  * all three neck rods tilted FORWARD 41.5 deg
  * the neck box stays level - it carries the head

The chain is built from the tracks up, then the whole body is translated so
the neck box lands exactly under the fixed neck base.  The ground moves with
the body, so every ground constraint still holds.

Because the chest tilts but the box stays level, the rods cannot form a
parallelogram.  The two driven rear rods keep their 84 mm length; the passive
front rod slides in its slot, so its length follows from where its line meets
the yoke, and the slot housing is placed at that point.
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

PARTS = []   # filled in at ASSEMBLE


def _add(name, xyz, rpy, geom, mat):
    GROUPS[GROUP].append((name, tuple(xyz), tuple(rpy), geom, mat))


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



# ------------------------------------------------------------- posing ---
GROUP = "upper"      # upper = chest group, column = waist column, lower = world-aligned
GROUPS = {"upper": [], "column": [], "lower": []}


def _rpy_to_m(r, p, y):
    cr, sr = math.cos(r), math.sin(r); cp, sp = math.cos(p), math.sin(p); cy, sy = math.cos(y), math.sin(y)
    return [[cy * cp, cy * sp * sr - sy * cr, cy * sp * cr + sy * sr],
            [sy * cp, sy * sp * sr + cy * cr, sy * sp * cr - cy * sr],
            [-sp, cp * sr, cp * cr]]


def _m_to_rpy(M):
    sp = -M[2][0]
    if abs(sp) > 0.999999:
        p = math.copysign(math.pi / 2, sp)
        return (0.0, p, math.atan2(-M[0][1], M[1][1]))
    return (math.atan2(M[2][1], M[2][2]), math.asin(sp), math.atan2(M[1][0], M[0][0]))


def _mm(A, B):
    return [[sum(A[i][k] * B[k][j] for k in range(3)) for j in range(3)] for i in range(3)]


def _ry(t):
    return [[math.cos(t), 0, math.sin(t)], [0, 1, 0], [-math.sin(t), 0, math.cos(t)]]


def pose_group(parts, pivot, angle, dest):
    """Rotate parts by `angle` about +Y through `pivot`, then move pivot to dest."""
    R = _ry(angle)
    out = []
    for n, c, rpy, g, m in parts:
        d = [c[i] - pivot[i] for i in range(3)]
        rc = [sum(R[i][k] * d[k] for k in range(3)) + dest[i] for i in range(3)]
        out.append((n, tuple(rc), _m_to_rpy(_mm(R, _rpy_to_m(*rpy))), g, m))
    return out


def rot_point(p, pivot, angle, dest):
    R = _ry(angle)
    d = [p[i] - pivot[i] for i in range(3)]
    return tuple(sum(R[i][k] * d[k] for k in range(3)) + dest[i] for i in range(3))

# ------------------------------------------------------------- constants ---
# Built in the upright v4 frame, then posed.
NECK_X = 0.123658
GROUND = -1.0928
CHEST_X = 0.0677

WAIST_BACK = math.radians(21.0)       # column tilt, top toward the rear
CHEST_FWD = math.radians(25.0)        # chest tilt, top toward the front
ROD_FWD = math.radians(41.5)          # neck rods, top toward the front

HINGE0 = (CHEST_X, 0.0, -0.226)       # bellows centre in the upright frame
DRUM = (0.000, 0.0, -0.800)           # sprocket axis, world-aligned lower body
COLUMN_LEN = math.hypot(HINGE0[0] - DRUM[0], HINGE0[2] - DRUM[2])
HINGE = (DRUM[0] - COLUMN_LEN * math.sin(WAIST_BACK), 0.0, DRUM[2] + COLUMN_LEN * math.cos(WAIST_BACK))

# neck linkage anchors in the upright frame
YOKE_TOP0 = 0.066 + 0.075 / 2         # 0.1035
REAR_X = NECK_X - 0.030
NECK_AXLE0 = (REAR_X, 0.0, YOKE_TOP0 + 0.0105)
REAR_ROD_LEN = 0.084

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

# ======================================================= BELLOWS HINGE
# Split bellows: upper half follows the chest, lower half follows the column,
# meeting at the hinge pin.
GROUP = "upper"
bellows("waist_bellows_upper", (CHEST_X, 0, HINGE0[2]), (CHEST_X, 0, -0.190), 0.066, 0.054, 3, "black")

# ========================================================= NECK LINKAGE (yoke side)
# Rear axle, brackets, drive - built on the upright yoke, posed with the chest.
for sd, sy in (("l", 1), ("r", -1)):
    cyl(f"neck_rear_eye_bot_{sd}", (REAR_X, sy * 0.057, NECK_AXLE0[2]), 0.0075, 0.008, "dark", axis="y")
    for bi, by in enumerate((0.047, 0.067)):
        box(f"neck_axle_bracket_{sd}_{bi}", (REAR_X, sy * by, (YOKE_TOP0 + NECK_AXLE0[2] + 0.010) / 2),
            (0.022, 0.005, NECK_AXLE0[2] + 0.010 - YOKE_TOP0), "gray")
    cyl(f"neck_axle_cap_{sd}", (REAR_X, sy * 0.0715, NECK_AXLE0[2]), 0.007, 0.004, "light", axis="y")
rod("neck_rear_axle", (REAR_X, -0.071, NECK_AXLE0[2]), (REAR_X, 0.071, NECK_AXLE0[2]), 0.0045, "light")
cyl("neck_drive_hub", (REAR_X, 0, NECK_AXLE0[2]), 0.014, 0.026, "dark", axis="y")
box("neck_drive_gearbox", (REAR_X - 0.030, 0, YOKE_TOP0 + 0.012), (0.034, 0.040, 0.024), "dark")

# --- solve the pose of the neck linkage -------------------------------------
# Rear axle after the chest is posed (before the final translation):
AXLE_P = rot_point(NECK_AXLE0, HINGE0, CHEST_FWD, HINGE)
ROD_U = (math.sin(ROD_FWD), 0.0, math.cos(ROD_FWD))
REAR_TOP_P = tuple(AXLE_P[i] + REAR_ROD_LEN * ROD_U[i] for i in range(3))
# Box relative to the rear top pivot (box level): pivot sits 30 mm aft of the
# neck axis and 15.5 mm below the box centre.
BOX_TOP, BOX_H, BOX_W = 0.1375, 0.028, 0.084
BOX_C = (NECK_X, 0.0, BOX_TOP - BOX_H / 2)
REAR_TOP_Z, FRONT_TOP_Z = 0.108, 0.104
FRONT_X = NECK_X + 0.028
box_c_posed = (REAR_TOP_P[0] + 0.030, 0.0, REAR_TOP_P[2] + (BOX_C[2] - REAR_TOP_Z))
# Final translation that puts the box under the fixed neck base
SHIFT = (BOX_C[0] - box_c_posed[0], 0.0, BOX_C[2] - box_c_posed[2])

# Front rod: from its pivot under the box, down along ROD_U, to where it meets
# the yoke top.  Work in the upright chest frame to find that point.
def _to_upright(p):
    q = (p[0] - SHIFT[0], p[1], p[2] - SHIFT[2])
    return rot_point(q, HINGE, -CHEST_FWD, HINGE0)
F_TOP = (FRONT_X, 0.0, FRONT_TOP_Z)
f0 = _to_upright(F_TOP)
f1 = _to_upright(tuple(F_TOP[i] - ROD_U[i] for i in range(3)))
fd = [f1[i] - f0[i] for i in range(3)]
HOUSING_H = 0.027
t_top = (f0[2] - (YOKE_TOP0 + HOUSING_H)) / -fd[2]           # where it enters the housing top
t_bot = (f0[2] - (YOKE_TOP0 + 0.004)) / -fd[2]              # rod ends just above the yoke
SLOT0 = tuple(f0[i] + fd[i] * (t_top + t_bot) / 2 for i in range(3))
FRONT_ROD_LEN = t_bot                                       # ROD_U is unit length

box("neck_rod_housing", (SLOT0[0] - 0.004, 0, YOKE_TOP0 + HOUSING_H / 2), (0.050, 0.070, HOUSING_H), "gray")
box("neck_rod_slot", (SLOT0[0] + 0.010, 0, YOKE_TOP0 + HOUSING_H / 2 + 0.001),
    (0.030, 0.014, HOUSING_H + 0.002), "shadow")

# =========================================================== WAIST COLUMN
# Built upright along +Z from the drum centre, then tilted back about the drum.
GROUP = "column"
L = COLUMN_LEN
cx, cz = DRUM[0], DRUM[2]
bellows("waist_bellows_lower", (cx, 0, cz + L - 0.036), (cx, 0, cz + L), 0.066, 0.054, 3, "black")
cyl("hinge_pin", (cx, 0, cz + L), 0.022, 0.150, "gray", axis="y")
for sd, sy in (("l", 1), ("r", -1)):
    cyl(f"hinge_pin_cap_{sd}", (cx, sy * 0.078, cz + L), 0.030, 0.008, "blue", axis="y")
    box(f"hinge_cheek_{sd}", (cx, sy * 0.070, cz + L - 0.030), (0.050, 0.008, 0.060), "dark")
cyl("waist_ring_top", (cx, 0, cz + L - 0.052), 0.080, 0.032, "gray")
col_top, col_bot = cz + L - 0.068, cz
cyl("waist_column", (cx, 0, (col_top + col_bot) / 2), 0.068, col_top - col_bot, "blue")
cyl("waist_band_upper", (cx, 0, col_top - 0.018), 0.071, 0.028, "gray")
cyl("waist_band_lower", (cx, 0, cz + 0.110), 0.071, 0.028, "gray")
box("waist_front_panel", (cx + 0.066, 0, col_top - 0.056), (0.010, 0.056, 0.050), "dark")
box("waist_front_chip", (cx + 0.072, 0, col_top - 0.056), (0.004, 0.036, 0.018), "gold")
for sd, sy in (("l", 1), ("r", -1)):
    box(f"waist_side_panel_{sd}", (cx + 0.008, sy * 0.068, col_top - 0.200), (0.070, 0.016, 0.200), "dark")
    for i in range(7):
        box(f"waist_side_chip_{sd}_{i}", (cx + 0.008, sy * 0.077, col_top - 0.120 - i * 0.027), (0.050, 0.004, 0.012),
            "gold" if i % 2 == 0 else "amber")
# hinge at the drum: the column's lower yoke wraps the drum
cyl("pelvis_hinge_collar", (cx, 0, cz), 0.091, 0.056, "dark", axis="y")
for sd, sy in (("l", 1), ("r", -1)):
    box(f"pelvis_hinge_cheek_{sd}", (cx, sy * 0.034, cz + 0.070), (0.110, 0.010, 0.100), "gray")

# ==================================================================== TRACKS
GROUP = "lower"
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
# Drive sprocket centre, placed at the point marked on the v3 render (read off
# the screenshot using the roller spacing and sprocket height as scale).
SPROCKET_X = 0.000
SPROCKET_Z = -0.800
TRACK_TOP = SPROCKET_Z + SPROCKET_R + BELT_T

CLEAT_PITCH = 0.042

cyl("pelvis_drum", (SPROCKET_X, 0, SPROCKET_Z), 0.085, 0.186, "gray", axis="y")
cyl("pelvis_drum_band", (SPROCKET_X, 0, SPROCKET_Z), 0.088, 0.030, "dark", axis="y")


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
# Twin trailing casters.  Moved straight back 0.182 m (the arrow on the v5
# render).  The original 62 mm wheel was made 50 % wider (93 mm) and split into
# two wheels of 46.5 mm with a gap of 25 % of their combined width (23.25 mm).
# The arm attaches low, centred on the back of the pelvis drum, and runs the
# full width between the tracks.
BELT_REAR_X = TRACK_X - AXLE_SPACING / 2 - (ROLLER_R + BELT_T)     # -0.250
CASTER_X = -0.340 - 0.182                                           # -0.522
CASTER_R = 0.078
CASTER_Z = GROUND + CASTER_R
WHEEL_W = 0.062 * 1.5 / 2                                           # 0.0465 each
WHEEL_GAP = 0.25 * (2 * WHEEL_W)                                    # 0.02325
WHEEL_Y = WHEEL_GAP / 2 + WHEEL_W / 2                               # 0.034875
ARM_W = 2 * (TRACK_Y - BELT_W / 2) - 0.004                          # 0.186, 2 mm clear each side
HEAD_Z = -0.822

arm_a = (DRUM[0] - 0.080, 0, DRUM[2])            # low on the back of the drum
arm_b = (CASTER_X, 0, HEAD_Z)
strut("caster_arm", arm_a, arm_b, 0.070, ARM_W, "gray")
strut("caster_arm_rib", (arm_a[0] - 0.020, 0, arm_a[2] + 0.040), (arm_b[0] + 0.030, 0, arm_b[2] + 0.040),
      0.018, ARM_W * 0.65, "dark")
box("caster_head", (CASTER_X, 0, HEAD_Z - 0.006), (0.100, ARM_W, 0.050), "dark")
bellows("caster_spring", (CASTER_X, 0, HEAD_Z - 0.031), (CASTER_X, 0, -0.920), 0.034, 0.027, 4, "black")
YOKE_W = 2 * (WHEEL_Y + WHEEL_W / 2) + 0.030
box("caster_yoke", (CASTER_X, 0, -0.935), (0.085, YOKE_W, 0.030), "dark")
for sd, fy in (("l", WHEEL_Y + WHEEL_W / 2 + 0.008), ("c", 0.0), ("r", -(WHEEL_Y + WHEEL_W / 2 + 0.008))):
    box(f"caster_fork_{sd}", (CASTER_X, fy, -0.978), (0.050, 0.010, 0.085), "gray")
rod("caster_axle", (CASTER_X, -(YOKE_W / 2), CASTER_Z), (CASTER_X, YOKE_W / 2, CASTER_Z), 0.010, "light")
for sd, sy in (("l", 1), ("r", -1)):
    cyl(f"caster_wheel_{sd}", (CASTER_X, sy * WHEEL_Y, CASTER_Z), CASTER_R, WHEEL_W, "black", axis="y")
    cyl(f"caster_hub_{sd}", (CASTER_X, sy * WHEEL_Y, CASTER_Z), 0.034, WHEEL_W + 0.008, "gray", axis="y")

# ================================================================ ASSEMBLE
upper = pose_group(GROUPS["upper"], HINGE0, CHEST_FWD, HINGE)
column = pose_group(GROUPS["column"], DRUM, -WAIST_BACK, DRUM)
lower = list(GROUPS["lower"])
PARTS = [(n, (c[0] + SHIFT[0], c[1], c[2] + SHIFT[2]), rpy, g, m) for n, c, rpy, g, m in upper + column + lower]
GROUND_FINAL = GROUND + SHIFT[2]

# ============================================================ NECK LINKAGE (box side)
# Built directly in final world coordinates: the box is level under the head.
GROUP = "final"
GROUPS["final"] = PARTS
box("neck_box", BOX_C, (BOX_W, BOX_W, BOX_H), "gray")
box("neck_box_lip", (NECK_X, 0, BOX_TOP - 0.003), (BOX_W + 0.004, BOX_W + 0.004, 0.006), "light")

# front pivot and passive front rod
for sd, sy in (("l", 1), ("r", -1)):
    box(f"neck_front_clevis_{sd}", (FRONT_X, sy * 0.014, FRONT_TOP_Z + 0.001), (0.022, 0.004, 0.015), "gray")
    bellows(f"neck_front_spring_{sd}", (FRONT_X, sy * 0.017, FRONT_TOP_Z), (FRONT_X, sy * 0.037, FRONT_TOP_Z),
            0.0085, 0.0065, 4, "light")
rod("neck_front_pin", (FRONT_X, -0.040, FRONT_TOP_Z), (FRONT_X, 0.040, FRONT_TOP_Z), 0.0035, "light")
cyl("neck_front_knuckle", (FRONT_X, 0, FRONT_TOP_Z), 0.010, 0.022, "dark", axis="y")
sleeve_a = F_TOP
sleeve_b = tuple(F_TOP[i] - ROD_U[i] * 0.026 for i in range(3))
rod("neck_front_sleeve", sleeve_a, sleeve_b, 0.0115, "shadow")
front_bot = tuple(F_TOP[i] - ROD_U[i] * FRONT_ROD_LEN for i in range(3))
rod("neck_front_rod", sleeve_b, front_bot, 0.0038, "black")

# rear rods: from the posed axle to lugs on the level box
AXLE_W = tuple(AXLE_P[i] + SHIFT[i] for i in range(3))
for sd, sy in (("l", 1), ("r", -1)):
    top = (NECK_X - 0.030, sy * 0.052, REAR_TOP_Z)
    bot = (AXLE_W[0], sy * 0.057, AXLE_W[2])
    box(f"neck_rear_lug_{sd}", (NECK_X - 0.030, sy * 0.046, 0.1145), (0.024, 0.006, 0.034), "gray")
    rod(f"neck_rear_pin_{sd}", (top[0], sy * 0.043, top[2]), (top[0], sy * 0.065, top[2]), 0.004, "light")
    cyl(f"neck_rear_pin_head_{sd}", (top[0], sy * 0.064, top[2]), 0.0065, 0.004, "light", axis="y")
    cyl(f"neck_rear_eye_top_{sd}", top, 0.0075, 0.008, "dark", axis="y")
    rod(f"neck_rear_core_{sd}", top, bot, 0.003, "dark")
    d = [b_ - a_ for a_, b_ in zip(top, bot)]
    Ld = math.sqrt(sum(v * v for v in d))
    s0 = [a_ + v / Ld * 0.009 for a_, v in zip(top, d)]
    s1 = [b_ - v / Ld * 0.009 for b_, v in zip(bot, d)]
    bellows(f"neck_rear_spring_{sd}", s0, s1, 0.0068, 0.0050, 11, "black")

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
    lines = ["  <!-- ==== Johnny 5 body v6 - posed like the side photo, below the neck only ==== -->"]
    if head_urdf:
        lines += material_defs(head_urdf)
    lines += links_xml()
    lines.append("  <!-- ==== end body ==== -->")
    return "\n".join(lines)


if __name__ == "__main__":
    print(f"{len(PARTS)} parts")
    print(f"column length {COLUMN_LEN:.4f}; hinge (posed, before shift) {HINGE[0]:.4f}, {HINGE[2]:.4f}")
    print(f"body shift x {SHIFT[0]:+.4f}  z {SHIFT[2]:+.4f};  ground now z = {GROUND_FINAL:.4f}")
    print(f"rear rod length {REAR_ROD_LEN:.3f}; front rod length {FRONT_ROD_LEN:.4f}")
    print(f"caster x {CASTER_X + SHIFT[0]:.4f} (world), wheels {WHEEL_W*1000:.2f} mm each, gap {WHEEL_GAP*1000:.2f} mm")
    print("laser parts:", [p[0] for p in PARTS if "laser" in p[0]] or "none")
