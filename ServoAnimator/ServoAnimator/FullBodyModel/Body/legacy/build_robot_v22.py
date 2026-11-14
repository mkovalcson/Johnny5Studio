"""Johnny 5 body v22 - posed like the side photograph, with the three-rod neck
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
    "tan":     "step_gold_polished", # #f4e4a7  resistors, connector pins
    "copper":  "cad_d89d8b",         # #d79d8b  copper pads
    "red":     "mouth_red",          # #db3333  small red parts (static; the LEDs use *_dynamic)
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


def bellows_taper(name, p1, p2, r1, r2, n, mat, groove=0.84):
    """Corrugated tube from p1 (outer radius r1) to p2 (outer radius r2)."""
    d = [b - a for a, b in zip(p1, p2)]
    L = math.sqrt(sum(v * v for v in d))
    u = [v / L for v in d]
    step = L / (2 * n)
    for i in range(2 * n):
        f = (i + 0.5) / (2 * n)
        c = [a + u[k] * step * (i + 0.5) for k, a in enumerate(p1)]
        r = r1 + (r2 - r1) * f
        _add(f"{name}_{i:02d}", c, _align_z(u), ("cylinder", r if i % 2 == 0 else r * groove, step * 1.02), mat)


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

# ------------------------------------------------------------ meshes ---
# Shapes primitives cannot express are written as binary STL into
# Meshes/Body (or $J5_MESH_DIR) and referenced like the head meshes.
import os, struct
MESH_DIR = os.environ.get("J5_MESH_DIR") or os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Meshes", "Body")


def _ear_clip(poly):
    idx = list(range(len(poly))); tris = []
    cross = lambda o, a, b: (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])
    def inside(p, a, b, c):
        return cross(a, b, p) > 0 and cross(b, c, p) > 0 and cross(c, a, p) > 0
    guard = 0
    while len(idx) > 3 and guard < 20000:
        guard += 1
        for k in range(len(idx)):
            i0, i1, i2 = idx[k - 1], idx[k], idx[(k + 1) % len(idx)]
            a, b, c = poly[i0], poly[i1], poly[i2]
            if cross(a, b, c) <= 1e-12:
                continue
            if any(inside(poly[j], a, b, c) for j in idx if j not in (i0, i1, i2)):
                continue
            tris.append((i0, i1, i2)); idx.pop(k); break
    tris.append(tuple(idx))
    return tris



def _write_stl(path, tris):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as f:
        f.write(b"Johnny 5 track shroud".ljust(80, b" "))
        f.write(struct.pack("<I", len(tris)))
        for a, b, c in tris:
            u = [b[i] - a[i] for i in range(3)]; v = [c[i] - a[i] for i in range(3)]
            n = [u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0]]
            l = math.sqrt(sum(x * x for x in n)) or 1.0
            f.write(struct.pack("<12fH", *(x / l for x in n), *a, *b, *c, 0))



def prism_x(profile_yz, x0, x1):
    """Triangles of a prism: counter-clockwise (y, z) profile extruded from x0 to x1."""
    faces = _ear_clip(profile_yz); P = profile_yz; tris = []
    for a, b, c in faces:
        tris.append(((x1, *P[a]), (x1, *P[b]), (x1, *P[c]))); tris.append(((x0, *P[a]), (x0, *P[c]), (x0, *P[b])))
    for i in range(len(P)):
        j = (i + 1) % len(P)
        tris.append(((x0, *P[i]), (x0, *P[j]), (x1, *P[j]))); tris.append(((x0, *P[i]), (x1, *P[j]), (x1, *P[i])))
    return tris


def frustum(bot, top):
    """Triangles of a solid between two axis-aligned rectangles (x0, x1, y0, y1, z)."""
    def ring(r):
        x0, x1, y0, y1, z = r
        return [(x0, y0, z), (x1, y0, z), (x1, y1, z), (x0, y1, z)]
    B, T = ring(bot), ring(top); tris = [(B[0], B[2], B[1]), (B[0], B[3], B[2]), (T[0], T[1], T[2]), (T[0], T[2], T[3])]
    for i in range(4):
        j = (i + 1) % 4
        tris += [(B[i], B[j], T[j]), (B[i], T[j], T[i])]
    return tris


def prism_y(poly_xz, y0, y1):
    """Polygon in the x-z plane (either winding) extruded from y0 to y1."""
    area = sum(poly_xz[i][0] * poly_xz[(i + 1) % len(poly_xz)][1] - poly_xz[(i + 1) % len(poly_xz)][0] * poly_xz[i][1]
               for i in range(len(poly_xz)))
    P = poly_xz if area > 0 else poly_xz[::-1]
    tris = []
    for a_, b_, c_ in _ear_clip(P):
        tris += [((P[a_][0], y1, P[a_][1]), (P[b_][0], y1, P[b_][1]), (P[c_][0], y1, P[c_][1])),
                 ((P[a_][0], y0, P[a_][1]), (P[c_][0], y0, P[c_][1]), (P[b_][0], y0, P[b_][1]))]
    for i in range(len(P)):
        j = (i + 1) % len(P)
        q = [(P[i][0], y0, P[i][1]), (P[j][0], y0, P[j][1]), (P[j][0], y1, P[j][1]), (P[i][0], y1, P[i][1])]
        tris += [(q[0], q[1], q[2]), (q[0], q[2], q[3])]
    return tris



def mesh_part(name, origin, tris, mat):
    """Write tris (relative to origin) as Meshes/Body/<name>.stl and add the part."""
    _write_stl(os.path.join(MESH_DIR, f"{name}.stl"), tris)
    _add(name, origin, (0, 0, 0), ("mesh", f"Meshes/Body/{name}.stl"), mat)


# ------------------------------------------------------------- constants ---
# Built in the upright v4 frame, then posed.
NECK_X = 0.123658
GROUND = -1.0928
CHEST_X = 0.0677

WAIST_BACK = math.radians(21.0)       # column tilt, top toward the rear
CHEST_FWD = math.radians(25.0)        # chest tilt, top toward the front
ROD_FWD = math.radians(41.5)          # neck rods, top toward the front

HINGE0 = (CHEST_X, 0.0, -0.226)       # bellows centre in the upright frame
# Pelvis drum = waist hinge.  Sized from the front photograph of the tracks:
# its top stays just below the tread top over the sprockets, so a larger
# radius puts its axis below the sprocket axis.
DRUM_R = 0.105
DRUM = (0.000, 0.0, (-0.800 + 0.075 + 0.012) - 0.002 - DRUM_R)   # sprocket top - 2 mm - radius
COLUMN_LEN = math.hypot(HINGE0[0] - 0.000, HINGE0[2] - (-0.800))  # column length unchanged since v6
HINGE = (DRUM[0] - COLUMN_LEN * math.sin(WAIST_BACK), 0.0, DRUM[2] + COLUMN_LEN * math.cos(WAIST_BACK))

# neck linkage anchors in the upright frame
YOKE_C = (0.075, 0.066)                # yoke cylinder axis (x, z), runs along Y
YOKE_R = 0.055                         # just inside the shoulder hubs (0.060)
def yoke_surface_z(x):
    """Top surface of the yoke cylinder at x (upright frame)."""
    return YOKE_C[1] + math.sqrt(max(0.0, YOKE_R ** 2 - (x - YOKE_C[0]) ** 2))
YOKE_TOP0 = yoke_surface_z(NECK_X - 0.030)  # surface under the rear axle
REAR_X = NECK_X - 0.030
NECK_AXLE0 = (REAR_X, 0.0, YOKE_TOP0 + 0.0105)
REAR_ROD_LEN = 0.084

# ============================================================ SHOULDER YOKE
# Cylindrical yoke running left to right between the shoulder seams, with two
# stadium-shaped windows (straight top and bottom, round ends) in its front,
# and the inner tube lying in each window.
cyl("yoke", (YOKE_C[0], 0, YOKE_C[1]), YOKE_R, 2 * (0.166 - 0.003), "gray", axis="y")   # ends at the shoulder seams
WIN_W, WIN_H, WIN_Y = 0.115, 0.040, 0.105
win_x = YOKE_C[0] + math.sqrt(YOKE_R ** 2 - (WIN_H / 2) ** 2)    # surface at the window's top/bottom edge
for side, sy in (("l", 1), ("r", -1)):
    box(f"yoke_window_{side}", (win_x + 0.001, sy * WIN_Y, YOKE_C[1]), (0.014, WIN_W - WIN_H, WIN_H), "shadow")
    for en, ey in (("o", WIN_Y + (WIN_W - WIN_H) / 2), ("i", WIN_Y - (WIN_W - WIN_H) / 2)):
        cyl(f"yoke_window_end_{en}_{side}", (win_x + 0.001, sy * ey, YOKE_C[1]), WIN_H / 2, 0.014, "shadow", axis="x")
    cyl(f"yoke_window_tube_{side}", (win_x + 0.004, sy * WIN_Y, YOKE_C[1]), 0.011, WIN_W - 0.014, "dark", axis="y")

# =================================================================== CHEST
# From the front photograph of the chest: a squarish shell with chamfered
# upper corners, a tapered collar rising from its top up into the yoke, two
# recessed circuit-board panels over a wide connector panel, and a black
# screwed flange at the bottom where the waist bellows attaches.
CHEST_D, CHEST_W = 0.170, 0.230
CH_BOT, CH_TOP, CH_SHOULDER, CH_TOP_W = -0.172, -0.005, -0.060, 0.160
FACE_X = CHEST_X + CHEST_D / 2                      # front face

profile = [(-CHEST_W / 2, CH_BOT), (CHEST_W / 2, CH_BOT), (CHEST_W / 2, CH_SHOULDER),
           (CH_TOP_W / 2, CH_TOP), (-CH_TOP_W / 2, CH_TOP), (-CHEST_W / 2, CH_SHOULDER)]
mesh_part("chest_shell", (CHEST_X, 0, 0), prism_x(profile, -CHEST_D / 2, CHEST_D / 2), "gray")
mesh_part("chest_collar", (CHEST_X, 0, 0),
          frustum((-0.060, 0.070, -0.078, 0.078, CH_TOP - 0.002), (-0.050, 0.045, -0.064, 0.064, 0.045)), "gray")
box("chest_back", (CHEST_X - CHEST_D / 2 - 0.002, 0, -0.095), (0.004, 0.190, 0.140), "dark")
for side, sy in (("l", 1), ("r", -1)):
    box(f"chest_cheek_{side}", (CHEST_X, sy * (CHEST_W / 2 + 0.002), -0.113), (0.125, 0.004, 0.090), "blue")

# front-face layout, measured off the photograph in pixels: the chest spans
# 40-470 px across and 150-555 px down, so y and z map from those.
_PY = CHEST_W / 430.0
_PZ = (CH_TOP - CH_BOT) / 405.0
def _fy(px): return (px - 255) * _PY                   # image left = robot right (-Y)
def _fz(py): return CH_TOP - (py - 150) * _PZ
def plate(name, px0, px1, py0, py1, mat, proud):
    """Box on the front face spanning the photo rectangle, standing `proud` out."""
    y0, y1, z0, z1 = _fy(px0), _fy(px1), _fz(py1), _fz(py0)
    box(f"chest_{name}", (FACE_X + proud / 2, (y0 + y1) / 2, (z0 + z1) / 2), (proud, y1 - y0, z1 - z0), mat)
def pad(name, px, py, rpx, mat, proud):
    cyl(f"chest_{name}", (FACE_X + proud / 2, _fy(px), _fz(py)), rpx * _PY, proud, mat, axis="x")

# recessed panels (dark), each with a thin lighter border
for name, r in (("panel_a", (120, 255, 220, 415)), ("panel_b_top", (295, 370, 235, 300)),
                ("panel_b", (300, 415, 305, 420)), ("panel_c", (100, 410, 440, 530))):
    plate(name + "_rim", r[0] - 4, r[1] + 4, r[2] - 4, r[3] + 4, "dark", 0.0015)
    plate(name, *r, "shadow", 0.0025)

# panel A: two copper pads, a raised module with two resistors, copper squares, a red part, a star
pad("a_pad_1", 226, 246, 11, "copper", 0.005); pad("a_pad_2", 228, 272, 11, "copper", 0.005)
plate("a_slot", 140, 150, 238, 276, "dark", 0.004)
plate("a_module", 157, 242, 298, 398, "gray", 0.004)
plate("a_res_1", 163, 178, 305, 378, "tan", 0.008); plate("a_res_2", 180, 195, 305, 378, "tan", 0.008)
for i, (px, py) in enumerate(((201, 318), (216, 318), (201, 336), (216, 336))):
    plate(f"a_cu_{i}", px, px + 12, py, py + 14, "copper", 0.007)
plate("a_red", 205, 234, 353, 362, "red", 0.007)
plate("a_star", 178, 193, 372, 388, "gold", 0.007)

# panel B: copper tab, tan parts, resistors, red, star, blue-grey chips
plate("b_tab", 312, 342, 244, 272, "copper", 0.005)
plate("b_tan", 330, 346, 272, 298, "tan", 0.005)
plate("b_res_1", 354, 367, 360, 416, "tan", 0.008); plate("b_res_2", 369, 383, 360, 416, "tan", 0.008)
plate("b_cu", 318, 345, 345, 380, "copper", 0.006)
plate("b_red", 313, 324, 334, 344, "red", 0.006)
plate("b_star", 350, 364, 334, 351, "gold", 0.006)
plate("b_chips", 312, 346, 386, 405, "blue", 0.005)

# panel C: eight connector pins, pads and bars
for i in range(8):
    plate(f"c_pin_{i}", 131 + i * 20, 143 + i * 20, 450, 494, "tan", 0.007)
    plate(f"c_pin_tip_{i}", 133 + i * 20, 141 + i * 20, 494, 500, "gold", 0.004)
plate("c_dark_1", 305, 326, 441, 469, "dark", 0.005); plate("c_dark_2", 305, 326, 475, 500, "dark", 0.005)
pad("c_dot_1", 315, 455, 5, "copper", 0.007); pad("c_dot_2", 315, 488, 5, "copper", 0.007)
plate("c_tan", 344, 366, 440, 480, "tan", 0.007)
plate("c_red", 350, 361, 481, 490, "red", 0.006)
plate("c_cu_1", 324, 360, 503, 526, "copper", 0.005); plate("c_cu_2", 365, 401, 503, 526, "copper", 0.005)
plate("c_bar_1", 114, 170, 505, 526, "black", 0.005); plate("c_bar_2", 255, 286, 505, 526, "black", 0.005)

# black flange under the shell, with screws along its front
box("chest_flange", (CHEST_X, 0, CH_BOT - 0.009), (CHEST_D + 0.012, CHEST_W + 0.014, 0.018), "black")
for i, px in enumerate((80, 190, 305, 420)):
    cyl(f"chest_flange_screw_{i}", (FACE_X + 0.0075, _fy(px), CH_BOT - 0.009), 0.0045, 0.003, "light", axis="x")

# ============================================ SIDE UNIT + HOSE (robot right)
box("side_unit", (0.105, -0.150, -0.040), (0.085, 0.060, 0.100), "gray")
box("side_unit_face", (0.149, -0.150, -0.040), (0.006, 0.045, 0.080), "dark")
cyl("side_unit_knob", (0.160, -0.150, -0.020), 0.012, 0.020, "light", axis="x")
bellows("side_hose", (0.108, -0.150, -0.090), (0.085, -0.128, -0.186), 0.018, 0.013, 7, "black")

# ==================================================================== HANDS
# Built in a canonical frame about the wrist rotation point O: a = forward
# (pointing axis, +X), b = lateral (+Y), c = up (+Z).  Canonically the paired
# fingers sit side by side above and the single finger below; the whole hand is
# then rolled 90 deg about the pointing axis so the single finger faces inward
# (toward the body centreline) and the paired fingers stack vertically.
#
# Axes and pads (blue domes mark the axis ends):
#   * paired fingers share one base axis   - pads on the OUTSIDE of the pair only
#   * each paired finger has a second axis  - pads on both sides of each finger
#   * the single finger has one base axis   - pads on both sides
#   * every finger has an extra pad pair on both sides where it bends inward
HAND_BASE = (0.056, 0.014)      # paired fingers' shared axis (a, c)
HAND_MID = (0.094, 0.018)       # paired fingers' second axis
HAND_BEND = (0.140, 0.014)      # paired fingers bend inward here
HAND_TIP = (0.162, 0.000)
THUMB_BASE = (0.052, -0.024)    # single finger's axis
THUMB_BEND = (0.134, -0.026)
THUMB_TIP = (0.160, -0.012)
FINGER_W, FINGER_Y, THUMB_W = 0.019, 0.021, 0.020

# wrist hinge swivel sits on the wrist rod's axis, this far back from its end
# (the red circle on the v7 render); the connecting body runs forward from its
# centre to the back face of the roll swivel disk.
WRIST_HINGE_BACK = 0.012
WRIST_LINK_LEN = 0.030


def _rx(t):
    return [[1, 0, 0], [0, math.cos(t), -math.sin(t)], [0, math.sin(t), math.cos(t)]]


def pose_rx(parts, pivot, angle):
    R = _rx(angle)
    out = []
    for n, c, rpy, g, m in parts:
        d = [c[i] - pivot[i] for i in range(3)]
        rc = tuple(sum(R[i][k] * d[k] for k in range(3)) + pivot[i] for i in range(3))
        out.append((n, rc, _m_to_rpy(_mm(R, _rpy_to_m(*rpy))), g, m))
    return out


def build_hand(side, sy, O):
    global GROUP
    prev, GROUP = GROUP, "hand"
    GROUPS["hand"] = []
    P = lambda a, b, c: (O[0] + a, O[1] + b, O[2] + c)

    def pad(name, a, b, c, out):
        """Blue domed pad on a lateral face at (a, b, c); out = +1/-1 along b."""
        cyl(f"{name}_base", P(a, b + out * 0.002, c), 0.0115, 0.004, "pad", axis="y")
        sphere(f"{name}_dome", P(a, b - out * 0.003, c), 0.0100, "pad")

    # wrist rotation disk and palm
    cyl(f"wrist_disk_{side}", P(0.000, 0, 0), 0.031, 0.012, "light", axis="x")
    cyl(f"wrist_disk_hub_{side}", P(0.007, 0, 0), 0.016, 0.006, "dark", axis="x")
    box(f"palm_{side}", P(0.032, 0, -0.004), (0.044, 0.068, 0.060), "dark")

    # paired fingers on their shared base axis
    half = FINGER_Y + FINGER_W / 2
    rod(f"finger_base_axle_{side}", P(HAND_BASE[0], -half, HAND_BASE[1]), P(HAND_BASE[0], half, HAND_BASE[1]),
        0.005, "light")
    for out in (1, -1):
        pad(f"finger_base_pad_{side}_{'o' if out > 0 else 'u'}", HAND_BASE[0], out * half, HAND_BASE[1], out)
    for fi, fy in ((0, FINGER_Y), (1, -FINGER_Y)):
        strut(f"finger_prox_{side}_{fi}", P(HAND_BASE[0], fy, HAND_BASE[1]), P(HAND_MID[0], fy, HAND_MID[1]),
              0.022, FINGER_W, "gray")
        cyl(f"finger_knuckle_{side}_{fi}", P(HAND_MID[0], fy, HAND_MID[1]), 0.012, FINGER_W, "gray", axis="y")
        strut(f"finger_dist_{side}_{fi}", P(HAND_MID[0], fy, HAND_MID[1]), P(HAND_BEND[0], fy, HAND_BEND[1]),
              0.022, FINGER_W, "gray")
        strut(f"finger_tip_{side}_{fi}", P(HAND_BEND[0], fy, HAND_BEND[1]), P(HAND_TIP[0], fy, HAND_TIP[1]),
              0.020, FINGER_W, "gray")
        for ri in range(5):                                  # grip ridges, facing the single finger
            a = HAND_MID[0] + 0.006 + ri * 0.009
            c = HAND_MID[1] + (HAND_BEND[1] - HAND_MID[1]) * (a - HAND_MID[0]) / (HAND_BEND[0] - HAND_MID[0])
            box(f"finger_ridge_{side}_{fi}_{ri}", P(a, fy, c - 0.0125), (0.004, FINGER_W * 0.9, 0.004), "black")
        for (a, c), tag in ((HAND_MID, "mid"), (HAND_BEND, "bend")):
            for out in (1, -1):
                pad(f"finger_pad_{tag}_{side}_{fi}_{'p' if out > 0 else 'n'}", a, fy + out * FINGER_W / 2, c, out)

    # single finger
    strut(f"thumb_prox_{side}", P(THUMB_BASE[0], 0, THUMB_BASE[1]), P(THUMB_BEND[0], 0, THUMB_BEND[1]),
          0.020, THUMB_W, "gray")
    strut(f"thumb_tip_{side}", P(THUMB_BEND[0], 0, THUMB_BEND[1]), P(THUMB_TIP[0], 0, THUMB_TIP[1]),
          0.020, THUMB_W, "gray")
    cyl(f"thumb_knuckle_{side}", P(THUMB_BASE[0], 0, THUMB_BASE[1]), 0.012, THUMB_W, "gray", axis="y")
    for ri in range(5):                                      # grip ridges, facing the paired fingers
        a = THUMB_BASE[0] + 0.040 + ri * 0.009
        box(f"thumb_ridge_{side}_{ri}", P(a, 0, THUMB_BASE[1] + 0.0115), (0.004, THUMB_W * 0.9, 0.004), "black")
    for (a, c), tag in ((THUMB_BASE, "base"), (THUMB_BEND, "bend")):
        for out in (1, -1):
            pad(f"thumb_pad_{tag}_{side}_{'p' if out > 0 else 'n'}", a, out * THUMB_W / 2, c, out)

    # roll 90 deg about the pointing axis: single finger (canonically -c) turns
    # to face inward (-sy in Y)
    GROUPS[prev] += pose_rx(GROUPS["hand"], O, -sy * math.pi / 2)
    del GROUPS["hand"]
    GROUP = prev


# ==================================================================== ARMS
# Shoulder: bar end -> rotation seam (lateral axis) -> hub -> housing ->
# swivel (axis along the upper arm) -> upper arm -> elbow.  The swivel sits
# ~40 % of the way from the shoulder centre to the elbow, so the housing and
# its window show below the (large) shoulder hub.
SHOULDER_SEAM_Y = 0.166          # |y| of the rotation seam, right at the window's outer end
HUB_R = 0.060
HUB_LEN = 0.075
HUB_IN = SHOULDER_SEAM_Y + 0.003
HUB_OUT = HUB_IN + HUB_LEN
ARM_Y_TOP = HUB_OUT + 0.027      # |y| of the bracket and swivel: outboard of the hub end
SCOOP_D = 0.032                  # depth of the indentation at the bottom of the hub's outer end
PIVOT_IN = 0.018                 # shoulder pivot: this far in from the hub's outer end ...
PIVOT_UP = 0.040                 # ... and this far above the hub axis; pin runs front to back


def shoulder_hub_tris(sy, n=64):
    """Solid hub along Y from HUB_IN to HUB_OUT (local to the hub axis), with a
    smooth scoop cut into the bottom of its outer end so the bracket can swing
    about the shoulder pivot."""
    def end_y(t):                                   # t: 0 = top, pi = bottom
        return HUB_OUT - SCOOP_D * max(0.0, -math.cos(t)) ** 1.5
    ring = [(HUB_R * math.sin(2 * math.pi * i / n), HUB_R * math.cos(2 * math.pi * i / n), end_y(2 * math.pi * i / n))
            for i in range(n)]
    tris = []
    c_in = (0.0, HUB_IN, 0.0)
    c_out = (0.0, sum(r[2] for r in ring) / n, 0.0)
    for i in range(n):
        j = (i + 1) % n
        (x0, z0, e0), (x1, z1, e1) = ring[i], ring[j]
        a0, a1 = (x0, HUB_IN, z0), (x1, HUB_IN, z1)
        b0, b1 = (x0, e0, z0), (x1, e1, z1)
        tris += [(a0, a1, b1), (a0, b1, b0), (c_in, a1, a0), (c_out, b0, b1)]
    if sy < 0:
        tris = [tuple((x, -y, z) for x, y, z in (c, b, a)) for a, b, c in tris]
    return tris
HOUSING_SHOW = 0.035             # housing visible below the hub
SWIVEL_TOP = 0.066 - HUB_R - HOUSING_SHOW   # bottom of the housing / top of the swivel
SWIVEL_H = 0.018
SWIVEL_R = 0.050                 # collar radius; the housing continues up from it
HOUSING_R = 0.055                # shoulder housing: round front and back ...
HOUSING_HW = 0.027               # ... flat inner and outer sides, 54 mm apart


def shoulder_housing_tris(S, ay, n=40):
    """Cylindrical housing rising from the swivel: round front and back
    (radius HOUSING_R), flat inner and outer sides, and a top that follows the
    shoulder hub's circle about the hub axis, so seen from the side housing and
    hub read as one rounded shape."""
    xs = [HOUSING_R * math.sin(-math.pi / 2 + math.pi * i / n) for i in range(n + 1)]   # denser at the ends
    rows = []
    for x in xs:
        hw = min(HOUSING_HW, math.sqrt(max(0.0, HOUSING_R ** 2 - x * x)))
        zt = S[2] + math.sqrt(HUB_R ** 2 - x * x)
        rows.append((S[0] + x, ay - hw, ay + hw, SWIVEL_TOP, zt))
    tris = []
    for (x0, lo0, hi0, zb0, zt0), (x1, lo1, hi1, zb1, zt1) in zip(rows, rows[1:]):
        tris += [((x0, lo0, zt0), (x1, lo1, zt1), (x1, hi1, zt1)), ((x0, lo0, zt0), (x1, hi1, zt1), (x0, hi0, zt0))]   # top
        tris += [((x0, lo0, zb0), (x1, hi1, zb1), (x1, lo1, zb1)), ((x0, lo0, zb0), (x0, hi0, zb0), (x1, hi1, zb1))]   # bottom
        tris += [((x0, hi0, zb0), (x0, hi0, zt0), (x1, hi1, zt1)), ((x0, hi0, zb0), (x1, hi1, zt1), (x1, hi1, zb1))]   # side +y
        tris += [((x0, lo0, zb0), (x1, lo1, zt1), (x0, lo0, zt0)), ((x0, lo0, zb0), (x1, lo1, zb1), (x1, lo1, zt1))]   # side -y
    return tris


def housing_plate_outline(S, inset, n=24):
    """Tombstone outline on the housing's flat outer side: straight sides, a
    bottom just above the swivel, and a top arc concentric with the hub."""
    half = math.sqrt(HOUSING_R ** 2 - HOUSING_HW ** 2) - inset
    r_top = HUB_R - inset
    zb = SWIVEL_TOP + inset + 0.002
    top = [(S[0] + half * math.cos(math.pi * i / n), S[2] + math.sqrt(max(0.0, r_top ** 2 - (half * math.cos(math.pi * i / n)) ** 2)))
           for i in range(n + 1)]
    return [(S[0] - half, zb), (S[0] + half, zb)] + top[:-1] + [top[-1]]
for side, sy in (("left", 1), ("right", -1)):
    S = (0.075, sy * 0.236, 0.066)            # shoulder centre, on the bar axis
    E = (0.082, sy * 0.262, -0.172)           # elbow
    W = (0.218, sy * 0.262, -0.302)           # wrist

    # shoulder rotation (red arrow): the hub turns on the end of the shoulder
    # bar about the bar's lateral axis; a dark seam ring marks the joint
    cyl(f"shoulder_seam_{side}", (S[0], sy * SHOULDER_SEAM_Y, S[2]), 0.0615, 0.006, "shadow", axis="y")
    mesh_part(f"shoulder_hub_{side}", (S[0], 0.0, S[2]), shoulder_hub_tris(sy), "gray")

    # second pivot (green arrow): pin running front to back near the top of the
    # hub's outer end; the bracket swings about it into the scoop below
    py_, pz_ = sy * (HUB_OUT - PIVOT_IN), S[2] + PIVOT_UP
    half = math.sqrt(HUB_R ** 2 - PIVOT_UP ** 2)             # hub surface at the pin's height
    rod(f"shoulder_pivot_pin_{side}", (S[0] - half - 0.003, py_, pz_), (S[0] + half + 0.003, py_, pz_), 0.0045, "light")
    for fb, fx in (("f", 1), ("b", -1)):
        cyl(f"shoulder_pivot_head_{side}_{fb}", (S[0] + fx * (half + 0.0015), py_, pz_), 0.008, 0.003, "dark", axis="x")

    # shoulder housing: a cylinder rising from the swivel, its top following the
    # hub's circle, with an inset plate on its flat outer side
    ay = sy * ARM_Y_TOP
    mesh_part(f"shoulder_housing_{side}", (0.0, 0.0, 0.0), shoulder_housing_tris(S, ay), "gray")
    face = ay + sy * HOUSING_HW
    mesh_part(f"shoulder_plate_rim_{side}", (0.0, 0.0, 0.0),
              prism_y(housing_plate_outline(S, 0.0035), *sorted((face, face + sy * 0.0006))), "dark")
    mesh_part(f"shoulder_plate_{side}", (0.0, 0.0, 0.0),
              prism_y(housing_plate_outline(S, 0.0060), *sorted((face, face + sy * 0.0013))), "pale")

    # swivel (green arrow): ringed collar, axis along the upper arm
    top = (S[0], ay, SWIVEL_TOP)
    ua = [E[i] - top[i] for i in range(3)]
    ual = math.sqrt(sum(v * v for v in ua)); uu = [v / ual for v in ua]
    at = lambda d: tuple(top[i] + uu[i] * d for i in range(3))
    rod(f"shoulder_swivel_{side}", at(0.000), at(SWIVEL_H), SWIVEL_R, "gray")
    for gi, d in enumerate((SWIVEL_H * 0.33, SWIVEL_H * 0.67)):
        rod(f"shoulder_swivel_groove_{side}_{gi}", at(d - 0.001), at(d + 0.001), SWIVEL_R + 0.0015, "shadow")
    rod(f"shoulder_swivel_lip_{side}", at(SWIVEL_H - 0.002), at(SWIVEL_H + 0.002), SWIVEL_R + 0.003, "light")

    # upper arm below the swivel: hub at the top, flat link with round holes
    ua_top = at(SWIVEL_H)
    cyl(f"upper_arm_hub_{side}", (ua_top[0] - 0.004, ua_top[1], ua_top[2] - 0.022), 0.022, 0.056, "dark", axis="y")
    strut(f"upper_arm_{side}", ua_top, E, 0.070, 0.036, "gray")
    for hi in range(4):
        f = 0.28 + hi * 0.15
        c = tuple(ua_top[i] + (E[i] - ua_top[i]) * f for i in range(3))
        cyl(f"upper_arm_hole_{side}_{hi}", (c[0] + 0.004, c[1] + sy * 0.0175, c[2]), 0.0085, 0.004, "shadow", axis="y")
    rod(f"upper_arm_piston_{side}", (ua_top[0] - 0.036, sy * 0.250, ua_top[2] - 0.030),
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
    # wrist: forearm rod -> hinge swivel (up/down, lateral axis) -> short
    # connecting body -> roll swivel disk (about the hand's pointing axis) -> hand
    HW = (wrist_end[0] - ux * WRIST_HINGE_BACK, W[1], wrist_end[2] - uz * WRIST_HINGE_BACK)
    cyl(f"wrist_hinge_{side}", HW, 0.020, 0.078, "dark", axis="y")
    for cs in (1, -1):
        cyl(f"wrist_hinge_cap_{side}_{'p' if cs > 0 else 'n'}", (HW[0], HW[1] + cs * 0.041, HW[2]),
            0.014, 0.004, "gray", axis="y")
    O = (HW[0] + WRIST_LINK_LEN + 0.006, W[1], HW[2])        # roll disk centre (12 mm thick)
    strut(f"wrist_link_{side}", HW, (O[0] - 0.006, W[1], O[2]), 0.036, 0.040, "gray")
    build_hand(side, sy, O)

# ======================================================= BELLOWS HINGE
# Split bellows: upper half follows the chest, lower half follows the column,
# meeting at the hinge pin.
GROUP = "upper"
bellows_taper("waist_bellows_upper", (CHEST_X, 0, -0.190), (CHEST_X, 0, HINGE0[2]), 0.095, 0.078, 5, "black", groove=0.86)

# ========================================================= NECK LINKAGE (yoke side)
# Rear axle, brackets, drive - built on the upright yoke, posed with the chest.
for sd, sy in (("l", 1), ("r", -1)):
    cyl(f"neck_rear_eye_bot_{sd}", (REAR_X, sy * 0.057, NECK_AXLE0[2]), 0.0075, 0.008, "dark", axis="y")
    for bi, by in enumerate((0.047, 0.067)):
        bb = YOKE_TOP0 - 0.012                                   # seated into the curved top
        box(f"neck_axle_bracket_{sd}_{bi}", (REAR_X, sy * by, (bb + NECK_AXLE0[2] + 0.010) / 2),
            (0.022, 0.005, NECK_AXLE0[2] + 0.010 - bb), "gray")
    cyl(f"neck_axle_cap_{sd}", (REAR_X, sy * 0.0715, NECK_AXLE0[2]), 0.007, 0.004, "light", axis="y")
rod("neck_rear_axle", (REAR_X, -0.071, NECK_AXLE0[2]), (REAR_X, 0.071, NECK_AXLE0[2]), 0.0045, "light")
cyl("neck_drive_hub", (REAR_X, 0, NECK_AXLE0[2]), 0.014, 0.026, "dark", axis="y")
gx = REAR_X - 0.030
box("neck_drive_gearbox", (gx, 0, yoke_surface_z(gx) + 0.006), (0.034, 0.040, 0.030), "dark")

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
def _hit(radius):
    """Smallest t where the rod line reaches `radius` from the yoke axis (x-z plane)."""
    px, pz = f0[0] - YOKE_C[0], f0[2] - YOKE_C[1]
    a = fd[0] ** 2 + fd[2] ** 2; b = 2 * (px * fd[0] + pz * fd[2]); c = px * px + pz * pz - radius ** 2
    return (-b - math.sqrt(b * b - 4 * a * c)) / (2 * a)
t_top = _hit(YOKE_R + HOUSING_H)                            # enters the housing top
t_bot = _hit(YOKE_R + 0.004)                                # rod ends just above the yoke surface
SLOT0 = tuple(f0[i] + fd[i] * (t_top + t_bot) / 2 for i in range(3))
FRONT_ROD_LEN = t_bot                                       # ROD_U is unit length
# housing sits on the surface, square to the normal through the slot
nang = math.atan2(SLOT0[0] - YOKE_C[0], SLOT0[2] - YOKE_C[1])       # from +Z toward +X
nx_, nz_ = math.sin(nang), math.cos(nang)
hc = (YOKE_C[0] + nx_ * (YOKE_R + HOUSING_H / 2 - 0.004), 0, YOKE_C[1] + nz_ * (YOKE_R + HOUSING_H / 2 - 0.004))
tx_, tz_ = nz_, -nx_                                                 # tangent, pointing forward/down
box("neck_rod_housing", hc, (0.050, 0.070, HOUSING_H + 0.008), "gray", rpy=(0, nang, 0))
box("neck_rod_slot", (hc[0] + tx_ * 0.012 + nx_ * 0.001, 0, hc[2] + tz_ * 0.012 + nz_ * 0.001),
    (0.030, 0.014, HOUSING_H + 0.010), "shadow", rpy=(0, nang, 0))

# =========================================================== WAIST COLUMN
# Built upright along +Z from the drum centre, then tilted back about the drum.
# Detail from the front photograph below the chest: a tall, finely convoluted
# bellows; a grey column with side modules at the top, two blue panels wrapping
# its front, a side electronics module, and a wider electronics block low down.
GROUP = "column"
L = COLUMN_LEN
cx, cz = DRUM[0], DRUM[2]
BELLOWS_LOW = 0.064                                   # lower bellows length (was 0.036)
bellows_taper("waist_bellows_lower", (cx, 0, cz + L), (cx, 0, cz + L - BELLOWS_LOW), 0.078, 0.069, 8, "black",
              groove=0.86)
cyl("hinge_pin", (cx, 0, cz + L), 0.022, 0.150, "gray", axis="y")
for sd, sy in (("l", 1), ("r", -1)):
    cyl(f"hinge_pin_cap_{sd}", (cx, sy * 0.078, cz + L), 0.030, 0.008, "blue", axis="y")
    box(f"hinge_cheek_{sd}", (cx, sy * 0.070, cz + L - 0.030), (0.050, 0.008, 0.060), "dark")
cyl("waist_ring_top", (cx, 0, cz + L - BELLOWS_LOW - 0.016), 0.080, 0.032, "gray")
col_top, col_bot = cz + L - BELLOWS_LOW - 0.032, cz
COL_R = 0.068

# photo mapping: column spans 120-290 px across (136 mm) and 230-600 px down
# (from the top of the column to the top of the drum)
_CX = 2 * COL_R / 170.0
_CZ = ((col_top - cz) - DRUM_R) / 370.0
def cy_(px): return (px - 205) * _CX                 # image left = robot right (-Y)
def cz_(py): return col_top - (py - 230) * _CZ
def on_col(phi, r):                                   # point on/around the column at angle phi from the front
    return (cx + r * math.cos(phi), r * math.sin(phi))
def col_box(name, phi, r, z, size, mat):
    x, y = on_col(phi, r)
    box(name, (x, y, z), size, mat, rpy=(0, 0, phi))

def arc_shell(r0, r1, p0, p1, z0, z1, n=16):
    """Curved panel: part of a cylindrical shell between radii r0..r1, angles p0..p1."""
    pts = lambda r: [(r * math.cos(p0 + (p1 - p0) * i / n), r * math.sin(p0 + (p1 - p0) * i / n)) for i in range(n + 1)]
    O, I = pts(r1), pts(r0); tris = []
    for i in range(n):
        for ring, out in ((O, True), (I, False)):
            a0, a1 = ring[i], ring[i + 1]
            q = [(a0[0], a0[1], z0), (a1[0], a1[1], z0), (a1[0], a1[1], z1), (a0[0], a0[1], z1)]
            tris += [(q[0], q[1], q[2]), (q[0], q[2], q[3])] if out else [(q[0], q[2], q[1]), (q[0], q[3], q[2])]
        for z, up in ((z0, False), (z1, True)):
            q = [(I[i][0], I[i][1], z), (O[i][0], O[i][1], z), (O[i + 1][0], O[i + 1][1], z), (I[i + 1][0], I[i + 1][1], z)]
            tris += [(q[0], q[2], q[1]), (q[0], q[3], q[2])] if not up else [(q[0], q[1], q[2]), (q[0], q[2], q[3])]
    for ring_p in ((O[0], I[0]), (O[-1], I[-1])):
        o, i_ = ring_p
        tris += [((o[0], o[1], z0), (i_[0], i_[1], z0), (i_[0], i_[1], z1)), ((o[0], o[1], z0), (i_[0], i_[1], z1), (o[0], o[1], z1))]
    return tris

# --- upper band with side modules (photo 230-290 px) ---
cyl("waist_band_upper", (cx, 0, cz_(240)), COL_R + 0.003, 0.020, "gray")
col_box("waist_module_r", math.radians(-52), COL_R + 0.012, cz_(262), (0.030, 0.042, 0.050), "dark")
for i, py in enumerate((246, 262, 278)):
    x, y = on_col(math.radians(-52), COL_R + 0.028)
    cyl(f"waist_module_r_button_{i}", (x, y, cz_(py)), 0.0055, 0.006, "pale", axis="x")
col_box("waist_module_l", math.radians(52), COL_R + 0.012, cz_(258), (0.030, 0.042, 0.058), "dark")
for i, (dpy, dz_, m) in enumerate(((-1, 0.012, "tan"), (1, 0.012, "copper"), (-1, -0.004, "copper"),
                                   (1, -0.004, "red"), (-1, -0.020, "tan"), (1, -0.020, "gold"))):
    phi = math.radians(52) + dpy * 0.12
    col_box(f"waist_module_l_chip_{i}", phi, COL_R + 0.028, cz_(258) + dz_, (0.004, 0.014, 0.011), m)

# --- blue panels wrapping the front (photo 295-455 px), seam down the middle ---
bz0, bz1 = cz_(455), cz_(295)
for nm, p0, p1 in (("r", -38, -0.8), ("l", 0.8, 38)):
    mesh_part(f"waist_blue_panel_{nm}", (cx, 0.0, 0.0),
              [tuple((v[0], v[1], v[2]) for v in t) for t in
               arc_shell(COL_R - 0.001, COL_R + 0.002, math.radians(p0), math.radians(p1), bz0, bz1)], "pad")
col_box("waist_strip_r", math.radians(-60), COL_R + 0.002, (bz0 + bz1) / 2, (0.006, 0.013, bz1 - bz0 - 0.008), "black")

# side electronics module (red arrow, right of the photo = robot left)
EZ0, EZ1 = cz_(440), cz_(300)
col_box("waist_elec_module", math.radians(64), COL_R + 0.008, (EZ0 + EZ1) / 2, (0.018, 0.034, EZ1 - EZ0), "dark")
stack = ((305, 312, "copper"), (314, 321, "copper"), (330, 345, "black"), (352, 362, "gold"),
         (366, 394, "tan"), (398, 408, "copper"), (410, 436, "tan"))
for i, (y0, y1, m) in enumerate(stack):
    col_box(f"waist_elec_part_{i}", math.radians(64), COL_R + 0.019, (cz_(y1) + cz_(y0)) / 2,
            (0.005, 0.026 if m != "black" else 0.016, cz_(y0) - cz_(y1)), m)
x, y = on_col(math.radians(64), COL_R + 0.022)
cyl("waist_elec_knob", (x, y, cz_(337)), 0.006, 0.008, "light", axis="x")

cyl("waist_band_lower", (cx, 0, cz_(462)), COL_R + 0.003, 0.016, "gray")

# --- notch, black disk and electronics panel (lower waist) ---------------
# From the close-up: the front of the waist is notched from just under the
# lower band down to the pelvis front housing.  A large black disk, facing
# forward, sits in the notch; a narrower rectangular panel in front of it
# carries the electronics, so the disk's left and right edges show either
# side, with an arc of copper studs round its rim on the robot's left.  All
# of it stays within the waist band's diameter.
BAND_R = COL_R + 0.003
NOTCH_TOP = cz_(462) - 0.008                        # underside of the lower band
_PANEL_BOT_H = 0.088                                # meets the front housing's top in the posed robot
NOTCH_BOT = cz + _PANEL_BOT_H
cyl("waist_column", (cx, 0, (col_top + NOTCH_TOP) / 2), COL_R, col_top - NOTCH_TOP, "gray")
mesh_part("waist_column_rear", (cx, 0.0, 0.0), arc_shell(0.0005, COL_R, math.pi / 2, 3 * math.pi / 2, col_bot, NOTCH_TOP, 24), "gray")

# photo mapping for the close-up: panel spans 190-730 px across, 160-830 px down
_PANEL_HALF_W = 0.0475
_QX = 2 * _PANEL_HALF_W / 540.0
_QZ = (NOTCH_TOP - NOTCH_BOT) / 670.0
def qy(px): return (px - 460) * _QX                  # image left = robot right (-Y)
def qz(py): return NOTCH_TOP - (py - 160) * _QZ

DISK_R, DISK_X0, DISK_X1 = 0.064, -0.006, 0.030      # disk faces forward, front face 30 mm ahead of the axis
DISK_Z = qz(490)
cyl("waist_disk", (cx + (DISK_X0 + DISK_X1) / 2, 0, DISK_Z), DISK_R, DISK_X1 - DISK_X0, "black", axis="x")
cyl("waist_disk_rim", (cx + DISK_X1 - 0.001, 0, DISK_Z), DISK_R, 0.003, "dark", axis="x")
cyl("waist_disk_face", (cx + DISK_X1, 0, DISK_Z), DISK_R - 0.006, 0.003, "black", axis="x")
for i in range(11):                                  # copper studs round the rim, robot's left
    a_ = math.radians(-38 + i * 7.6)
    cyl(f"waist_disk_stud_{i}", (cx + DISK_X1 + 0.003, 0.0545 * math.cos(a_), DISK_Z + 0.0545 * math.sin(a_)),
        0.0036, 0.006, "copper", axis="x")

PANEL_X0, PANEL_X1 = DISK_X1, 0.050
box("waist_panel", (cx + (PANEL_X0 + PANEL_X1) / 2, 0, (NOTCH_TOP + NOTCH_BOT) / 2),
    (PANEL_X1 - PANEL_X0, 2 * _PANEL_HALF_W, NOTCH_TOP - NOTCH_BOT), "dark")
def part(name, px0, px1, py0, py1, mat, proud):
    y0, y1, z0, z1 = qy(px0), qy(px1), qz(py1), qz(py0)
    box(f"waist_panel_{name}", (cx + PANEL_X1 + proud / 2, (y0 + y1) / 2, (z0 + z1) / 2), (proud, y1 - y0, z1 - z0), mat)

# left: U bracket and the copper contact strip
part("u_bracket", 197, 277, 235, 325, "black", 0.004); part("u_slot", 222, 252, 245, 292, "dark", 0.005)
for i in range(11):
    part(f"contact_{i:02d}", 195, 275, 335 + i * 39, 368 + i * 39, "copper", 0.004)
# middle-left: two dark squares, tan connector block with dots and copper pads
part("sq_1", 340, 390, 162, 225, "black", 0.004); part("sq_2", 395, 445, 162, 225, "black", 0.004)
part("tan_block", 338, 408, 230, 405, "tan", 0.004)
for i in range(5):
    part(f"tan_row_{i}", 340, 404, 262 + i * 33, 266 + i * 33, "dark", 0.005)
    part(f"dot_{i}", 408, 414, 243 + i * 33, 251 + i * 33, "black", 0.005)
    part(f"cu_pad_{i}", 414, 445, 236 + i * 33, 262 + i * 33, "copper", 0.005)
part("mod_left", 310, 430, 410, 550, "black", 0.003); part("mod_left_plug", 318, 345, 468, 495, "pale", 0.005)
part("arch", 330, 470, 575, 805, "dark", 0.004); part("arch_plug", 385, 410, 655, 705, "tan", 0.006)
part("arch_cu", 340, 450, 735, 800, "copper", 0.005)
# middle: pale tall block, copper block, orange/black striped part
part("pale", 457, 523, 192, 410, "pale", 0.005)
part("cu_block", 450, 525, 420, 560, "copper", 0.004)
part("stripe", 482, 535, 620, 800, "black", 0.004)
part("stripe_1", 488, 530, 640, 700, "amber", 0.006); part("stripe_2", 495, 522, 712, 780, "amber", 0.006)
# right: dark top, star module, pads, tan bar, big module with studs
part("r_top", 545, 665, 165, 230, "black", 0.004)
part("star_mod", 545, 672, 235, 400, "black", 0.004); part("star", 578, 620, 262, 308, "gold", 0.006)
part("star_btn", 585, 612, 320, 348, "pale", 0.006)
part("mod_right", 540, 672, 418, 570, "black", 0.004); part("mod_right_cu", 552, 628, 455, 485, "copper", 0.006)
part("mod_right_gold", 565, 615, 505, 555, "gold", 0.006); part("mod_right_sq", 630, 657, 505, 552, "copper", 0.006)
part("tan_bar", 552, 670, 577, 610, "tan", 0.005)
part("big_mod", 545, 690, 628, 805, "dark", 0.005)
for i in range(3):
    part(f"big_mod_stud_{i}", 645, 672, 730 + i * 25, 750 + i * 25, "copper", 0.007)

# hinge at the drum: the column's lower yoke wraps the drum
cyl("pelvis_hinge_collar", (cx, 0, cz), DRUM_R + 0.006, 0.056, "dark", axis="y")
for sd, sy in (("l", 1), ("r", -1)):
    box(f"pelvis_hinge_cheek_{sd}", (cx - 0.015, sy * 0.034, cz + 0.070), (0.070, 0.010, 0.100), "gray")

# ==================================================================== TRACKS
GROUP = "lower"
# Triangular track units: front and rear rollers low and level, a drive
# sprocket high near the hip.  The belt is a closed loop of straight runs plus
# a wrapped end at each roller.  Tread plates sit on the two upper runs only,
# so the belt underside stays exactly on the ground.
BELT_T = 0.012
BELT_W = 0.170                   # belt width == roller width (narrowed from 0.200, photo ratio)
ROLLER_R = 0.086                 # front and rear rollers
SPROCKET_R = 0.075
TRACK_OUTER_SPAN = 0.590         # outer edge to outer edge of the belts, unchanged
TRACK_Y = (TRACK_OUTER_SPAN - BELT_W) / 2
TRACK_X = 0.090
AXLE_SPACING = 0.484
AXLE_Z = GROUND + BELT_T + ROLLER_R                     # -0.9948
# Drive sprocket centre, placed at the point marked on the v3 render (read off
# the screenshot using the roller spacing and sprocket height as scale).
SPROCKET_X = 0.000
SPROCKET_Z = -0.800
TRACK_TOP = SPROCKET_Z + SPROCKET_R + BELT_T

CLEAT_PITCH = 0.042
HUB_BOLTS, HUB_BOLT_R = 5, 0.034

assert abs((DRUM[2] + DRUM_R) - (SPROCKET_Z + SPROCKET_R + BELT_T - 0.002)) < 1e-9   # top just below the treads
DRUM_LEN = 2 * (TRACK_Y - BELT_W / 2 - 0.0035 - 0.004) - 0.003     # fills the gap between the shroud plates
cyl("pelvis_drum", (DRUM[0], 0, DRUM[2]), DRUM_R, DRUM_LEN, "gray", axis="y")
cyl("pelvis_drum_band", (DRUM[0], 0, DRUM[2]), DRUM_R + 0.003, 0.030, "dark", axis="y")

# Front housing on the drum.  Upper part: tapered, with a blue panel on its
# upper right.  Below a band of panel plates, the housing becomes a curved
# shell concentric with the drum that wraps round its front and ends at the
# drum's bottom, carrying two blue panels side by side (from the photograph).
_OLD_BOT, PH_TOP = (0.140, 0.110, -0.075), (0.080, 0.075, 0.100)  # (front x, half width, z) about the drum
PH_BREAK_Z = -0.030                                                # where the taper meets the curved shell
_f = (PH_BREAK_Z - _OLD_BOT[2]) / (PH_TOP[2] - _OLD_BOT[2])
PH_BOT = (_OLD_BOT[0] + (PH_TOP[0] - _OLD_BOT[0]) * _f, _OLD_BOT[1] + (PH_TOP[1] - _OLD_BOT[1]) * _f, PH_BREAK_Z)
mesh_part("pelvis_front_housing", (DRUM[0], 0.0, DRUM[2]),
          frustum((0.000, PH_BOT[0], -PH_BOT[1], PH_BOT[1], PH_BOT[2]),
                  (0.000, PH_TOP[0], -PH_TOP[1], PH_TOP[1], PH_TOP[2])), "gray")
def _ph_face(t, y, out):
    """Point on the upper housing's sloped front face: t 0 bottom .. 1 top, offset `out` along its normal."""
    x = PH_BOT[0] + (PH_TOP[0] - PH_BOT[0]) * t; z = PH_BOT[2] + (PH_TOP[2] - PH_BOT[2]) * t
    nx, nz = (PH_TOP[2] - PH_BOT[2]), -(PH_TOP[0] - PH_BOT[0]); l = math.hypot(nx, nz)
    return (x + nx / l * out, y, z + nz / l * out)
