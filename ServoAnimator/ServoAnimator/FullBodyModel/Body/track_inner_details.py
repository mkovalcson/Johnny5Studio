"""Photo-scaled inboard track covers. Metres; X forward, Y left, Z up.
Depth is estimated from a single oblique photograph, not a measured drawing.
Fixed bearing housings deliberately remain separate from rotating tread hubs.
"""
import math


def add_inner_track_details(side, sign, front, track_y, belt_width, cyl, strut):
    inner = track_y - sign * belt_width / 2
    # Green arrows: broad lower axle covers. Red arrows: raised intermediate
    # bearing covers. The connecting frame lip is indicated by the yellow arrow.
    covers = (("front", front[0], front[1], .074, .032),
              ("middle", front[0] - .155, front[1] + .085, .059, .038))
    for label, x, z, radius, depth in covers:
        prefix = f"track_inner_{label}_{side}"
        cyl(prefix + "_seat", (x, inner - sign * .003, z), radius + .004, .006, "dark", axis="y")
        cyl(prefix + "_cover", (x, inner - sign * (.006 + depth / 2), z), radius, depth, "gray", axis="y")
        # Stepped, shallow end plate and axle cap retain the photo's silhouette.
        cyl(prefix + "_face", (x, inner - sign * (.006 + depth + .0015), z), radius - .006, .003, "gray", axis="y")
        cyl(prefix + "_cap", (x, inner - sign * (.006 + depth + .005), z), radius * .28, .004, "light", axis="y")
    a = (front[0] - .015, inner - sign * .008, front[1] + .035)
    b = (front[0] - .195, inner - sign * .008, front[1] + .145)
    strut(f"track_inner_frame_web_{side}", a, b, .052, .010, "gray")
    # A narrow darker seam gives the raised edge separation without coplanar faces.
    a2 = (a[0], inner - sign * .014, a[2] + .023)
    b2 = (b[0], inner - sign * .014, b[2] + .023)
    strut(f"track_inner_frame_seam_{side}", a2, b2, .006, .003, "dark")
    a3 = (a2[0], inner - sign * .017, a2[2] + .005)
    b3 = (b2[0], inner - sign * .017, b2[2] + .005)
    strut(f"track_inner_frame_lip_{side}", a3, b3, .010, .006, "light")
