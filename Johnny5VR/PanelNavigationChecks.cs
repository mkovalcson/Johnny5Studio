using System.Numerics;
using Johnny5.VrBridge;
using Valve.VR;

namespace Johnny5VR;

internal static class PanelNavigationChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var layout=new EditorPanelLayout{World=Matrix4x4.CreateTranslation(0,1.5f,-2),Width=2,Aspect=.6f};
        var controller=Matrix4x4.CreateTranslation(0,1.5f,0);
        check(layout.Begin(PanelTarget.Move,controller,new(0,2,-2)),"Top bar starts a captured trigger move.");
        var start=layout.World;controller.Translation+=new Vector3(.3f,.2f,-.4f);layout.Update(controller,Vector3.Zero,-Vector3.UnitZ);
        check(Vector3.Distance(layout.World.Translation,start.Translation+new Vector3(.3f,.2f,-.4f))<1e-6,"Trigger movement follows controller translation without a jump.");
        layout.End();var placed=layout.World;layout.Update(Matrix4x4.Identity,Vector3.Zero,-Vector3.UnitZ);
        check(layout.World==placed,"Releasing a grab fixes its placement in the room.");
        foreach(var (target,sx,sy) in new[]{(PanelTarget.TopLeft,-1,1),(PanelTarget.TopRight,1,1),(PanelTarget.BottomLeft,-1,-1),(PanelTarget.BottomRight,1,-1)})
        {
            layout=new(){World=Matrix4x4.CreateRotationY(.4f)*Matrix4x4.CreateTranslation(1,1.5f,-2),Width=2,Aspect=.6f};
            var corner=Vector3.Transform(new(sx,sy*.6f,0),layout.World);var anchor=Vector3.Transform(new(-sx,-sy*.6f,0),layout.World);
            var normal=Vector3.TransformNormal(Vector3.UnitZ,layout.World);
            check(layout.Begin(target,Matrix4x4.Identity,corner),target+" starts resizing.");
            var stretched=corner+Vector3.TransformNormal(new(sx*.4f,sy*.24f,0),layout.World);
            layout.Update(Matrix4x4.Identity,stretched+normal*2,-normal);
            var fixedCorner=Vector3.Transform(new(-sx*layout.Width/2,-sy*layout.Width*layout.Aspect/2,0),layout.World);
            check(Math.Abs(layout.Width-2.4f)<1e-5&&Vector3.Distance(fixedCorner,anchor)<1e-5,target+" stretches proportionally around the fixed opposite corner.");
            layout.Update(Matrix4x4.Identity,stretched+normal*2,Vector3.TransformNormal(Vector3.UnitX,layout.World));
            check(EditorPanelLayout.Finite(layout.World),"A ray parallel to the resize plane cannot corrupt placement.");
        }
        layout.Locked=true;layout.End();check(!layout.Begin(PanelTarget.Move,controller,default)&&!layout.Begin(PanelTarget.TopLeft,controller,default),"Layout lock blocks both moving and resizing.");
        check(!EditorPanelLayout.RayPlane(Matrix4x4.Identity,new(0,0,1),Vector3.UnitZ,out _),"A panel behind the pointing ray cannot be grabbed.");
        var frame=new EditorDesktopFrame(new byte[640*400*4],640,400,new(0,0,640,400),[]);
        var image=EditorPanelChrome.Draw(frame,new(PanelTarget.TopRight,false,"Hold trigger to stretch; release to place"));
        check(EditorPanelChrome.Hit(image.Width,image.Height,.005f,.005f)==PanelTarget.TopLeft&&EditorPanelChrome.Hit(image.Width,image.Height,.995f,.995f)==PanelTarget.BottomRight,"Visible corner handles have distinct hit targets.");
        check(EditorPanelChrome.Hit(image.Width,image.Height,.5f,.07f)==PanelTarget.Move,"The top grab bar is separate from the editor content.");
        check(EditorPanelChrome.Hit(image.Width,image.Height,.5f,.5f)==PanelTarget.Content,"Editor content is distinct from frame buttons and handles.");
        using var overlay=new FakePanelOverlay();using var panel=new EditorVrPanel(0,overlay,false);panel.AcceptFrame(frame);
        var head=Matrix4x4.CreateTranslation(0,1.6f,0);ControllerReading Input(bool trigger=false)=>new(Vector2.Zero,Vector2.Zero,"","",true,"",Select:trigger);
        Matrix4x4 Aim(float u,float v)
        {
            var target=Vector3.Transform(new((u-.5f)*panel.Width,(.5f-v)*panel.Width*image.Height/image.Width,0),panel.World);
            return Matrix4x4.CreateWorld(head.Translation,Vector3.Normalize(target-head.Translation),Vector3.UnitY);
        }
        void Step(Matrix4x4 c,bool trigger=false,bool focus=true)=>panel.Step(head,Matrix4x4.Identity,c,Input(trigger),focus,false,.02f);
        panel.Step(head,Matrix4x4.Identity,head,Input(),true,false,.02f);
        var aim=Aim(.5f,.07f);for(int i=0;i<15;i++)Step(aim);
        Step(aim,true);check(panel.IsDragging,"The actual panel starts a trigger grab through the top bar.");
        var shifted=aim;shifted.Translation+=new Vector3(.2f,0,0);Step(shifted,true);
        check(panel.IsDragging,"A grab stays captured while the pointer moves away from its original hit.");
        overlay.FailNextPose=true;Step(shifted,true);
        check(!panel.IsDragging&&panel.Error.Contains("Injected")&&overlay.Destroyed==0,"A simulated overlay failure releases the interaction without destroying VR overlays.");
        panel.Recenter();Step(aim);check(panel.Error==""&&!panel.IsDragging,"Bring Here recovers from an interaction error.");
        panel.ToggleLock();for(int i=0;i<15;i++)Step(Aim(.5f,.07f));Step(Aim(.5f,.07f),true);
        check(panel.Locked&&!panel.IsDragging,"The actual panel respects layout lock on trigger presses.");
        Step(aim,false,false);check(!panel.IsDragging,"Focus loss releases panel manipulation.");
        int commands=0;panel.Command=c=>{if(c==VrEditorCommand.PlayPause)commands++;};
        float buttonU=(EditorPanelChrome.Border+(image.Width-2f*EditorPanelChrome.Border)/14)/image.Width;
        float buttonV=(EditorPanelChrome.Top-29f)/image.Height;
        for(int i=0;i<20;i++)Step(Aim(buttonU,buttonV));
        for(int i=0;i<5;i++)Step(Aim(buttonU,buttonV),true);
        check(commands==1,"Toolbar playback activates once per trigger press even when layout is locked.");
    }
    private sealed class FakePanelOverlay:IPanelOverlay,IDisposable
    {
        internal bool FailNextPose;internal int Destroyed;private ulong next;
        public EVROverlayError CreateOverlay(string key,string name,ref ulong handle){handle=++next;return 0;}
        public EVROverlayError SetOverlayWidthInMeters(ulong h,float width){if(!float.IsFinite(width)||width<=0)throw new Exception("Invalid width");return 0;}
        public EVROverlayError SetOverlayInputMethod(ulong h,VROverlayInputMethod m)=>0;
        public EVROverlayError SetOverlaySortOrder(ulong h,uint order)=>0;
        public EVROverlayError SetOverlayRaw(ulong h,IntPtr data,uint w,uint height,uint depth)=>0;
        public EVROverlayError SetOverlayTransformAbsolute(ulong h,ETrackingUniverseOrigin origin,ref HmdMatrix34_t pose){if(FailNextPose){FailNextPose=false;throw new InvalidOperationException("Injected panel overlay failure");}return 0;}
        public EVROverlayError ShowOverlay(ulong h)=>0;
        public EVROverlayError HideOverlay(ulong h)=>0;
        public EVROverlayError DestroyOverlay(ulong h){Destroyed++;return 0;}
        public void Dispose(){}
    }
}
