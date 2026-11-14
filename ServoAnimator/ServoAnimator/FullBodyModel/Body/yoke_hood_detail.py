"""Photo-based chest yoke hood. Coordinates use the generator's upright chest frame.
No build_robot import: this helper can regenerate these two assets independently.
"""

def hood_meshes(bottom, top):
    bx0, bx1, by0, by1, bz = bottom
    tx0, tx1, ty0, ty1, tz = top
    height = tz - bz
    # Octagonal sections create broad cheek planes and narrow corner bevels.
    def ring(z, back, front, half_width, bevel):
        b = min(bevel, (front-back)/3, half_width/3)
        return [(back+b,-half_width,z), (front-b,-half_width,z),
                (front,-half_width+b,z), (front,half_width-b,z),
                (front-b,half_width,z), (back+b,half_width,z),
                (back,half_width-b,z), (back,-half_width+b,z)]
    def loft(rings):
        triangles=[]
        for lower, upper in zip(rings,rings[1:]):
            for i in range(8):
                j=(i+1)%8
                triangles.extend([(lower[i],lower[j],upper[j]),(lower[i],upper[j],upper[i])])
        for points, reverse in ((rings[0],True),(rings[-1],False)):
            center=tuple(sum(p[k] for p in points)/8 for k in range(3))
            for i in range(8):
                a,b=points[i],points[(i+1)%8]
                triangles.append((center,b,a) if reverse else (center,a,b))
        return triangles
    half=(by1-by0)/2
    narrow=(ty1-ty0)/2
    # The photo has a raised narrow brow, a sloping face, and widening lower cheeks.
    # Short end sections soften the top/bottom edges without expensive curved meshes.
    sections=[
        ring(bz+.0015,bx0+.002,bx1-.002,half-.003,.004),
        ring(bz+.0045,bx0,bx1,half,.004),
        ring(bz+height*.44,bx0+(tx0-bx0)*.44,bx1+.002,half*.81,.005),
        ring(tz-.004,tx0,tx1,narrow*.88,.004),
        ring(tz,tx0+.002,tx1-.002,narrow*.88-.002,.003)
    ]
    shell=loft(sections)
    # A recessed, exposed gasket avoids coplanar dark/light faces at the chest seam.
    gasket=loft([ring(bz,bx0+.003,bx1-.003,half-.004,.004),
                 ring(bz+.002,bx0+.003,bx1-.003,half-.004,.004)])
    return shell,gasket
