using System.Numerics;
using ServoAnimator;
namespace Johnny5VR;
internal static class FollowNeutralChecks
{
    internal static void Run()
    {
        int count=0;void Check(bool valid,string message){if(!valid)throw new InvalidOperationException(message);count++;}
        var controls=FollowNeutralCatalog.Controls;
        Check(controls.Length==37&&controls.Select(c=>c.Key).Distinct().Count()==37,"Every tracked head/body/hand joint has exactly one neutral slider.");
        Check(controls.Select(c=>c.Section).Distinct().Count()==6,"Torso, head, left/right arms and hands have separate pages.");
        Check(!controls.Any(c=>c.Key.StartsWith("URDF")||c.Key.Contains("Laser")||c.Key.Contains("Hug")),"Travel, accessory and disabled hug controls are excluded.");
        foreach(var c in controls)Check(Math.Abs(c.FromSlider(c.ToSlider(c.Initial))-c.Initial)<.0001f,"Neutral slider round trip: "+c.Key);
        TrackedArm arm=new(Vector3.Zero,Vector3.Zero,Vector3.Zero,0,0,0);
        BodyTrackingResult Pose(Dictionary<string,float> targets)=>new(targets,arm,arm,0,0,0,0,"");
        var reference=Pose(new(){{"B_NeckLift",0},{"B_ChestTilt",0},{"B_WaistLean",0},{"B_WristRollLeft",.2f},{"B_WristRollRight",-.1f},{"NeckTurn",0},{"URDFStageX",12}});
        var mapper=new FollowNeutralMapper();mapper.Capture(reference);
        var neutral=new Dictionary<string,float>{{"B_NeckLift",.7f},{"B_ChestTilt",.1f},{"B_WaistLean",-.1f},{"B_WristRollLeft",.4f},{"NeckTurn",15}};
        var standing=mapper.Apply(reference,false,neutral);
        Check(standing.Targets["B_NeckLift"]==.7f&&standing.Targets["B_ChestTilt"]==.1f&&standing.Targets["B_WaistLean"]==-.1f,"Measured neutral stance adopts separately selected neck/chest/waist defaults.");
        var moved=reference with{Targets=new(reference.Targets){["B_NeckLift"]=-.175f,["B_WristRollLeft"]=.5f,["NeckTurn"]=10}};
        var adjusted=mapper.Apply(moved,false,neutral);
        Check(Math.Abs(adjusted.Targets["B_NeckLift"]-.175f)<.0001f,"Half of neck lowering traverses half the distance from custom rest height to the bottom.");
        Check(Math.Abs(adjusted.Targets["B_WristRollLeft"]-.7f)<.0001f&&adjusted.Targets["NeckTurn"]==5,"Body and head movement are relative to the measured reference plus the chosen defaults.");
        Check(adjusted.Targets["URDFStageX"]==12,"Neutral defaults never alter joystick travel.");
        var lowest=mapper.Apply(reference with{Targets=new(reference.Targets){["B_NeckLift"]=-.35f}},false,neutral);
        Check(Math.Abs(lowest.Targets["B_NeckLift"]+.35f)<.0001f,"Even a high neck default reaches the full lower endpoint.");
        var swapped=mapper.Apply(reference with{Targets=new(reference.Targets){["B_WristRollRight"]=.3f}},true,neutral);
        Check(Math.Abs(swapped.Targets["B_WristRollLeft"]-.8f)<.0001f,"Robot-side defaults remain on that robot side when mirroring swaps measured motions.");
        neutral["B_WristRollLeft"]=100;Check(mapper.Apply(moved,false,neutral).Targets["B_WristRollLeft"]<=MathF.PI,"Offsets clamp to the modeled joint limits.");
        Check(FollowNeutralMapper.WaitForNeck(-.35f,.3)&&!FollowNeutralMapper.WaitForNeck(-.35f,-.35)&&!FollowNeutralMapper.WaitForNeck(-.2f,.3),"Torso waits for actual neck arrival, rather than just its requested target.");
        Check(!FollowNeutralMapper.WaitForNeck(-.35f,-.2,-.2),"A narrower configured neck range cannot deadlock torso movement.");
        var profile=new BodyTrackingCalibration{LeftShoulder=new(-.2f,-.3f,0),RightShoulder=new(.2f,-.3f,0),LeftUpper=.3f,RightUpper=.3f,LeftForearm=.35f,RightForearm=.35f,StandingHeight=1.7f,CrouchDrop=.4f,BowDrop=.25f,BowForward=.3f};
        BodyTrackingFrame Frame(float drop,float forward=0,float pitch=0)=>new(Matrix4x4.CreateRotationX(pitch)*Matrix4x4.CreateTranslation(0,1.7f-drop,-forward),Matrix4x4.CreateTranslation(-.2f,1.05f,-.3f-forward),Matrix4x4.CreateTranslation(.2f,1.05f,-.3f-forward),[0,0,0,0,0],[0,0,0,0,0],true,"skeletal");
        BodyTrackingResult Solve(BodyTrackingFrame frame){var solver=new BodyTrackingSolver(profile);solver.Recenter(Frame(0),0,0,0);return solver.Solve(frame,.02f);}
        var shallow=Solve(Frame(.08f));Check(shallow.Targets["B_NeckLift"]<0&&shallow.Targets["B_ChestTilt"]==0&&shallow.Targets["B_WaistLean"]==0,"Shallow crouching lowers only the neck.");
        var bow=Solve(Frame(.05f,.15f,-.5f));Check(bow.Targets["B_NeckLift"]<0&&bow.Targets["B_ChestTilt"]==0&&bow.Targets["B_WaistLean"]==0,"Shallow forward bending also lowers the neck before the torso.");
        var deep=Solve(Frame(.4f,.3f,-.8f));Check(Math.Abs(deep.Targets["B_NeckLift"]+.35f)<.001f&&deep.Targets["B_ChestTilt"]>0&&deep.Targets["B_WaistLean"]>0,"Deep crouch/bow reaches the neck bottom and then bends the torso.");
        var unchanged=mapper.Apply(reference,false,new Dictionary<string,float>());Check(unchanged.Targets.SequenceEqual(reference.Targets),"Reset restores the measured mapping without leftover offsets.");
        var sanitized=FollowNeutralCatalog.Sanitize(new Dictionary<string,float>{{"B_NeckLift",100},{"B_ChestTilt",float.NaN},{"Unknown",1}});
        Check(sanitized.Count==1&&sanitized["B_NeckLift"]==controls.Single(c=>c.Key=="B_NeckLift").Maximum,"Invalid saved keys and nonfinite values are filtered; out-of-range values clamp.");
        neutral["B_WristRollLeft"]=.4f;
        var input=new FollowInputFilter();
        const string wrist="B_WristRollLeft";
        input.Settings[wrist]=new(.5f,0);
        Check(Math.Abs(mapper.Apply(moved,false,neutral,input,.02f).Targets[wrist]-.55f)<.0001f,"50% input scaling halves displacement about a custom neutral.");
        Check(Math.Abs(mapper.Apply(reference,false,neutral,input,.02f).Targets[wrist]-.4f)<.0001f,"Scaling leaves the resting pose in place.");
        input.Settings[wrist]=new(2,0);
        Check(Math.Abs(mapper.Apply(moved,false,neutral,input,.02f).Targets[wrist]-1f)<.0001f,"200% input scaling doubles displacement.");
        var pointing=moved with{Left=arm with{Pointing=true}};
        Check(mapper.Apply(pointing,false,neutral,input,.02f).Targets[wrist]==moved.Targets[wrist],"Assisted pointing bypasses filtering and scaling.");
        float FilterAt(float dt,int steps)
        {
            var f=new FollowInputFilter();f.Settings[wrist]=new(1,100);
            f.Shape(wrist,0,dt);
            float value=0;for(int i=0;i<steps;i++)value=f.Shape(wrist,1,dt);
            return value;
        }
        Check(Math.Abs(FilterAt(.01f,10)-FilterAt(.05f,2))<.0001f&&FilterAt(.01f,1)<.1f,"Smoothing damps a fast change and gives the same response at different frame rates.");
        input.Settings[wrist]=new(1,100);
        mapper.Apply(reference,false,neutral,input,.02f);
        var filtered=mapper.Apply(moved,false,neutral,input,.02f);
        Check(filtered.Targets[wrist]>.4f&&filtered.Targets[wrist]<.5f&&filtered.Targets["URDFStageX"]==12,"Joint smoothing leaves joystick travel alone.");
        input.Reset();
        Check(Math.Abs(mapper.Apply(moved,false,neutral,input,.02f).Targets[wrist]-.7f)<.0001f,"Reset discards stale filter history.");
        input.Settings["B_NeckLift"]=new(.5f,100);
        var staged=new BodyTrackingSolver(profile);staged.Recenter(Frame(0),0,0,0);
        staged.Solve(Frame(0),.02f,input);
        var early=staged.Solve(Frame(.4f),.02f,input);
        Check(early.Targets["B_NeckLift"]<0&&early.Targets["B_ChestTilt"]==0,"Filtered crouch still lowers the neck before bending the torso.");
        var late=early;for(int i=0;i<100;i++)late=staged.Solve(Frame(.4f),.02f,input);
        Check(Math.Abs(late.Targets["B_NeckLift"]+.35f)<.001f&&late.Targets["B_ChestTilt"]>0,"The scaled and filtered crouch can reach the neck endpoint and transition to torso bend.");
        string folder=Directory.CreateTempSubdirectory("J5-Neutral-Check-").FullName;
        try
        {
            var settings=new FollowNeutralSettings();settings.Load(folder);var c=controls[0];settings.Set(c,1);settings.Flush();
            var restored=new FollowNeutralSettings();restored.Load(folder);Check(restored.Values[c.Key]==c.Maximum,"Neutral settings save and reload separately from calibration measurements.");
            var tuning=new FollowInputSettings();tuning.Load(folder);tuning.Set(wrist,new(.65f,125));tuning.Flush();
            var loadedTuning=new FollowInputSettings();loadedTuning.Load(folder);
            Check(loadedTuning.Filter.Get(wrist)==new FollowInputAdjustment(.65f,125)&&loadedTuning.Filter.Get("NeckTurn")==FollowInputAdjustment.Default,"Independent input settings save and reload without changing unconfigured controls.");
            restored.Reset(controls);restored.Flush();var reset=new FollowNeutralSettings();reset.Load(folder);Check(reset.Values.Count==0,"Reset persists the absence of custom offsets.");
        }
        finally {if(ConfigurationPaths.IsWithin(Path.GetTempPath(),folder)&&Path.GetFileName(folder).StartsWith("J5-Neutral-Check-",StringComparison.Ordinal))Directory.Delete(folder,true);}
        Console.WriteLine($"PASS: {count} focused neutral pose, input tuning and neck-first sequencing checks. Full suite not run.");
    }
}