def _ph_slab(t0, t1, y0, y1, d0, d1):
    q = lambda t, y, o: _ph_face(t, y, o)
    B = [q(t0, y0, d0), q(t0, y1, d0), q(t1, y1, d0), q(t1, y0, d0)]
    T = [q(t0, y0, d1), q(t0, y1, d1), q(t1, y1, d1), q(t1, y0, d1)]
    tris = [(B[0], B[2], B[1]), (B[0], B[3], B[2]), (T[0], T[1], T[2]), (T[0], T[2], T[3])]
    for i in range(4):
        j = (i + 1) % 4; tris += [(B[i], B[j], T[j]), (B[i], T[j], T[i])]
    return tris
mesh_part("pelvis_front_blue", (DRUM[0], 0.0, DRUM[2]), _ph_slab(0.08, 0.95, 0.006, 0.066, -0.001, 0.002), "pad")
mesh_part("pelvis_front_tab", (DRUM[0], 0.0, DRUM[2]), _ph_slab(0.10, 0.18, 0.044, 0.058, 0.002, 0.004), "gray")

# curved lower shell, concentric with the drum
SHELL_R = math.hypot(PH_BOT[0], PH_BREAK_Z)              # meets the taper's front edge exactly
SHELL_T0 = math.atan2(PH_BREAK_Z, PH_BOT[0])              # just below the front
SHELL_T1 = -math.pi / 2                                   # the drum's bottom
SHELL_W = 0.110
def arc_y(r0, r1, t0, t1, y0, y1, n=28):
    """Solid curved slab about the drum axis (Y): radii r0..r1, angles t0..t1 in the x-z plane, y0..y1."""
    P = lambda r, t, y: (r * math.cos(t), y, r * math.sin(t))
    ts = [t0 + (t1 - t0) * i / n for i in range(n + 1)]
    tris = []
    for i in range(n):
        a_, b_ = ts[i], ts[i + 1]
        for r, out in ((r1, True), (r0, False)):
            q = [P(r, a_, y0), P(r, b_, y0), P(r, b_, y1), P(r, a_, y1)]
            tris += [(q[0], q[2], q[1]), (q[0], q[3], q[2])] if out else [(q[0], q[1], q[2]), (q[0], q[2], q[3])]
        for y, side in ((y0, False), (y1, True)):
            q = [P(r0, a_, y), P(r1, a_, y), P(r1, b_, y), P(r0, b_, y)]
            tris += [(q[0], q[1], q[2]), (q[0], q[2], q[3])] if side else [(q[0], q[2], q[1]), (q[0], q[3], q[2])]
    for t in (t0, t1):
        q = [P(r0, t, y0), P(r1, t, y0), P(r1, t, y1), P(r0, t, y1)]
        tris += [(q[0], q[1], q[2]), (q[0], q[2], q[3])]
    return tris
