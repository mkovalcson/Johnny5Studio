using System.Numerics;
using Valve.VR;
namespace Johnny5VR;
internal static class VrTaskMenuChecks
{
    internal static void Run(Action<bool,string> check)
    {
        check(VrMenu.FooterAt(new(.2f,850f/900))==0&&VrMenu.FooterAt(new(.8f,850f/900))==1&&VrMenu.FooterAt(new(.5f,.4f))==-1,"Back/Close use fixed footer hit regions, separate from scrolling content.");
        using(var menu=new VrMenu(new Overlay()))
        {
            var input=ControllerReading.Failed("");var head=Matrix4x4.CreateTranslation(0,1.6f,0);int backs=0;
            MenuRow[] Rows(int count)=>Enumerable.Range(0,count).Select(i=>new MenuRow("Item "+i,()=>{})).ToArray();
            menu.GoBack=()=>backs++;menu.Set("Long page",Rows(12));menu.Navigation.Selected=10;menu.Set("Other page",Rows(2));menu.Set("Long page",Rows(12));
            check(menu.Navigation.Selected==10,"Returning to a task page restores its selection.");
            menu.Set("Short",[new("Item",()=>{}),new("Back",()=>{}),new("Close menu",()=>{})]);check(menu.Rows.Length==1,"Navigation controls are removed from scrolling task rows.");
            menu.Navigation.Open();menu.Step(input,true,head);
            Matrix4x4 Point(float u,float v)
            {
                var target=new Vector3((u-.5f)*1.05f,1.48f+(.5f-v)*1.05f*900/1100,-1.3f);
                return Matrix4x4.CreateWorld(head.Translation,Vector3.Normalize(target-head.Translation),Vector3.UnitY);
            }
            menu.Step(input,true,head,Point(.2f,850f/900));menu.Step(input with{Select=true},true,head,Point(.2f,850f/900));
            check(backs==1&&menu.Navigation.Visible,"Fixed Back invokes the parent page without closing the menu.");
            menu.Step(input,true,head,Point(.8f,850f/900));menu.Step(input with{Select=true},true,head,Point(.8f,850f/900));
            check(!menu.Navigation.Visible,"Fixed Close dismisses the menu from the footer.");
        }
        var headPose=Matrix4x4.CreateTranslation(0,1.7f,0);var panelWorld=VrTransportPanel.Placement(headPose);
        check(panelWorld.Translation.Y<headPose.Translation.Y&&panelWorld.Translation.X<0,"Compact transport starts below and beside the robot-facing view.");
        check(VrTransportPanel.Hit(new(.15f,.6f))==0&&VrTransportPanel.Hit(new(.5f,.6f))==1&&VrTransportPanel.Hit(new(.85f,.6f))==2&&VrTransportPanel.Hit(new(.5f,.05f))==3&&VrTransportPanel.Hit(new(.98f,.05f))==4,"Transport separates playback, Stop, Record, drag handle and Close targets.");
        using(var panel=new VrTransportPanel(new Overlay()))
        {
            bool follow=false;int actions=0;panel.Source=()=>new("Test sequence",12.3,false,false,follow,"Paused");panel.Action=i=>actions++;
            var point=Vector3.Transform(new Vector3((.15f-.5f)*.72f,(.5f-.6f)*.72f*330/1200,0),panelWorld);
            var ray=Matrix4x4.CreateWorld(headPose.Translation,Vector3.Normalize(point-headPose.Translation),Vector3.UnitY);var input=ControllerReading.Failed("");
            panel.Step(headPose,Matrix4x4.Identity,ray,input,true,true);panel.Step(headPose,Matrix4x4.Identity,ray,input with{Select=true},true,true);
            panel.Step(headPose,Matrix4x4.Identity,ray,input with{Select=true},true,true);check(actions==1,"Transport activates once per trigger press, not once per held frame.");
            panel.Step(headPose,Matrix4x4.Identity,ray,input,true,true);follow=true;panel.Step(headPose,Matrix4x4.Identity,ray,input with{Select=true},true,true);
            check(actions==1&&panel.Pointer==null,"Follow mode keeps transport read-only and does not expose a selection ray.");
        }
        var rehearsal=new TransportPanelState("Rehearsal",4,false,false,false,""){Rehearsal=true};
        check(VrTransportPanel.Buttons(rehearsal).Select(b=>b.Action).SequenceEqual(new[]{0,1,2,8})&&VrTransportPanel.Hit(new(.88f,.55f),rehearsal)==8,"Ready rehearsal panel offers separate Record and Overdub actions.");
        check(VrTransportPanel.Buttons(rehearsal with{Pending=true,Countdown=3}).Single().Action==1,"Countdown exposes Cancel without starting another take.");
        check(VrTransportPanel.Buttons(rehearsal with{Recording=true,Elapsed=2.3}).Single().Action==1,"Active take exposes Stop and reports elapsed time.");
        check(VrTransportPanel.Buttons(rehearsal with{HasTake=true}).Select(b=>b.Action).SequenceEqual(new[]{5,6,7}),"Draft review offers Replay, Keep and Discard.");
        check(VrTransportPanel.Buttons(rehearsal with{HasTake=true,Replaying=true}).Single().Action==1,"Replay exposes Stop while guarding Keep and Discard.");
        using(var stream=new MemoryStream())
        {
            var pose=new Johnny5.VrBridge.VrPoseState("token",3,false,0,"","",[]){Replaying=true,HasTake=true,RecordedSeconds=2,TravelExtentReceipt="saved",TravelExtents=[new("B_ElbowLeft",-30,52,-360,360,"°")]};
            using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))Johnny5.VrBridge.PoseWire.Write(writer,pose);
            stream.Position=0;using var reader=new BinaryReader(stream);
            var decoded=Johnny5.VrBridge.PoseWire.Read(reader);
            check(decoded is {Replaying:true,HasTake:true,RecordedSeconds:2},"Bridge preserves replay state and take duration.");
            check(decoded?.TravelExtentReceipt=="saved"&&decoded.TravelExtents.Single().Maximum==52,"Bridge preserves travel extents and their independent save receipt.");
        }
        foreach(var state in new[]{new TransportPanelState("Introduction · VR Editor sequence",12.3,false,false,false,"Ready · current timeline cursor"),new TransportPanelState("Body recording",23.8,true,false,true,"")})
        {using var image=VrTransportPanel.Draw(state);image.Save(Path.Combine(Diagnostics.Folder,state.Recording?"transport-recording.png":"transport-idle.png"));}
        using(var image=VrMenu.Draw("J5 Studio · Home","Choose a task. Back and Close stay below every page.",["Library · Movies and sequences","Playback · Transport and effects","Pose · Robot controls","Follow / Record · Body movements","Editor · Workspace","Settings · Navigation and feedback"],0))image.Save(Path.Combine(Diagnostics.Folder,"vr-task-home.png"));
        using(var image=VrMenu.Draw("Library · Sequences","Back and Close remain visible while this library scrolls.",Enumerable.Range(1,20).Select(i=>"Sequence "+i).ToArray(),11,8))image.Save(Path.Combine(Diagnostics.Folder,"vr-task-scrolling.png"));
    }
    private sealed class Overlay:IPanelOverlay
    {
        private ulong id;
        public EVROverlayError CreateOverlay(string key,string name,ref ulong handle){handle=++id;return 0;}
        public EVROverlayError SetOverlayWidthInMeters(ulong h,float w)=>0;
        public EVROverlayError SetOverlayInputMethod(ulong h,VROverlayInputMethod m)=>0;
        public EVROverlayError SetOverlaySortOrder(ulong h,uint o)=>0;
        public EVROverlayError SetOverlayRaw(ulong h,IntPtr p,uint w,uint v,uint d)=>0;
        public EVROverlayError SetOverlayTransformAbsolute(ulong h,ETrackingUniverseOrigin o,ref HmdMatrix34_t p)=>0;
        public EVROverlayError ShowOverlay(ulong h)=>0;
        public EVROverlayError HideOverlay(ulong h)=>0;
        public EVROverlayError DestroyOverlay(ulong h)=>0;
        public void Dispose(){}
    }
}
