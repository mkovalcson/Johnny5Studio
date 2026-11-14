"""Photo-proportioned stationary caster support details; dimensions in metres."""

def add_caster_arm_details(collar, top, width, cyl, rod, strut, box, panel):
    x,y,z=collar
    # Blue-arrow correction: the arm housing drops to the brace attachment.
    # Keep the brace endpoint inside this housing, not forward into the shroud.
    support_x=x+.165
    support_bottom=z-.048
    support_top=top-.012
    box("caster_detail_lower_arm_body",
        (support_x,y,(support_top+support_bottom)/2),
        (.080,width-.008,support_top-support_bottom),"gray")
    rod("caster_detail_lower_arm_edge",
        (support_x,y-width/2+.009,support_bottom+.007),
        (support_x,y+width/2-.009,support_bottom+.007),.009,"gray")
    # Purple: forked lower brace anchored directly to the stationary collar.
    for side,sign in (("left",1),("right",-1)):
        a=(x,y+sign*.032,z)
        b=(support_x,y+sign*(width/2-.012),support_bottom+.012)
        strut(f"caster_detail_collar_brace_{side}",a,b,.022,.014,"gray")
        cyl(f"caster_detail_collar_pin_{side}",(x,y+sign*.042,z),.012,.008,"light",axis="y")
        # Blue: deeper side cheek with an inset panel and lower rolled edge.
        sy=y+sign*(width/2+.005)
        box(f"caster_detail_side_cheek_{side}",(x+.245,sy,top-.072),(.095,.012,.154),"gray")
        box(f"caster_detail_side_panel_{side}",(x+.245,sy+sign*.007,top-.063),(.070,.003,.109),"light")
        rod(f"caster_detail_side_bottom_{side}",(x+.201,sy,top-.146),(x+.289,sy,top-.146),.008,"gray")
        for dz in (.0,-.128):
            cyl(f"caster_detail_side_screw_{side}_{int(-dz*1000)}",(x+.275,sy+sign*.008,top+dz),.003,.002,"dark",axis="y")
    # Green: long black hydraulic body, exposed shaft, gland and mounting eye.
    py=y;hz=top+.031
    rod("caster_detail_hydraulic_body",(x+.115,py,hz),(x+.280,py,hz+.012),.016,"black")
    rod("caster_detail_hydraulic_gland",(x+.270,py,hz+.011),(x+.286,py,hz+.012),.020,"dark")
    # Continue the shaft's slope to the waist-column rear surface, with a
    # 2 mm overlap so the curved surface cannot leave a visible end gap.
    rod("caster_detail_hydraulic_shaft",(x+.286,py,hz+.012),(x+.412,py,hz+.02151),.008,"light")
    # Mounting eye and pin are 25 mm forward of the transverse hinge.
    cyl("caster_detail_hydraulic_eye",(x+.150,py,hz),.023,.030,"dark",axis="y")
    cyl("caster_detail_hydraulic_pin",(x+.150,py,hz),.007,.034,"light",axis="y")
    # Yellow: stacked on the same centreline, with its rear end in the shroud.
    py=y;hz=top+.083
    rod("caster_detail_upper_cylinder",(x+.125,py,hz),(x+.255,py,hz),.023,"black")
    for label,px in (("rear",x+.121),("front",x+.260)):
        cyl("caster_detail_upper_cylinder_"+label,(px,py,hz),.025,.007,"dark",axis="x")
    # Open-front tapered housing: both cylinder rear mounts are enclosed,
    # while the black bodies and shaft remain visible between the cheeks.
    box("caster_detail_cylinder_saddle",(x+.106,py,top+.052),(.020,.088,.116),"gray")
    profile=[(x+.096,top-.006),(x+.270,top-.006),
             (x+.270,top+.016),(x+.116,top+.110),(x+.096,top+.110)]
    for side,sign in (("left",1),("right",-1)):
        ya=y+sign*.038;yb=y+sign*.046
        panel(f"caster_detail_cylinder_shroud_{side}",profile,min(ya,yb),max(ya,yb),"gray")
        rod(f"caster_detail_cylinder_shroud_lip_{side}",
            (x+.116,y+sign*.042,top+.110),
            (x+.270,y+sign*.042,top+.016),.004,"gray")
    # Red: transverse hinge knuckles with a dark seam and recessed top plates.
    hx=x+.125
    box("caster_detail_hinge_seam",(hx,y,top+.004),(.014,width*.80,.004),"shadow")
    for i in range(5):
        cyl(f"caster_detail_hinge_knuckle_{i}",(hx,y+(i-2)*.026,top+.011),.011,.023,"gray",axis="y")
    cyl("caster_detail_hinge_pin",(hx,y,top+.011),.004,.143,"dark",axis="y")
    box("caster_detail_hinge_rear_leaf",(hx-.025,y,top+.004),(.031,width*.66,.006),"light")
    box("caster_detail_hinge_front_leaf",(hx+.025,y,top+.004),(.031,width*.66,.006),"gray")