DO = (DRUM[0], 0.0, DRUM[2])
mesh_part("pelvis_front_shell", DO, arc_y(DRUM_R - 0.004, SHELL_R, SHELL_T0, SHELL_T1, -SHELL_W, SHELL_W), "gray")
# band of panel plates just below the taper, with dark seams (as in the photo)
BAND_T0, BAND_T1 = SHELL_T0 - math.radians(1.5), SHELL_T0 - math.radians(10.5)
for i, (y0, y1) in enumerate(((-0.106, -0.062), (-0.058, 0.044), (0.048, 0.106))):
    mesh_part(f"pelvis_band_plate_{i}", DO, arc_y(SHELL_R, SHELL_R + 0.0018, BAND_T0, BAND_T1, y0, y1, 6), "gray")
for i, (y0, y1) in enumerate(((-0.0115, -0.0035), (0.0015, 0.0095))):
    mesh_part(f"pelvis_band_tab_{i}", DO, arc_y(SHELL_R + 0.0018, SHELL_R + 0.0036,
                                                  BAND_T0 - math.radians(3.5), BAND_T0 - math.radians(6.5), y0, y1, 2), "light")
mesh_part("pelvis_band_seam", DO, arc_y(SHELL_R - 0.001, SHELL_R + 0.0012, BAND_T1 - math.radians(1.2), BAND_T1 - math.radians(0.2),
                                          -SHELL_W, SHELL_W, 2), "dark")
