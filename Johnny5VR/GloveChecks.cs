using System.Numerics;
namespace Johnny5VR;
internal static class GloveChecks
{
    internal static void Run(Action<bool,string> check)
    {
        check(FollowControllerLabels.ClockText(new DateTime(2026,11,8,0,5,0))=="12:05 AM"&&FollowControllerLabels.ClockText(new DateTime(2026,11,8,13,7,0))=="1:07 PM","Left LCD clock uses local twelve-hour time with AM/PM including midnight.");
        var controller=Matrix4x4.CreateRotationY(.8f)*Matrix4x4.CreateRotationX(.3f)*Matrix4x4.CreateTranslation(.2f,1,-.4f);Matrix4x4.Invert(controller,out var inverse);
        var rigid=FollowControllerLabels.DisplayWorld(controller)*inverse;var expected=FollowControllerLabels.DisplayWorld(Matrix4x4.Identity);
        check(Vector3.Distance(rigid.Translation,expected.Translation)<1e-5f&&Vector3.Distance(new(rigid.M31,rigid.M32,rigid.M33),new(expected.M31,expected.M32,expected.M33))<1e-5f,"The combined A/B LCD has a fixed position and orientation relative to its glove, independent of head pose.");
        var open=ControllerGlove.Build([new(true,Matrix4x4.Identity,new float[5])]);var closed=ControllerGlove.Build([new(true,Matrix4x4.Identity,Enumerable.Repeat(1f,5).ToArray())]);
        check(open.Length==closed.Length&&open.Length>500&&open.Zip(closed).Count(p=>Vector3.Distance(p.First.Position,p.Second.Position)>.005f)>100,"Metal glove fingers articulate while retaining stable geometry topology.");
        check(open.All(v=>float.IsFinite(v.Position.X+v.Position.Y+v.Position.Z)&&Math.Abs(v.Normal.Length()-1)<.001f),"Glove vertices and lighting normals stay finite and normalized.");
        check(ControllerGlove.Curl([.1f,.2f,.3f,.4f,.5f],1)==.2f&&ControllerGlove.Curl([.1f,.2f,.3f,.4f,.5f],2)==.3f,"Glove index and middle fingers retain their own independent sensor curls.");
        check(!FollowMovement.DrivesViewer(true,FollowMovementTarget.Robot)&&FollowMovement.DrivesViewer(true,FollowMovementTarget.Viewer)&&FollowMovement.Label(FollowMovementTarget.Robot)=="Move Robot"&&FollowMovement.Cue(FollowMovementTarget.Robot)==FollowToggle.MoveRobot&&FollowMovement.Label(FollowMovementTarget.Viewer)=="Move Me"&&FollowMovement.Cue(FollowMovementTarget.Viewer)==FollowToggle.MoveMe,"Movement routing, LCD label and audio cue agree for both targets.");
        check(ControllerFeedback.ToggleGroup(FollowToggle.MoveMe)==ControllerFeedback.ToggleGroup(FollowToggle.MoveRobot)&&ControllerFeedback.ToggleGroup(FollowToggle.Mirrored)!=ControllerFeedback.ToggleGroup(FollowToggle.MoveMe),"New movement confirmations replace stale movement cues without replacing the mapping cue.");
        using(var motion=new VrBodyMotion(new NullOverlay()))
        {
            motion.Configure(Path.Combine(Diagnostics.Folder,"body-follow-check","Config"));
            using var file=new BinaryReader(File.OpenRead(Path.Combine(Diagnostics.Folder,"pose-control-catalog.bin")));var pose=Johnny5.VrBridge.PoseWire.Read(file)!;
            var frame=new ServoAnimator.BodyTrackingFrame(Matrix4x4.CreateTranslation(0,1.7f,0),Matrix4x4.CreateTranslation(-.2f,1.05f,0),Matrix4x4.CreateTranslation(.2f,1.05f,0),[0,0,0,0,0],[0,0,0,0,0],true,"skeletal");
            var batches=new List<Johnny5.VrBridge.VrMotionBatch>();var cues=new List<FollowToggle>();motion.Send=batches.Add;motion.ToggleFeedback=cues.Add;
            var input=ControllerReading.Failed("");motion.StartFollowing();motion.Step(frame,pose,input,true,.04f);float start=batches.Last().Targets["URDFStageX"];
            var viewer=new Locomotion();motion.Step(frame,pose,input with{Move=new(0,1)},true,.04f);float driven=batches.Last().Targets["URDFStageX"];
            viewer.Step(frame.Head,Vector2.Zero,0,.04f,1,30,true,true);
            check(driven>start&&viewer.Origin==Matrix4x4.Identity,"Actual Follow wrapper: Move Robot changes robot placement while the viewer stays still.");
            motion.FollowButtons(input with{LeftB=true},true);motion.Step(frame,pose,input with{Move=new(0,1)},true,.04f);
            viewer.Step(frame.Head,new(0,1),0,.04f,1,30,true,true);
            check(batches.Last().Targets["URDFStageX"]>driven&&viewer.Origin.Translation.Length()>.01f&&cues.Count==0,"Both targets can move together; Left B no longer reroutes the sticks or announces Move Me.");
        }
        foreach(bool left in new[]{true,false}){using var image=FollowControllerLabels.Display(left,true,FollowMovementTarget.Robot,"1:07 PM");image.Save(Path.Combine(Diagnostics.Folder,left?"glove-left-lcd.png":"glove-right-lcd.png"));}
    }
    private sealed class NullOverlay:IPanelOverlay
    {
        public Valve.VR.EVROverlayError CreateOverlay(string k,string n,ref ulong h){h=1;return 0;}
        public Valve.VR.EVROverlayError SetOverlayWidthInMeters(ulong h,float w)=>0;
        public Valve.VR.EVROverlayError SetOverlayInputMethod(ulong h,Valve.VR.VROverlayInputMethod m)=>0;
        public Valve.VR.EVROverlayError SetOverlaySortOrder(ulong h,uint o)=>0;
        public Valve.VR.EVROverlayError SetOverlayRaw(ulong h,IntPtr p,uint w,uint v,uint d)=>0;
        public Valve.VR.EVROverlayError SetOverlayTransformAbsolute(ulong h,Valve.VR.ETrackingUniverseOrigin o,ref Valve.VR.HmdMatrix34_t p)=>0;
        public Valve.VR.EVROverlayError ShowOverlay(ulong h)=>0;
        public Valve.VR.EVROverlayError HideOverlay(ulong h)=>0;
        public Valve.VR.EVROverlayError DestroyOverlay(ulong h)=>0;
        public void Dispose(){}
    }
    internal static void Preview()
    {
        Directory.CreateDirectory(Diagnostics.Folder);const int width=1800,height=1200;
        GloveHand[] hands=[new(true,Matrix4x4.CreateTranslation(-.105f,0,0),[.05f,.06f,.12f,.2f,.28f]),new(false,Matrix4x4.CreateRotationY(-.15f)*Matrix4x4.CreateTranslation(.105f,0,0),[.8f,.06f,.8f,.9f,1])];
        using var form=new Form{ClientSize=new(width,height),ShowInTaskbar=false};using var renderer=new Renderer(form.Handle,width,height);
        renderer.Upload(new SceneData([new(new(10,10,10),Vector3.UnitY,Vector4.One),new(new(10,10,11),Vector3.UnitY,Vector4.One),new(new(11,10,10),Vector3.UnitY,Vector4.One)],[],1,Vector3.Zero,Vector3.One,[],1));
        var eye=new Vector3(0,.32f,.20f);var view=Matrix4x4.CreateLookAt(eye,new(0,0,-.015f),Vector3.UnitY);renderer.PreviewGloves(hands,view,eye);
        string path=Path.Combine(Diagnostics.Folder,"metal-glove-preview.png");renderer.Screenshot(path);
        var vp=view*Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI/4,(float)width/height,.01f,10);
        PointF Project(Vector3 p){var value=Vector4.Transform(new Vector4(p,1),vp);return new((value.X/value.W+1)*width/2,(1-value.Y/value.W)*height/2);}
        // SteamVR composites LCD overlays separately; reproduce that same rigid transform in this desktop diagnostic.
        using var result=new Bitmap(path);using(var g=Graphics.FromImage(result))
        {
            foreach(var hand in hands)
            {
                var world=FollowControllerLabels.DisplayWorld(hand.World);float w=FollowControllerLabels.Width,h=w*400/640;
                PointF P(float x,float y)=>Project(Vector3.Transform(new(x,y,0),world));
                using var lcd=FollowControllerLabels.Display(hand.Left,true,FollowMovementTarget.Robot,"1:07 PM");g.DrawImage(lcd,new[]{P(-w/2,h/2),P(w/2,h/2),P(-w/2,-h/2)});
            }
        }
        result.Save(Path.Combine(Diagnostics.Folder,"metal-glove-lcd-preview.png"));
    }
}
