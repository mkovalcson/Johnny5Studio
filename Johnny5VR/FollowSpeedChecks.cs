using System.Numerics;
using ServoAnimator;
using Johnny5.VrBridge;
namespace Johnny5VR;
internal static class FollowSpeedChecks
{
    internal static void Run()
    {
        int count=0;void Check(bool good,string name){if(!good)throw new InvalidOperationException(name);count++;}
        var values=FollowSpeedCatalog.Controls.ToDictionary(c=>c.Key,c=>c.Initial);
        foreach(var c in FollowSpeedCatalog.Controls)
        {
            Check(FollowSpeedCatalog.FromSlider(FollowSpeedCatalog.ToSlider(c.Initial))==c.Initial,"Slider round trip: "+c.Key);
            if(c.Key.StartsWith("B_")&&!new[]{"B_NeckLift","B_ChestTilt","B_WaistSwivel","B_WaistLean","B_LaserLower"}.Contains(c.Key))
            {
                values[c.Key]=.4f;Check(FollowSpeedCatalog.Value(values,c.Key+"Left")==.4f&&FollowSpeedCatalog.Value(values,c.Key+"Right")==.4f,"Paired speed: "+c.Key);values[c.Key]=c.Initial;
            }
        }
        Check(FollowSpeedCatalog.FromSlider(0)==.1f&&FollowSpeedCatalog.FromSlider(1)==5,"Slider endpoints.");
        Check(FollowSpeedCatalog.Value(values,"B_ShoulderRotateLeft")==3&&FollowSpeedCatalog.Value(values,"B_WristRollRight")==2,"Existing boosts preserved as initial values.");
        values["B_ShoulderRotate"]=1;Check(FollowSpeedCatalog.Value(values,"B_ShoulderRotateLeft")==1,"1x replaces the boost, rather than multiplying it.");
        values["B_ShoulderRotate"]=float.NaN;Check(FollowSpeedCatalog.Value(values,"B_ShoulderRotateLeft")==1,"Invalid stored speeds recover safely.");
        var profile=new BodyTrackingCalibration{LeftShoulder=new(-.2f,-.3f,0),RightShoulder=new(.2f,-.3f,0),LeftUpper=.3f,RightUpper=.3f,LeftForearm=.35f,RightForearm=.35f,StandingHeight=1.7f,CrouchDrop=.4f,BowDrop=.25f,BowForward=.3f};
        string folder=Path.Combine(Diagnostics.Folder,"speed-check-"+Guid.NewGuid().ToString("N"));profile.Save(Path.Combine(folder,"VR","BodyTrackingCalibration.json"));
        using(var motion=new VrBodyMotion(new FollowControlsChecks.NullOverlay()))
        {
            motion.Configure(folder);motion.Speeds.Set("B_ShoulderRotate",FollowSpeedCatalog.ToSlider(.5f));
            var frame=new BodyTrackingFrame(Matrix4x4.CreateTranslation(0,1.7f,0),Matrix4x4.CreateTranslation(-.2f,1.05f,-.3f),Matrix4x4.CreateTranslation(.2f,1.05f,-.3f),[0,0,0,0,0],[0,0,0,0,0],true,"skeletal");
            VrPoseHandle H(string key)=>new("Servo:"+key,key,Vector3.Zero,-100,100,0,false);
            var pose=new VrPoseState("speed",0,true,0,"","",[H("B_ShoulderRotateLeft"),H("B_ShoulderRotateRight")]);
            var packets=new List<VrMotionBatch>();motion.Send=packets.Add;motion.StartFollowing();
            var input=ControllerReading.Failed("");motion.Step(frame,pose,input,true,.02f);
            Check(packets.Count>0&&packets[^1].Speeds["B_ShoulderRotate"]==.5f,"Live values reach the Editor packet.");
            Check(packets[^1].Targets.Keys.All(k=>FollowSpeedCatalog.Controls.Any(c=>c.Key==FollowSpeedCatalog.Key(k))),"Every tracked target has a speed slider.");
            float x=packets[^1].Targets["URDFStageX"],y=packets[^1].Targets["URDFStageY"],heading=packets[^1].Targets["URDFStageHeading"];
            motion.SelectionsOpen=true;motion.TuningSpeeds=true;
            motion.Speeds.Set("B_ShoulderRotate",FollowSpeedCatalog.ToSlider(.1f));int before=packets.Count;
            motion.Step(frame with{Left=Matrix4x4.CreateTranslation(-.2f,1.3f,-.4f)},pose,input with{Move=new(1,1),Right=new(1,1)},true,.02f);
            Check(motion.Following&&packets.Count==before+1&&packets[^1].Speeds["B_ShoulderRotate"]==.1f,"Tuning continues Follow and immediately sends changed speed.");
            Check(packets[^1].Targets["URDFStageX"]==x&&packets[^1].Targets["URDFStageY"]==y&&packets[^1].Targets["URDFStageHeading"]==heading,"Sliders cannot accidentally drive the robot.");
            using var memory=new MemoryStream();using var writer=new BinaryWriter(memory,System.Text.Encoding.UTF8,true);VrMotionWire.Write(writer,packets[^1]);writer.Flush();memory.Position=0;
            using var reader=new BinaryReader(memory);var decoded=VrMotionWire.Read(reader);
            Check(decoded.Speeds.SequenceEqual(packets[^1].Speeds)&&decoded.Targets.SequenceEqual(packets[^1].Targets)&&memory.Position==memory.Length,"Matching Player/Editor wire round trip consumes all values.");
            motion.Speeds.Flush();var reload=new FollowSpeedSettings();reload.Load(folder);Check(reload.Get("B_ShoulderRotateLeft")==.1f&&reload.Get("B_ShoulderRotateRight")==.1f,"Saved speeds survive reload for both sides.");
            reload.Set("B_WristRoll",FollowSpeedCatalog.ToSlider(.7f));reload.Set("B_WristPitch",FollowSpeedCatalog.ToSlider(.9f));reload.Set("B_ArmTwist",FollowSpeedCatalog.ToSlider(1.2f));reload.Set("B_Elbow",FollowSpeedCatalog.ToSlider(.8f));
            reload.Set(FollowSpeedSettings.AllFingers,FollowSpeedCatalog.ToSlider(.4f));
            Check(FollowSpeedCatalog.Controls.Where(c=>FollowSpeedSettings.IsFinger(c.Key)).All(c=>reload.Get(c.Key+"Left")==.4f&&reload.Get(c.Key+"Right")==.4f),"All-finger slider controls every finger and thumb joint on both hands.");
            Check(reload.Get("B_WristRoll")==.7f&&reload.Get("B_WristPitch")==.9f&&reload.Get("B_ArmTwist")==1.2f&&reload.Get("B_Elbow")==.8f,"Grouped finger adjustment leaves wrist/arm/elbow speeds alone.");
            reload.Set("B_ThumbCurl",FollowSpeedCatalog.ToSlider(.2f));reload.Set("B_ThumbTipCurl",FollowSpeedCatalog.ToSlider(.6f));
            Check(reload.MixedFingers&&Math.Abs(reload.Get(FollowSpeedSettings.AllFingers)-.4f)<.001f,"Advanced finger adjustments show a mixed master value.");
            reload.Set(FollowSpeedSettings.AllFingers,FollowSpeedCatalog.ToSlider(.4f));Check(!reload.MixedFingers,"Master adjustment synchronizes mixed values even when their average is unchanged.");
            reload.Flush();var restored=new FollowSpeedSettings();restored.Load(folder);Check(restored.Get(FollowSpeedSettings.AllFingers)==.4f&&!restored.MixedFingers&&restored.Get("B_WristPitch")==.9f,"Grouped finger and separate wrist settings survive reloading.");
            Check(!restored.Values.ContainsKey(FollowSpeedSettings.AllFingers),"Grouped UI control writes existing per-joint keys accepted by the Editor.");
            var primary=ViewerWindow.MainFollowSpeeds;Check(primary.Select(c=>c.Key).SequenceEqual(new[]{FollowSpeedSettings.AllFingers,"B_WristRoll","B_WristPitch","B_ArmTwist","B_Elbow","B_ShoulderRotate"}),"Requested arm and hand sliders all fit on the first page.");
            var labels=primary.Select(c=>$"{c.Label}  {restored.Get(c.Key):F1}×").Concat(new[]{"Head, body and travel speeds…","Advanced finger speeds…"}).ToArray();
            using var bitmap=VrMenu.Draw("Follow · Arm and hand speeds","All fingers + thumbs adjusts both hands together. Wrist roll, wrist pitch, arm twist and elbow have separate paired sliders. Follow stays active; changes save automatically.",labels,0,0,primary.Select(c=>(float?)FollowSpeedCatalog.ToSlider(restored.Get(c.Key))).Concat(new float?[]{null,null}).ToArray(),192);
            bitmap.Save(Path.Combine(Diagnostics.Folder,"follow-speed-sliders.png"));
        }
        Console.WriteLine($"PASS: {count} focused Follow speed checks. Full suite not run.");
    }
}