# the two blue panels below the band (green arrows), split by a centre seam
PANEL_T0, PANEL_T1 = BAND_T1 - math.radians(2.0), math.radians(-74)
for nm, (y0, y1) in (("r", (-0.104, -0.0035)), ("l", (0.0035, 0.104))):
    mesh_part(f"pelvis_lower_blue_{nm}", DO, arc_y(SHELL_R - 0.001, SHELL_R + 0.0022, PANEL_T0, PANEL_T1, y0, y1, 20), "pad")
mesh_part("pelvis_lower_seam", DO, arc_y(SHELL_R - 0.001, SHELL_R + 0.0015, PANEL_T0, SHELL_T1, -0.0035, 0.0035, 20), "dark")
_sx, _sz = (SHELL_R + 0.002) * math.cos(math.radians(-80)), (SHELL_R + 0.002) * math.sin(math.radians(-80))
rod("pelvis_lower_screw", (DO[0] + _sx * 0.99, 0.0, DO[2] + _sz * 0.99), (DO[0] + _sx * 1.03, 0.0, DO[2] + _sz * 1.03), 0.004, "dark")


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
        if rname == "front":                                  # sprocket and rear hubs are under the shroud
            cyl(f"track_{rname}_hub_{side}", (rx, y + sy * (BELT_W / 2 + 0.012), rz), 0.046, 0.024, "gray", axis="y")
            cyl(f"track_{rname}_hub_cap_{side}", (rx, y + sy * (BELT_W / 2 + 0.026), rz), 0.022, 0.006, "dark", axis="y")
            for bi in range(HUB_BOLTS):                   # make hub rotation visible when animated
                ba = 2 * math.pi * bi / HUB_BOLTS
                cyl(f"track_{rname}_hub_bolt_{side}_{bi}", (rx + HUB_BOLT_R * math.cos(ba), y + sy * (BELT_W / 2 + 0.0255),
                    rz + HUB_BOLT_R * math.sin(ba)), 0.0045, 0.004, "light", axis="y")

    # the belt itself (band, tread plates, pads) is built in the TREADS section

    # side frames inside the belt loop (inboard and outboard)
    for fname, fy in (("in", y - sy * (BELT_W / 2 - 0.014)), ("out", y + sy * (BELT_W / 2 - 0.014))):
        box(f"track_frame_low_{fname}_{side}", (TRACK_X, fy, AXLE_Z + 0.010),
            (AXLE_SPACING * 0.92, 0.012, 0.120), "gray")
        strut(f"track_frame_fore_{fname}_{side}", (front[0] - 0.050, fy, AXLE_Z + 0.030),
              (top[0], fy, top[1] - 0.020), 0.090, 0.012, "gray")
        strut(f"track_frame_aft_{fname}_{side}", (rear[0] + 0.060, fy, AXLE_Z + 0.030),
              (top[0] - 0.010, fy, top[1] - 0.020), 0.110, 0.012, "gray")

    # axle housing from the pelvis drum out to the sprocket
    cyl(f"hip_axle_{side}", (SPROCKET_X, sy * (DRUM_LEN / 2 + 0.004), SPROCKET_Z), 0.048, 0.008, "dark", axis="y")

