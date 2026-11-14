using System.Numerics;
using ServoAnimator;

namespace Johnny5VR;

// TipToe (B_TipToe): catalog entry, shared geometry and the standalone playback rig.
internal static class TipToeChecks
{
    internal static void Run(ViewerSettings settings,Action<bool,string> check)
    {
        var definition=BodyControlCatalog.Find(ServoNames.B_TipToe);
        check(definition!=null&&BodyControlCatalog.IsBody(ServoNames.B_TipToe)&&definition.Joint=="TipToe"&&definition.MinRadians==0&&Math.Abs(definition.MaxRadians-TipToeGeometry.MaximumRadians)<1e-12&&Math.Abs(definition.MaxRadians*180/Math.PI-61.27)<1e-9,"TipToe is a body control from 0 (tracks flat) to 61.27 degrees (front-top tread vertical to the body).");
        check(ServoCommand.IsUrdfOnly(ServoNames.B_TipToe)&&BodyControlCatalog.PairedPicklistControls.Any(c=>c.Servo==ServoNames.B_TipToe)&&ControlIconFiles.For(ServoNames.B_TipToe)=="B_TipToe.png","TipToe is URDF-only, appears in body picklists and has an icon.");
        var setting=BodyMotionSetting.Default(definition!);
        check(setting.Map(0,definition!)==0&&Math.Abs(setting.Map(100,definition!)-TipToeGeometry.MaximumRadians)<1e-12&&setting.Map(-100,definition!)==0&&Math.Abs(setting.Map(50,definition!)-TipToeGeometry.MaximumRadians/2)<1e-12,"TipToe commands map 0→flat, 50→30.6°, 100→61.3°, and negative values stay flat.");
        var saved=new UrdfConfiguration();saved.BodyMotions["TipToe"]=new(){MinDegrees=0,MaxDegrees=45};var custom=new UrdfConfiguration();custom.BodyMotions["TipToe"]=new(){MinDegrees=0,MaxDegrees=30};
        check(Math.Abs(saved.BodySetting(definition!).MaxDegrees-61.27)<1e-9&&custom.BodySetting(definition!).MaxDegrees==30,"A saved stock 45° TipToe stop is raised to 61.27°; a custom stop is kept.");
        string urdf=settings.Resolve("Models/Robots/Johnny5FullBody/johnny5_head.urdf");
        var geometry=TipToeGeometry.FromUrdf(urdf);
        check(geometry!=null,"The full-body model's track, shroud and caster geometry builds the TipToe table.");
        var full=geometry!.At(TipToeGeometry.MaximumRadians);var half=geometry.At(TipToeGeometry.MaximumRadians/2);
        check(geometry.At(0)==default,"At rest TipToe changes nothing.");
        float Deg(float r)=>r*180/MathF.PI;
        check(geometry.UpperBraceLength>.195f&&geometry.UpperBraceLength<.21f&&geometry.LowerBraceLength>.215f&&geometry.LowerBraceLength<.23f,$"Caster four-bar from the URDF hinge markers: upper brace {geometry.UpperBraceLength*1000:F0} mm, lower brace {geometry.LowerBraceLength*1000:F0} mm (CAD 202 / 223).");
        check(MathF.Abs(Deg(full.UpperBrace)-41.7f)<.05f&&Deg(full.LowerBrace)>35&&Deg(full.LowerBrace)<39&&Deg(full.ColumnLean)>4&&Deg(full.ColumnLean)<8,$"Fully on tiptoe the upper brace swings {Deg(full.UpperBrace):F1}°, the lower brace {Deg(full.LowerBrace):F1}° and the column leans {Deg(full.ColumnLean):F1}° forward (proposal: 41.7 / 36.9 / 5.8).");
        check(Deg(full.Pitch)>4.5f&&Deg(full.Pitch)<7&&full.Lift>.18f&&full.Lift<.215f,$"The robot tips back {Deg(full.Pitch):F1}° onto the caster wheel and the body rises {full.Lift*100:F1} cm (proposal: 5.7°, 19.8 cm); the front tread ends at {90-Deg(full.Pitch):F1}° to the floor.");
        check(full.Forward>.26f&&full.Forward<.31f&&full.CasterRoll>.25f&&full.CasterRoll<.45f,$"With the front planted the body moves forward {full.Forward*100:F1} cm and the caster wheels roll {full.CasterRoll*100:F1} cm.");
        check(half.Lift>0&&half.Lift<full.Lift&&half.UpperBrace>0&&half.UpperBrace<full.UpperBrace&&half.Pitch>0&&half.Pitch<=full.Pitch+1e-4f,"Lift, brace swing and tip-back grow smoothly between rest and full tiptoe.");
        check(geometry.At(Math.PI)==full&&geometry.At(-1)==default,"TipToe angles are clamped to 0..61.27 degrees.");
        check(TipToeGeometry.IsTrackLink("body_shroud_strip_line_left_0_link")&&TipToeGeometry.IsTrackLink("body_tread_band_right_link")&&TipToeGeometry.IsCasterLink("body_caster_wheel_l_link")&&!TipToeGeometry.IsTrackLink("body_pelvis_drum_link"),"Shroud, tread and track parts rotate with the tracks; the pelvis drum does not.");
        check(TipToeGeometry.PartOf("body_caster_head_link")==TipToeGeometry.Part.UpperBrace&&TipToeGeometry.PartOf("body_caster_detail_collar_brace_left_link")==TipToeGeometry.Part.LowerBrace&&TipToeGeometry.PartOf("body_caster_wheel_l_lug_003_link")==TipToeGeometry.Part.Column&&TipToeGeometry.PartOf("body_caster_arm_link")==TipToeGeometry.Part.Body&&TipToeGeometry.PartOf("body_caster_detail_lower_arm_body_link")==TipToeGeometry.Part.Body,"Caster parts: the one-piece upper brace (swivel top housing), the lower brace, the column, and the body section with its hinge housing.");
        // Standalone playback rig (saved scenes in VR without the editor).
        var rig=new AnimationRig(urdf,settings.ConfigFolder);
        var drum=rig.RestPoint("body_pelvis_drum_link",Vector3.Zero);var front=rig.RestPoint("body_track_front_roller_left_link",Vector3.Zero);var caster=rig.RestPoint("body_caster_axle_link",Vector3.Zero);
        Vector3 Now(string link,Vector3 rest)=>Vector3.Transform(rest,rig.Palette[rig.Bone(link)]);
        rig.Apply(new(){{"servo:B_TipToe",(100,0,"B_TipToe")}});
        check(Math.Abs(rig.TipToeRadians-TipToeGeometry.MaximumRadians)<1e-9,"Playback applies a TipToe command of 100 as 61.27 degrees.");
        var drumNow=Now("body_pelvis_drum_link",drum);var frontNow=Now("body_track_front_roller_left_link",front);var casterNow=Now("body_caster_axle_link",caster);
        check(Math.Abs(drumNow.Z-drum.Z-full.Lift)<.001f&&Math.Abs(drumNow.X-drum.X-full.Forward)<.001f,"Playback raises the body and moves it forward by the shared lift and shift.");
        check(Vector3.Distance(frontNow,front)<.002f,"Playback keeps the front rollers planted on the floor.");
        check(casterNow.X>caster.X+.25f,"Playback rolls the caster wheel in from behind.");
        var wheelBottom=caster.Z-geometry.CasterWheelRadius;check(MathF.Abs(casterNow.Z-geometry.CasterWheelRadius-wheelBottom)<.012f,$"Playback keeps the caster wheel on the floor (axle {(casterNow.Z-caster.Z)*1000:F0} mm from its rest height; the column leans).");
        // The hinges stay joined: the upper brace's chamfer hinge and the column's marker coincide, as do the lower brace's end and the collar hinge.
        Vector3 Hinge(string link)=>Now(link,rig.RestPoint(link,Vector3.Zero));
        var a=rig.RestPoint(TipToeGeometry.UpperBodyHinge,Vector3.Zero);var c=rig.RestPoint(TipToeGeometry.LowerBodyHinge,Vector3.Zero);
        var bNow=Hinge(TipToeGeometry.UpperHeadHinge);var dNow=Hinge(TipToeGeometry.LowerHeadHinge);var aNow=Hinge(TipToeGeometry.UpperBodyHinge);var cNow=Hinge(TipToeGeometry.LowerBodyHinge);
        check(MathF.Abs(Vector3.Distance(aNow,bNow)-geometry.UpperBraceLength)<.001f&&MathF.Abs(Vector3.Distance(cNow,dNow)-geometry.LowerBraceLength)<.001f&&MathF.Abs(Vector3.Distance(bNow,dNow)-Vector3.Distance(rig.RestPoint(TipToeGeometry.UpperHeadHinge,Vector3.Zero),rig.RestPoint(TipToeGeometry.LowerHeadHinge,Vector3.Zero)))<.001f,"Playback keeps all four caster hinges joined (brace and column lengths unchanged).");
        var lug=rig.RestPoint("body_caster_wheel_l_lug_000_link",Vector3.Zero);var wheelCentre=rig.RestPoint("body_caster_wheel_l_link",Vector3.Zero);
        var lugNow=Now("body_caster_wheel_l_lug_000_link",lug);var centreNow=Now("body_caster_wheel_l_link",wheelCentre);
        float spokeBefore=MathF.Atan2((lug-wheelCentre).Z,(lug-wheelCentre).X),spokeAfter=MathF.Atan2((lugNow-centreNow).Z,(lugNow-centreNow).X);
        float turned=MathF.IEEERemainder(spokeBefore-spokeAfter,2*MathF.PI),expected=MathF.IEEERemainder((casterNow.X-caster.X)/geometry.CasterWheelRadius,2*MathF.PI);
        check(MathF.Abs(MathF.IEEERemainder(turned-expected,2*MathF.PI))<.05f,$"Playback turns the caster wheel {turned*180/MathF.PI:F0}° for {(casterNow.X-caster.X)*100:F1} cm of rolling (no slipping).");
        rig.Apply(new(){{"servo:B_TipToe",(0,0,"B_TipToe")}});
        check(rig.TipToeRadians==0&&Vector3.Distance(Now("body_pelvis_drum_link",drum),drum)<1e-5f,"Playback returns to the rest pose at 0.");
    }
}
