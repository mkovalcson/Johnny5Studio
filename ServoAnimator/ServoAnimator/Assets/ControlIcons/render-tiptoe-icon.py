"""Draws B_TipToe.png to match BodyUrdfSource/B_TipToe.svg (48 px, green outline, 18% fill), 8x supersampled."""
import sys, math
from PIL import Image, ImageDraw
S = 8; W = 48*S; green = (0x70, 0xdf, 0x9b)
img = Image.new('RGBA', (W, W), (0, 0, 0, 0)); fill = Image.new('RGBA', (W, W), (0, 0, 0, 0))
d = ImageDraw.Draw(img); f = ImageDraw.Draw(fill)
def P(x, y): return (x*S, y*S)
def line(points, width=2.3):
    pts = [P(*p) for p in points]; d.line(pts, fill=green+(255,), width=int(width*S), joint='curve')
    for p in (pts[0], pts[-1]): r = width*S/2; d.ellipse([p[0]-r, p[1]-r, p[0]+r, p[1]+r], fill=green+(255,))
# Track: a capsule from the drum pivot (19,15) down to the front roller near the floor (35,35), 45 degrees nose-down.
a, b, r = (19, 15), (35, 35), 6
dx, dy = b[0]-a[0], b[1]-a[1]; L = math.hypot(dx, dy); nx, ny = -dy/L, dx/L
outline = []
for k in range(0, 181, 10):
    t = math.radians(k); c, s = math.cos(t), math.sin(t)
    outline.append((b[0] + r*(nx*c - (-dx/L)*0) , 0))
outline = []
for k in range(0, 181, 10):  # front cap
    t = math.radians(-90 + k); ux, uy = dx/L, dy/L
    outline.append((b[0] + r*(ux*math.cos(t) - uy*math.sin(t)), b[1] + r*(uy*math.cos(t) + ux*math.sin(t))))
for k in range(0, 181, 10):  # rear cap
    t = math.radians(90 + k); ux, uy = dx/L, dy/L
    outline.append((a[0] + r*(ux*math.cos(t) - uy*math.sin(t)), a[1] + r*(uy*math.cos(t) + ux*math.sin(t))))
poly = [P(*p) for p in outline]
f.polygon(poly, fill=green+(46,))
line(outline + [outline[0]], 2.1)
# Drum pivot
cx, cy, cr = 19, 15, 3
f.ellipse([P(cx-cr, cy-cr), P(cx+cr, cy+cr)], fill=green+(46,)); d.ellipse([P(cx-cr, cy-cr), P(cx+cr, cy+cr)], outline=green+(255,), width=int(2.1*S))
# Floor
line([(6, 43), (42, 43)])
# Rise arrow
line([(8, 31), (8, 12)]); line([(4, 16), (8, 12), (12, 16)])
# Rotation arrow: clockwise arc around the drum pivot, ahead of the track (nose turning down)
R=17;angles=list(range(-38,9,4))
arc=[(19+R*math.cos(math.radians(t)),15+R*math.sin(math.radians(t))) for t in angles]
line(arc);ex,ey=arc[-1];t=math.radians(angles[-1]);tx,ty=-math.sin(t),math.cos(t)  # tangent (clockwise in screen space)
nx,ny=-ty,tx;h=3.4
line([(ex-tx*h+nx*h*.75,ey-ty*h+ny*h*.75),(ex,ey),(ex-tx*h-nx*h*.75,ey-ty*h-ny*h*.75)])
out = Image.alpha_composite(fill, img).resize((48, 48), Image.LANCZOS)
out.save(sys.argv[1]); print(sys.argv[1])