# ============================================================ TRACK SHROUDS
# A shroud around each track, following the edge points marked on the v9
# render and the side photograph:
#   * front edge runs just inside the front-upper tread run (treads visible)
#   * at the sprocket's top-rear it steps out and wraps OVER the tread
#   * the wrap covers the whole rear-upper run and the back of the rear roller
#   * the lower edge runs just above the bottom tread (bottom belt visible)
#   * side plates on both faces; the front roller stays exposed with its hub
# The outline follows the belt curves, so it is generated as a binary STL in
# Meshes/Body and referenced from the URDF like the head's meshes.

SHROUD_CLEAR = 0.012             # above the belt's outer surface (clears the tread plates)
SHROUD_T = 0.004                 # plate and wrap thickness
SHROUD_BOTTOM = GROUND + BELT_T + 0.0074 + 0.0120   # just above the bottom run: -1.0614
INNER_Y = TRACK_Y - BELT_W / 2 - 0.0035 # inner plate outer face, 0.5 mm clear of the belt edge
OUTER_Y = TRACK_Y + BELT_W / 2 + 0.0015 # outer plate inner face, 1.5 mm clear of the belt edge

# Side outline, from the photograph: 90 deg corners at the front (A), top (B)
# and rear (C); 135 deg corners (D, E) where a flat bottom edge runs just above
# the bottom tread.  A 135 deg corner against a horizontal edge puts the other
# edge at exactly 45 deg, and the 90 deg corners then put every edge at 45 deg:
# a rectangle rotated 45 deg with its bottom corner cut off.
SH_B = (SPROCKET_X + 0.050, SPROCKET_Z + SPROCKET_R + BELT_T + 0.013)   # top corner, just above the sprocket
SH_KA = 0.150                    # B -> A: 45 deg forward and down, to just under the front run
SH_KC = 0.280                    # B -> C: 45 deg back and down, onto the rear roller's upper rear
SH_A = (SH_B[0] + SH_KA, SH_B[1] - SH_KA)
SH_C = (SH_B[0] - SH_KC, SH_B[1] - SH_KC)
_SH_F = (SH_A[0] - SH_KC, SH_A[1] - SH_KC)                               # the corner that gets cut off
_sh_s = SHROUD_BOTTOM - _SH_F[1]
assert 0 < _sh_s < min(SH_KA, SH_KC), "bottom edge must cut the lower corner"
SH_E = (_SH_F[0] + _sh_s, SHROUD_BOTTOM)                                 # 135 deg, front of the bottom edge
SH_D = (_SH_F[0] - _sh_s, SHROUD_BOTTOM)                                 # 135 deg, rear of the bottom edge
SHROUD_OUTLINE = [SH_E, SH_A, SH_B, SH_C, SH_D]                          # counter-clockwise in x-z

