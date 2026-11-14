using System.Numerics;
using Johnny5.VrBridge;

namespace Johnny5VR;

internal static class CurvedCasterChecks
{
    internal static void Run(ViewerSettings settings,Action<bool,string> check)
    {
        var flat=Shapes.Cylinder(.02f,.04f).Select(t=>Shapes.Tri(t.A,t.B,t.C)).ToArray();
        var smooth=CurvedSurfaces.Smooth(flat);
        check(flat.Zip(smooth).All(p=>p.First.A==p.Second.A&&p.First.B==p.Second.B&&p.First.C==p.Second.C),"Curved shading retains every original vertex and triangle.");
        var radial=Vector3.Normalize(new Vector3(smooth[0].A.X,smooth[0].A.Y,0));
        check(Vector3.Dot(smooth[0].NA,radial)>.9999&&smooth[0].NA!=flat[0].NA,"Curved shading replaces cylinder facets with smooth radial normals.");
        check(smooth[2].NA.Z<-.9999&&smooth[3].NA.Z>.9999,"Cylinder cap creases remain sharp after smoothing.");
        check(CurvedSurfaces.Smooth([new(Vector3.Zero,Vector3.Zero,Vector3.Zero,Vector3.UnitZ,Vector3.UnitZ,Vector3.UnitZ)])[0].NA==Vector3.UnitZ,"Collapsed CAD triangles retain finite normals.");
        foreach(string part in new[]{"Meshes/Body/shoulder_cad_sleeve_left.stl","Meshes/Body/shoulder_cad_sleeve_right.stl","Meshes/NeckAssembly/neck_bellows_black.stl","Meshes/SimplifiedHead2/vent_left_fin1.stl","Meshes/SimplifiedHead2/eye_tube_left.stl"})
            check(CurvedSurfaces.IsRoundedHeadSurface(part),"Editor curved-surface selection includes "+part);
        check(!CurvedSurfaces.IsRoundedHeadSurface("Meshes/Body/track_shroud_left.stl")&&!CurvedSurfaces.IsRoundedHeadSurface("Meshes/SimplifiedHead2/vent_left_fin1_collision_full.stl"),"Unselected parts and collision meshes are not smoothed.");
        var straight=CasterWheelTravel.Step(0,0,.2,0,.5,.034875);
        check(Math.Abs(straight.Left-.2)<1e-12&&straight.Left==straight.Right,"Straight caster wheels roll their actual forward distance.");
        var reverse=CasterWheelTravel.Step(0,0,-.2,0,.5,.034875);
        check(reverse.Left<0&&reverse.Left==reverse.Right,"Reversing reverses wheel rotation before the caster swings around.");
        var turn=CasterWheelTravel.Step(-90,-90,0,10,.5,.034875);
        check(turn.Right>turn.Left&&turn.Left>0,"Inside and outside caster wheels roll different distances during turns.");
        check(CasterWheelTravel.Step(179,-179,0,0,.5,.034875).Left>-.002,"Caster angle wraparound does not create a full wheel-rotation jump.");
        var rig=new AnimationRig(settings.Resolve("Models/Robots/Johnny5FullBody/johnny5_head.urdf"),settings.ConfigFolder);
        rig.Apply(new());var yoke=rig.Palette[rig.Bone("body_caster_yoke_link")];
        var centre=rig.RestPoint("body_caster_wheel_l_link",Vector3.Zero);
        rig.Apply(new(),new(0,0,0,0,0,0,.04,.02));
        var wheel=rig.Palette[rig.Bone("body_caster_wheel_l_link")];
        check(Vector3.Distance(Vector3.Transform(centre,wheel),centre)<1e-6,"Caster rolling keeps the wheel centred on its axle.");
        var lug=rig.Palette[rig.Bone("body_caster_wheel_l_lug_000_link")];
        check(wheel!=Matrix4x4.Identity&&Vector3.Distance(Vector3.Transform(Vector3.One,lug),Vector3.Transform(Vector3.One,wheel))<1e-5,"Caster tire, tread lugs and wheel assembly rotate together.");
        check(rig.Palette[rig.Bone("body_caster_yoke_link")]==yoke&&rig.Palette[rig.Bone("body_caster_axle_link")]==Matrix4x4.Identity,"Rolling does not rotate the stationary fork or axle.");
        var seq=new PlaybackSequence("","",2,[new("TrackForward","",[new(0,20,false),new(1,0,false)],false)],[],false,"",[]);
        var path=new SecondaryMotionPath(seq,new(),rig.CasterRadius,rig.CasterTrail);
        var end=path.At(1);
        check(Math.Abs(end.CasterLeft-.2)<1e-9&&end.CasterLeft==end.CasterRight&&path.At(2)==end,"Timeline caster roll follows travel and stops without drifting.");
        check(path.At(.25)==new SecondaryMotionPath(seq,new(),rig.CasterRadius,rig.CasterTrail).At(.25),"Seeking restores caster wheel rotation deterministically.");
    }
}
