"""Outboard hub and fixed diagonal support, matched to the clearer side photo.
Metres, scaled from the existing 172 mm roller. Depth remains estimated.
"""
import math

def add_outer_track_details(side, sign, front, track_y, belt_width, cyl, strut, panel):
    x,z=front
    outer=track_y + sign*belt_width/2
    face=outer-sign*.002
    prefix=f"track_outer_guard_{side}"
    # Leave the hub exposed around a narrow diagonal arm. Hub details rotate;
    # the arm, axle end, ribs, and frame rail stay fixed.
    for name,radius,depth,centre,material in [
        ("rim",.081,.006,-.018,"gray"),
        ("recess",.077,.002,-.014,"shadow"),
        ("face",.073,.002,-.012,"dark")]:
        cyl(f"track_front_hub_detail_{name}_{side}",(x,outer+sign*centre,z),radius,depth,material,axis="y")
    for i in range(10):
        a=2*math.pi*i/10
        p=(x+.059*math.cos(a),outer-sign*.010,z+.059*math.sin(a))
        q=(x+.070*math.cos(a),outer-sign*.010,z+.070*math.sin(a))
        strut(f"track_front_hub_detail_lug_{i}_{side}",p,q,.009,.003,"gray")
    # Flat support: its face is 2 mm inside the belt edge; raised panels
    # still clear the shroud's inner face by 1 mm. No rearward depth taper.
    def offset(px): return 0.0
    def plate(name,coords,back,front,mat):
        panel(prefix+name,[(x+px,z+pz) for px,pz in coords],face+sign*back,face+sign*front,mat,offset)
    # Axis runs rearward/upward; rounded nose wraps the flush axle end.
    u=(-.848,.530); n=(.530,.848); w=.024; length=.275
    outline=[]
    for deg in range(90,271,10):
        a=math.radians(deg);outline.append((w*(u[0]*math.cos(a)+n[0]*math.sin(a)),w*(u[1]*math.cos(a)+n[1]*math.sin(a))))
    outline += [(length*u[0]-w*n[0],length*u[1]-w*n[1]),(length*u[0]+w*n[0],length*u[1]+w*n[1])]
    plate("_arm",outline,-.005,0,"gray")
    # Flush metal axle end and a shallow dark socket; no protruding axle cap.
    cyl(prefix+"_axle_end",(x,face-sign*.0015,z),.010,.003,"light",axis="y")
    cyl(prefix+"_axle_socket",(x,face+sign*.00015,z),.0055,.0003,"shadow",axis="y")
    # Stepped casting below the arm: the angular cutout and three raised panels.
    plate("_web",[(-.068,-.025),(-.098,.040),(-.218,.132),(-.238,.072),(-.188,.019),(-.205,-.006),(-.147,-.013),(-.169,-.049),(-.079,-.049)],-.012,-.007,"gray")
    for i,(start,end) in enumerate(((.079,.139),(.146,.204),(.211,.262))):
        coords=[(d*u[0]+v*n[0],d*u[1]+v*n[1]) for d,v in ((start,-.020),(end,-.020),(end,.020),(start,.020))]
        plate(f"_raised_panel_{i}",coords,0,.0025,"light")
    # Narrow upper reinforcing lip, and the long boxed rail just above treads.
    def rib(name,a,b,width,depth,mat):
        aa=(x+a[0],face+sign*a[2]+offset(x+a[0]),z+a[1])
        bb=(x+b[0],face+sign*b[2]+offset(x+b[0]),z+b[1])
        strut(prefix+name,aa,bb,width,depth,mat)
    rib("_upper_rib",(.025,.066,-.004),(-.213,.153,-.004),.006,.005,"gray")
    rib("_lower_rail",(-.036,-.041,-.003),(-.425,-.041,-.003),.018,.010,"dark")
    rib("_rail_highlight",(-.040,-.031,.0025),(-.420,-.031,.0025),.002,.001,"light")
    # Small round joint on the arm and recessed screw in the lower web.
    for name,dx,dz,rad in (("arm_bolt",-.080,.050,.007),("web_screw",-.164,.003,.004)):
        y=face+offset(x+dx)
        if name=="web_screw":y-=sign*.006
        cyl(prefix+"_"+name,(x+dx,y+sign*.001,z+dz),rad,.002,"light",axis="y")
    # The rear-tab hole shown in the earlier photo remains hidden by the shroud.