# The housing on the back of the shroud (green arrow): a thick sculpted block
# along the B-C edge, the full shroud width, wrapping over the top of the track
# so it hides the rear-upper tread run.  (u, w) coordinates: u along the edge
# from B to C, w outward from it (up and back).
HOUSING_T1, HOUSING_T2 = 0.060, 0.086    # base block, then the narrower upper tier
_R2 = math.sqrt(0.5)
_HU = (-_R2, -_R2); _HW = (-_R2, _R2)
HOUSING_L = SH_KC / _R2
def uw_to_xz(u, w):
    return (SH_B[0] + _HU[0] * u + _HW[0] * w, SH_B[1] + _HU[1] * u + _HW[1] * w)
def xz_to_uw(x, z):
    dx, dz = x - SH_B[0], z - SH_B[1]
    return (dx * _HU[0] + dz * _HU[1], dx * _HW[0] + dz * _HW[1])


def _uw_poly(pts):
    return [uw_to_xz(u, w) for u, w in pts]


L_ = HOUSING_L
HOUSING_BASE = _uw_poly([(0.0, 0.0), (L_, 0.0), (L_, HOUSING_T1 - 0.020), (L_ - 0.020, HOUSING_T1),
                         (0.020, HOUSING_T1), (0.0, HOUSING_T1 - 0.020)])
HOUSING_TIER = _uw_poly([(0.030, HOUSING_T1 - 0.002), (L_ - 0.070, HOUSING_T1 - 0.002), (L_ - 0.070, HOUSING_T2 - 0.014),
                         (L_ - 0.086, HOUSING_T2), (0.058, HOUSING_T2), (0.030, HOUSING_T2 - 0.020)])


def under_housing(x, z, margin=0.0):
    """True if a belt point sits under the housing (hidden from every side)."""
    u, w = xz_to_uw(x, z)
    return margin <= u <= HOUSING_L - margin and -0.020 <= w <= HOUSING_T1 - margin


for side, sy in (("left", 1), ("right", -1)):
    yi0, yi1 = sy * (INNER_Y - SHROUD_T), sy * INNER_Y
    yo0, yo1 = sy * OUTER_Y, sy * (OUTER_Y + SHROUD_T)
    tris = prism_y(SHROUD_OUTLINE, min(yi0, yi1), max(yi0, yi1)) + prism_y(SHROUD_OUTLINE, min(yo0, yo1), max(yo0, yo1))
    mesh_part(f"track_shroud_{side}", (0.0, 0.0, 0.0), tris, "gray")
    # housing: base the full shroud width, upper tier inset, blue panel and a notched pad
    ya, yb = sorted((sy * (INNER_Y - SHROUD_T), sy * (OUTER_Y + SHROUD_T)))
    mesh_part(f"shroud_housing_{side}", (0.0, 0.0, 0.0), prism_y(HOUSING_BASE, ya, yb), "gray")
    mesh_part(f"shroud_housing_tier_{side}", (0.0, 0.0, 0.0), prism_y(HOUSING_TIER, ya + 0.018, yb - 0.018), "gray")
    mesh_part(f"shroud_housing_blue_{side}", (0.0, 0.0, 0.0),
              prism_y(_uw_poly([(0.075, HOUSING_T2 - 0.001), (0.205, HOUSING_T2 - 0.001), (0.205, HOUSING_T2 + 0.0025),
                                (0.075, HOUSING_T2 + 0.0025)]), ya + 0.030, yb - 0.030), "blue")
    pad_y = (yb - 0.050, yb - 0.018) if sy > 0 else (ya + 0.018, ya + 0.050)
    mesh_part(f"shroud_housing_pad_{side}", (0.0, 0.0, 0.0),
              prism_y(_uw_poly([(0.230, HOUSING_T1 - 0.002), (0.300, HOUSING_T1 - 0.002), (0.300, HOUSING_T1 + 0.020),
                                (0.268, HOUSING_T1 + 0.020), (0.268, HOUSING_T1 + 0.010), (0.250, HOUSING_T1 + 0.010),
                                (0.250, HOUSING_T1 + 0.020), (0.230, HOUSING_T1 + 0.020)]), *pad_y), "gray")
    face = sy * (OUTER_Y + SHROUD_T)
    # bolted covers over the hidden sprocket and rear roller axles
    for cname, (cx_, cz_) in (("sprocket", (SPROCKET_X, SPROCKET_Z)), ("rear", (TRACK_X - AXLE_SPACING / 2, AXLE_Z))):
        cyl(f"shroud_{cname}_cover_{side}", (cx_, face + sy * 0.003, cz_), 0.040, 0.006, "gray", axis="y")
        cyl(f"shroud_{cname}_bolt_{side}", (cx_, face + sy * 0.007, cz_), 0.011, 0.004, "dark", axis="y")
    # dark blue strip along the bottom edge, its ends following the 45 deg edges
    zs0, zs1 = SHROUD_BOTTOM + 0.004, SHROUD_BOTTOM + 0.046
    strip = [(SH_E[0] + (zs0 - SH_E[1]) - 0.004, zs0), (SH_E[0] + (zs1 - SH_E[1]) - 0.004, zs1),
             (SH_D[0] - (zs1 - SH_D[1]) + 0.004, zs1), (SH_D[0] - (zs0 - SH_D[1]) + 0.004, zs0)]
    fa, fb = sorted((face, face + sy * 0.003))
    mesh_part(f"shroud_strip_{side}", (0.0, 0.0, 0.0), prism_y(strip, fa, fb), "blue")
    for li, f_ in enumerate((1 / 3, 2 / 3)):                      # two diagonal panel lines, as in the photo
        x0 = strip[0][0] + (strip[3][0] - strip[0][0]) * f_
        line = [(x0, zs0), (x0 + (zs1 - zs0), zs1), (x0 + (zs1 - zs0) + 0.003, zs1), (x0 + 0.003, zs0)]
        la, lb = sorted((face + sy * 0.003, face + sy * 0.0042))
        mesh_part(f"shroud_strip_line_{side}_{li}", (0.0, 0.0, 0.0), prism_y(line, la, lb), "dark")

# =================================================================== TREADS
# Patterned belt, from the tread photograph: a solid grey band carrying grey
# link plates the full belt width, each with a black trapezoidal pad.  Seen
# from the front on the front run, each pad is narrower at the top (the edge
# toward the sprocket).  Sizes are ratios of the belt width measured from the photo.  The
# belt's total thickness stays BELT_T (band + plate + pad), so the rollers still
# clear the ground by exactly the belt thickness and the tread rests on the
# floor.  Links run from the floor, round the front roller and up to the
# shroud; none are made where the shroud's wrap covers the belt.
# The pads' outer faces stop 0.2 mm short of BELT_T: flat links bridging the
# roller curves would otherwise dip 0.1 mm below the ground near the bottom of
# the front roller.
BAND_T, PLATE_T, PAD_T = 0.004, 0.004, 0.0038
TREAD_PITCH_TARGET = 0.183 * BELT_W                  # link pitch        (photo: 0.18 x belt width)
PLATE_GAP = 0.0022
PAD_LEN_LONG = 0.785 * BELT_W                        # pad across belt   (photo: 0.79 x belt width)
PAD_LEN_SHORT = PAD_LEN_LONG - 2 * 0.0070            # trapezoid: ends cut back 7 mm
PAD_DEPTH = 0.088 * BELT_W                           # pad along belt    (photo: 0.09 x belt width)


def _belt_segments():
    """Closed belt path at the roller surfaces, traversed counter-clockwise in
    the x-z plane: bottom run, front roller, front-upper run, sprocket,
    rear-upper run, rear roller."""
    fr = (TRACK_X + AXLE_SPACING / 2, AXLE_Z); rr = (TRACK_X - AXLE_SPACING / 2, AXLE_Z)
    sp = (SPROCKET_X, SPROCKET_Z)
    cen = ((fr[0] + rr[0] + sp[0]) / 3, (fr[1] + rr[1] + sp[1]) / 3)
    circles = [(fr, ROLLER_R), (sp, SPROCKET_R), (rr, ROLLER_R)]
    runs = [_tangent(rr, ROLLER_R, fr, ROLLER_R, cen), _tangent(fr, ROLLER_R, sp, SPROCKET_R, cen),
            _tangent(sp, SPROCKET_R, rr, ROLLER_R, cen)]
    segs = []
    for k in range(3):
        p1, p2, _ = runs[k]
        segs.append(("line", p1, p2))
        (c, r) = circles[k]
        q_in = runs[k][1]; q_out = runs[(k + 1) % 3][0]
        a0 = math.atan2(q_in[1] - c[1], q_in[0] - c[0]); a1 = math.atan2(q_out[1] - c[1], q_out[0] - c[0])
        while a1 <= a0: a1 += 2 * math.pi
        segs.append(("arc", c, r, a0, a1))
    return segs


def _seg_len(sg, d):
    return math.hypot(sg[2][0] - sg[1][0], sg[2][1] - sg[1][1]) if sg[0] == "line" else (sg[2] + d) * (sg[4] - sg[3])


def _belt_at(segs, s, d, d_ref):
    """Point (x, z), unit tangent and outward normal at arc length s (measured at
    offset d_ref), offset d from the roller surface."""
    L = sum(_seg_len(g, d_ref) for g in segs); s %= L
    for g in segs:
        l = _seg_len(g, d_ref)
        if s <= l or g is segs[-1]:
            if g[0] == "line":
                (x0, z0), (x1, z1) = g[1], g[2]; t = s / l
                tx, tz = (x1 - x0) / l, (z1 - z0) / l; nx, nz = tz, -tx
                return (x0 + (x1 - x0) * t + nx * d, z0 + (z1 - z0) * t + nz * d), (tx, tz), (nx, nz)
            c, r, a0, a1 = g[1], g[2], g[3], g[4]; a = a0 + s / (r + d_ref)
            return (c[0] + (r + d) * math.cos(a), c[1] + (r + d) * math.sin(a)), (-math.sin(a), math.cos(a)), (math.cos(a), math.sin(a))
        s -= l


def _octagon_prism(cu, cv, w0, w1, r, frame):
    O, U, V, W = frame
    P = lambda u, v, w: tuple(O[i] + U[i] * u + V[i] * v + W[i] * w for i in range(3))
    ring = lambda w: [P(cu + r * math.cos(k * math.pi / 4), cv + r * math.sin(k * math.pi / 4), w) for k in range(8)]
    B, T = ring(w0), ring(w1); cb, ct = P(cu, cv, w0), P(cu, cv, w1); tris = []
    for k in range(8):
        j = (k + 1) % 8
        tris += [(B[k], B[j], T[j]), (B[k], T[j], T[k]), (cb, B[j], B[k]), (ct, T[k], T[j])]
    return tris


def _prism(poly_uv, w0, w1, frame):
    """Convex (u, v) polygon extruded along the belt normal from w0 to w1."""
    O, U, V, W = frame
    P = lambda u, v, w: tuple(O[i] + U[i] * u + V[i] * v + W[i] * w for i in range(3))
    B = [P(u, v, w0) for u, v in poly_uv]; T = [P(u, v, w1) for u, v in poly_uv]; tris = []
    for k in range(1, len(poly_uv) - 1):
        tris += [(B[0], B[k + 1], B[k]), (T[0], T[k], T[k + 1])]
    for k in range(len(poly_uv)):
        j = (k + 1) % len(poly_uv)
        tris += [(B[k], B[j], T[j]), (B[k], T[j], T[k])]
    return tris


_segs = _belt_segments()
D_REF = BAND_T + PLATE_T                               # links are spaced along the plate tops
_L = sum(_seg_len(g, D_REF) for g in _segs)
N_LINKS = round(_L / TREAD_PITCH_TARGET)
TREAD_PITCH = _L / N_LINKS                             # divides the loop exactly
# links are hidden only while fully under the shroud housing (checked per link)

def _tread_links(yc, s0):
    """Plates and pads for one side with every link shifted s0 along the belt
    path.  A link is left out only while it is entirely under the shroud
    housing, so links appear and vanish only where the housing hides them."""
    plates, pads, made = [], [], 0
    hu = (TREAD_PITCH - PLATE_GAP) / 2
    hd, hl, hs = PAD_DEPTH / 2, PAD_LEN_LONG / 2, PAD_LEN_SHORT / 2
    for k in range(N_LINKS):
        s_c = ((k + 0.5) * TREAD_PITCH + s0) % _L
        (px, pz), (tx, tz), (nx, nz) = _belt_at(_segs, s_c, 0.0, D_REF)
        top = BAND_T + PLATE_T + PAD_T
        ends = [(px + tx * e + nx * top, pz + tz * e + nz * top) for e in (-TREAD_PITCH / 2, TREAD_PITCH / 2)]
        if all(under_housing(ex, ez) for ex, ez in ends):
            continue                                   # fully under the shroud housing
        frame = ((px, yc, pz), (tx, 0.0, tz), (0.0, 1.0, 0.0), (nx, 0.0, nz))
        plates += _prism([(-hu, -BELT_W / 2), (hu, -BELT_W / 2), (hu, BELT_W / 2), (-hu, BELT_W / 2)],
                         BAND_T, BAND_T + PLATE_T, frame)
        # long edge trails (-u), short edge leads (+u): narrower at the top of the front run
        pads += _prism([(-hd, -hl), (hd, -hs), (hd, hs), (-hd, hl)], BAND_T + PLATE_T, BAND_T + PLATE_T + PAD_T, frame)
        made += 1
    return plates, pads, made


# Animation frames: the pattern repeats every link, so TREAD_PHASES copies
# shifted by a fraction of one pitch make a seamless loop.  Frame p is the belt
# moved p * TREAD_PHASE_STEP along the path; frame 0 is the static mesh.
TREAD_PHASES = 12
TREAD_PHASE_STEP = TREAD_PITCH / TREAD_PHASES
TREAD_FRAME_DIR = os.path.join(MESH_DIR, "Treads")

for side, sy in (("left", 1), ("right", -1)):
    yc = sy * TRACK_Y
    y0, y1 = yc - BELT_W / 2, yc + BELT_W / 2
    # continuous band, swept round the whole loop (fine steps on the arcs); it
    # looks the same at every phase, so it has no animation frames
    band = []
    n_band = 360
    pts = [_belt_at(_segs, _L * i / n_band, 0.0, D_REF) for i in range(n_band)]
    outs = [_belt_at(_segs, _L * i / n_band, BAND_T, D_REF) for i in range(n_band)]
    for i in range(n_band):
        j = (i + 1) % n_band
        (a0, _, _), (a1, _, _) = pts[i], pts[j]; (b0, _, _), (b1, _, _) = outs[i], outs[j]
        A = [(a0[0], y0, a0[1]), (a1[0], y0, a1[1]), (a1[0], y1, a1[1]), (a0[0], y1, a0[1])]
        B = [(b0[0], y0, b0[1]), (b1[0], y0, b1[1]), (b1[0], y1, b1[1]), (b0[0], y1, b0[1])]
        band += [(B[0], B[2], B[1]), (B[0], B[3], B[2]),          # outer face
                 (A[0], A[1], A[2]), (A[0], A[2], A[3]),          # inner face
                 (A[0], B[1], A[1]), (A[0], B[0], B[1]),          # side y0
                 (A[3], A[2], B[2]), (A[3], B[2], B[3])]          # side y1
    mesh_part(f"tread_band_{side}", (0.0, 0.0, 0.0), band, "gray")
    plates, pads, made = _tread_links(yc, 0.0)
    mesh_part(f"tread_plates_{side}", (0.0, 0.0, 0.0), plates, "light")
    mesh_part(f"tread_pads_{side}", (0.0, 0.0, 0.0), pads, "black")
    for ph in range(TREAD_PHASES):
        fp, fd, _ = _tread_links(yc, ph * TREAD_PHASE_STEP)
        _write_stl(os.path.join(TREAD_FRAME_DIR, f"tread_plates_{side}_p{ph:02d}.stl"), fp)
        _write_stl(os.path.join(TREAD_FRAME_DIR, f"tread_pads_{side}_p{ph:02d}.stl"), fd)

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
ARM_W = 2 * (INNER_Y - SHROUD_T) - 0.003                            # full width between the shrouds
HEAD_Z = -0.822

arm_a = (DRUM[0] - DRUM_R + 0.005, 0, DRUM[2])   # centred on the back of the drum
arm_b = (CASTER_X, 0, HEAD_Z)
strut("caster_arm", arm_a, arm_b, 0.070, ARM_W, "gray")
strut("caster_arm_rib", (arm_a[0] - 0.020, 0, arm_a[2] + 0.040), (arm_b[0] + 0.030, 0, arm_b[2] + 0.040),
      0.018, ARM_W * 0.65, "dark")
box("caster_head", (CASTER_X, 0, HEAD_Z - 0.006), (0.100, ARM_W, 0.050), "dark")
bellows("caster_spring", (CASTER_X, 0, HEAD_Z - 0.031), (CASTER_X, 0, -0.920), 0.034, 0.027, 4, "black")
YOKE_W = 2 * (WHEEL_Y + WHEEL_W / 2) + 0.044
box("caster_yoke", (CASTER_X, 0, -0.935), (0.085, YOKE_W, 0.030), "dark")
for sd, fy in (("l", WHEEL_Y + WHEEL_W / 2 + 0.015), ("c", 0.0), ("r", -(WHEEL_Y + WHEEL_W / 2 + 0.015))):
    box(f"caster_fork_{sd}", (CASTER_X, fy, -0.978), (0.050, 0.010, 0.085), "gray")
rod("caster_axle", (CASTER_X, -(YOKE_W / 2), CASTER_Z), (CASTER_X, YOKE_W / 2, CASTER_Z), 0.010, "light")
for sd, sy in (("l", 1), ("r", -1)):
    cyl(f"caster_wheel_{sd}", (CASTER_X, sy * WHEEL_Y, CASTER_Z), CASTER_R, WHEEL_W, "black", axis="y")
    cyl(f"caster_hub_{sd}", (CASTER_X, sy * WHEEL_Y, CASTER_Z), 0.034, WHEEL_W + 0.008, "gray", axis="y")
    # large domed hub cap on each wheel's outer face, as in the photo
    fy = sy * (WHEEL_Y + WHEEL_W / 2)
    cyl(f"caster_hubcap_{sd}", (CASTER_X, fy + sy * 0.003, CASTER_Z), 0.058, 0.006, "gray", axis="y")
    cyl(f"caster_hubcap_rim_{sd}", (CASTER_X, fy + sy * 0.0065, CASTER_Z), 0.046, 0.002, "light", axis="y")
    cyl(f"caster_hubcap_bolt_{sd}", (CASTER_X, fy + sy * 0.009, CASTER_Z), 0.011, 0.004, "dark", axis="y")

# --- rear housing on the caster arm (from the side photograph) --------------
# A tall sculpted body over the arm: sloped front that stays clear of the
# tilted waist column, flat top with a recessed panel and a grab handle,
# blue-grey side panels, a rounded rear end, and a plate over the caster spring.
HOUSING_W = ARM_W - 0.004
h_x0, h_x1 = -0.175, -0.395                     # front and rear of the flat top
h_bot, h_top = -0.785, -0.690
box("rear_housing", ((h_x0 + h_x1) / 2, 0, (h_bot + h_top) / 2), (h_x0 - h_x1, HOUSING_W, h_top - h_bot), "gray")
strut("rear_housing_slope", (h_x0 + 0.050, 0, h_bot + 0.006), (h_x0 - 0.004, 0, h_top - 0.004), 0.030, HOUSING_W, "gray")
cyl("rear_housing_tail", (h_x1, 0, (h_bot + h_top) / 2), (h_top - h_bot) / 2, HOUSING_W, "gray", axis="y")
box("rear_housing_top_panel", ((h_x0 + h_x1) / 2 - 0.010, 0, h_top + 0.001), (0.150, HOUSING_W * 0.70, 0.004), "dark")
for sd, sy in (("l", 1), ("r", -1)):
    box(f"rear_housing_side_{sd}", ((h_x0 + h_x1) / 2 + 0.015, sy * (HOUSING_W / 2 + 0.001), (h_bot + h_top) / 2 + 0.008),
        (0.120, 0.003, 0.050), "blue")
    box(f"rear_housing_side_line_{sd}", ((h_x0 + h_x1) / 2 - 0.060, sy * (HOUSING_W / 2 + 0.001), (h_bot + h_top) / 2),
        (0.004, 0.004, h_top - h_bot - 0.010), "dark")
    box(f"rear_handle_post_{sd}", (h_x0 - 0.030 if sd == "l" else h_x0 - 0.140, 0, h_top + 0.018),
        (0.014, 0.020, 0.036), "dark")
rod("rear_handle", (h_x0 - 0.022, 0, h_top + 0.036), (h_x0 - 0.148, 0, h_top + 0.036), 0.010, "black")
box("caster_top_plate", (CASTER_X, 0, HEAD_Z + 0.023), (0.130, YOKE_W + 0.020, 0.010), "light")

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

# ===================================================== TREAD ANIMATION MANIFEST
def write_tread_manifest():
    """Describe the tread animation frames for the editor (see REQ-tread-animation.md)."""
    import json
    def mat_of(key): return MAT[key]
    hub_parts = {}
    for n, c, rpy, g, m in PARTS:
        for side in ("left", "right"):
            if n.startswith("track_front_hub") and n.endswith(side) or n.startswith(f"track_front_hub_bolt_{side}_"):
                hub_parts.setdefault(side, []).append(f"body_{n}_link")
    front = dict((n, c) for n, c, rpy, g, m in PARTS)
    step_deg = -math.degrees(TREAD_PHASE_STEP / ROLLER_R)
    sides = {}
    for side in ("left", "right"):
        hc = front[f"track_front_hub_{side}"]
        sides[side] = {
            "layers": [
                {"urdfLink": f"body_tread_plates_{side}_link", "urdfVisual": f"body_tread_plates_{side}_visual",
                 "material": mat_of("light"),
                 "frames": [f"Meshes/Body/Treads/tread_plates_{side}_p{p:02d}.stl" for p in range(TREAD_PHASES)]},
                {"urdfLink": f"body_tread_pads_{side}_link", "urdfVisual": f"body_tread_pads_{side}_visual",
                 "material": mat_of("black"),
                 "frames": [f"Meshes/Body/Treads/tread_pads_{side}_p{p:02d}.stl" for p in range(TREAD_PHASES)]},
            ],
            "rotators": [{
                "name": f"front_roller_hub_{side}",
                "urdfLinks": sorted(hub_parts.get(side, [])),
                "axisWorld": [0.0, 1.0, 0.0],
                "centreWorld": [round(hc[0], 6), round(hc[1], 6), round(hc[2], 6)],
                "radius_m": ROLLER_R,
                "degreesPerPhase": round(step_deg, 6),
            }],
        }
    manifest = {
        "version": 1,
        "description": "Johnny 5 body tread animation frames",
        "phases": TREAD_PHASES,
        "linkPitch_m": round(TREAD_PITCH, 6),
        "phaseStep_m": round(TREAD_PHASE_STEP, 6),
        "linksInLoop": N_LINKS,
        "frameSpace": "Each frame mesh uses the same local frame as the static visual it replaces "
                      "(the URDF link's joint origin). Swap the visual's geometry; leave the link transform alone.",
        "direction": "Showing frames in increasing order moves every link along the belt path: forward along "
                     "the bottom run and back along the top. Relative to the ground that drives the robot "
                     "BACKWARD. Show frames in decreasing order to drive FORWARD. Rotators turn by "
                     "degreesPerPhase about axisWorld per increasing frame (negative = clockwise seen from +Y).",
        "speed": "Belt surface speed = phaseStep_m x frames per second. Frame 0 equals the static mesh.",
        "sides": sides,
    }
    path = os.path.join(TREAD_FRAME_DIR, "tread_animation.json")
    os.makedirs(TREAD_FRAME_DIR, exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2)
    return manifest


TREAD_MANIFEST = write_tread_manifest()


# ================================================================ URDF out
def _fmt(v):
    return f"{v:.6f}"


def _geom(g):
    if g[0] == "mesh":
        return f'<mesh filename="{g[1]}" scale="1 1 1" />'
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
    lines = ["  <!-- ==== Johnny 5 body v22 - posed, shoulder housings, below the neck only ==== -->"]
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
