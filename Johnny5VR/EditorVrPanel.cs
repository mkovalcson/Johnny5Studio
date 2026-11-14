using System.Numerics;
using System.Runtime.InteropServices;
using Johnny5.VrBridge;
using Valve.VR;

namespace Johnny5VR;

internal sealed class EditorVrPanel:IDisposable
{
    private sealed record Capture(EditorDesktopFrame Desktop,PanelImage Image);
    private readonly IPanelOverlay overlay;
    private readonly int process;
    private readonly CancellationTokenSource stop=new();
    private readonly EditorPanelLayout layout=new();
    internal Func<EditorDesktopFrame?>? CaptureSource;
    private ulong handle,cursor;
    private Capture? pending,last;
    private volatile bool visible=true;
    private bool positioned,triggerHeld,rightHeld,cursorShown,selectWasDown,backWasDown,blocked,shown,disposed;
    private Vector2 smoothUv;
    private bool pointerReady;
    private long lastScroll,retryAfter;
    private PanelChromeState chrome=new(PanelTarget.None,false,"Trigger: click · B: right-click · Right stick: timeline pan / zoom, or panel scroll");
    internal Action<VrEditorCommand>? Command;
    internal Action<VrEditorNavigation>? Navigate;
    internal Action? GrabFeedback;
    internal string Error {get;private set;}="";
    internal bool Locked=>layout.Locked;
    internal bool IsDragging=>layout.Drag!=PanelTarget.None;
    internal void Hide(){if(visible)Toggle();}
    internal Matrix4x4 World=>layout.World;
    internal float Width=>layout.Width;
    internal (Vector3 Start,Vector3 End)? Pointer {get;private set;}
    internal EditorVrPanel(int process):this(process,new SteamVrPanelOverlay(),true){}
    internal EditorVrPanel(int process,IPanelOverlay overlay,bool capture)
    {
        this.process=process;this.overlay=overlay;
        try
        {
            Check(overlay.CreateOverlay("local.johnny5vr.editor."+process,"Animation Editor workspace",ref handle));
            Check(overlay.SetOverlayWidthInMeters(handle,layout.Width));Check(overlay.SetOverlayInputMethod(handle,VROverlayInputMethod.None));
            Check(overlay.CreateOverlay("local.johnny5vr.editor.cursor."+process,"Editor pointer",ref cursor));
            Check(overlay.SetOverlayWidthInMeters(cursor,.018f));Check(overlay.SetOverlaySortOrder(cursor,1));Check(overlay.SetOverlayInputMethod(cursor,VROverlayInputMethod.None));
            var pixels=new byte[32*32*4];
            for(int y=0;y<32;y++)for(int x=0;x<32;x++)
            {
                int dx=Math.Abs(x-16),dy=Math.Abs(y-16);bool cross=dx<=1&&dy<=11||dy<=1&&dx<=11;bool outline=dx<=2&&dy<=12||dy<=2&&dx<=12;
                if(!outline)continue;int i=(y*32+x)*4;pixels[i]=cross?(byte)15:(byte)0;pixels[i+1]=pixels[i+2]=cross?(byte)255:(byte)0;pixels[i+3]=255;
            }
            Upload(cursor,pixels,32,32);if(capture)_=Task.Run(CaptureLoop);
        }
        catch{Dispose();throw;}
    }
    internal bool Visible=>visible;
    internal void AcceptFrame(EditorDesktopFrame frame)
    {
        var start=BridgeTiming.Start();var image=EditorPanelChrome.Draw(frame,Volatile.Read(ref chrome));BridgeTiming.Stop(BridgeTiming.Section.PanelChrome,start);
        Interlocked.Exchange(ref pending,new(frame,image));
    }
    private static void Check(EVROverlayError error){if(error!=EVROverlayError.None)throw new InvalidOperationException("VR editor panel: "+error);}
    private static void Log(Exception ex){try{Diagnostics.Log(ex);}catch{}}
    private async Task CaptureLoop()
    {
        EditorDesktopFrame? previous=null;PanelChromeState? oldChrome=null;
        while(!stop.IsCancellationRequested)
        {
            try
            {
                if(visible&&CaptureSource?.Invoke() is {} frame)
                {
                    var state=Volatile.Read(ref chrome);
                    if(!ReferenceEquals(frame,previous)||state!=oldChrome){AcceptFrame(frame);previous=frame;oldChrome=state;}
                }
            }
            catch(Exception ex){Log(ex);}
            try{await Task.Delay(67,stop.Token);}catch(OperationCanceledException){return;}
        }
    }
    internal void Toggle()
    {
        visible=!visible;Release();blocked=true;
        try{if(!visible){overlay.HideOverlay(handle);shown=false;}else if(last!=null){Check(overlay.ShowOverlay(handle));shown=true;}}catch(Exception ex){Recover(ex);}
    }
    internal void ToggleLock(){layout.Locked=!layout.Locked;Release();blocked=true;SetChrome(PanelTarget.None,layout.Locked?"Layout locked · Click Unlock to move or resize":"Layout unlocked · Trigger-grab the top bar or a corner");}
    internal void Recenter(){Release();layout.Width=1.8f;visible=true;positioned=false;blocked=true;Error="";retryAfter=0;SetChrome(PanelTarget.None,"Workspace restored to a comfortable position and size");}
    internal void Suspend(){Release();blocked=true;}
    internal static Matrix4x4 Place(Matrix4x4 head,Matrix4x4 origin,bool left=true)
    {
        var h=head*origin;var forward=Vector3.TransformNormal(-Vector3.UnitZ,h);forward.Y=0;forward=Vector3.Normalize(forward.LengthSquared()<.001f?-Vector3.UnitZ:forward);
        var right=Vector3.Cross(forward,Vector3.UnitY);var position=h.Translation+forward*1.8f+(left?-right*.95f:Vector3.Zero)-Vector3.UnitY*.08f;
        var facing=position-h.Translation;facing.Y=0;return Matrix4x4.CreateWorld(position,Vector3.Normalize(facing),Vector3.UnitY);
    }
    internal bool Step(Matrix4x4 head,Matrix4x4 origin,Matrix4x4? controller,ControllerReading input,bool focus,bool menuOpen,float seconds)
    {
        try{return StepCore(head,origin,controller,input,focus,menuOpen,Math.Clamp(float.IsFinite(seconds)?seconds:0,0,.05f));}
        catch(Exception ex){Recover(ex);return true;}
        finally{selectWasDown=input.Select;backWasDown=input.Back;}
    }
    private bool StepCore(Matrix4x4 head,Matrix4x4 origin,Matrix4x4? controller,ControllerReading input,bool focus,bool menuOpen,float seconds)
    {
        Pointer=null;
        if(!visible){Release();return false;}
        if(!EditorPanelLayout.Finite(head)||!EditorPanelLayout.Finite(origin)||!Matrix4x4.Invert(origin,out var inverseOrigin)){Suspend();return false;}
        if(!positioned){layout.World=Place(head,origin);positioned=true;}
        if(layout.Drag==PanelTarget.None&&Interlocked.Exchange(ref pending,null) is {} next)
        {
            last=next;layout.Aspect=next.Image.Height/(float)next.Image.Width;
            var uploadStart=BridgeTiming.Start();
            Upload(handle,next.Image.Rgba,(uint)next.Image.Width,(uint)next.Image.Height);if(!shown){Check(overlay.ShowOverlay(handle));shown=true;}
            BridgeTiming.Stop(BridgeTiming.Section.PanelUpload,uploadStart);
        }
        Position(inverseOrigin);
        if(!focus||menuOpen||controller==null||last==null||!EditorPanelLayout.Finite(controller.Value)){Suspend();return false;}
        if(Environment.TickCount64<retryAfter)return true;
        if(!input.Select&&!input.Back)blocked=false;
        if(blocked)return true;
        var c=controller.Value;var controllerWorld=c*origin;
        var direction=Vector3.TransformNormal(-Vector3.UnitZ,c);
        if(direction.LengthSquared()<1e-10f){Suspend();return false;}direction=Vector3.Normalize(direction);
        var rayFrom=Vector3.Transform(c.Translation,origin);var rayDirection=Vector3.Normalize(Vector3.TransformNormal(direction,origin));
        if(layout.Drag!=PanelTarget.None)
        {
            if(!input.Select){layout.End();SetChrome(PanelTarget.None,"Placed · Grab the top bar to move, or a corner to resize");}
            else layout.Update(controllerWorld,rayFrom,rayDirection);
            Position(inverseOrigin);HideCursor();return true;
        }
        bool hit=EditorPanelLayout.RayPlane(layout.World,rayFrom,rayDirection,out var at);
        if(!Matrix4x4.Invert(layout.World,out var inversePanel))hit=false;
        var local=hit?Vector3.Transform(at,inversePanel):Vector3.Zero;
        var uv=Coordinates(local,layout.Width,layout.Width*layout.Aspect);
        hit=hit&&uv.X>=0&&uv.X<=1&&uv.Y>=0&&uv.Y<=1;
        if(!hit){ReleaseMouse();HideCursor();pointerReady=false;SetChrome(PanelTarget.None,Hint(PanelTarget.None));return false;}
        Pointer=(rayFrom,at);
        if(!pointerReady){smoothUv=uv;pointerReady=true;}else smoothUv=Vector2.Lerp(smoothUv,uv,1-MathF.Exp(-seconds*35));
        var target=EditorPanelChrome.Hit(last.Image.Width,last.Image.Height,smoothUv.X,smoothUv.Y);
        SetChrome(target,Hint(target));
        var physical=layout.World*inverseOrigin;
        var pointerLocal=new Vector3((smoothUv.X-.5f)*layout.Width,(.5f-smoothUv.Y)*layout.Width*layout.Aspect,.002f);
        Pointer=(rayFrom,Vector3.Transform(pointerLocal,layout.World));
        var cursorPose=physical;cursorPose.Translation=Vector3.Transform(pointerLocal,physical);
        var nativeCursor=Native(cursorPose);Check(overlay.SetOverlayTransformAbsolute(cursor,ETrackingUniverseOrigin.TrackingUniverseStanding,ref nativeCursor));
        if(!cursorShown){Check(overlay.ShowOverlay(cursor));cursorShown=true;}
        bool press=input.Select&&!selectWasDown;
        if(press&&target!=PanelTarget.Content)
        {
            ReleaseMouse();
            if(layout.Begin(target,controllerWorld,at)){GrabFeedback?.Invoke();SetChrome(target,target==PanelTarget.Move?"Moving · Release trigger to place":"Resizing · Release trigger to set size");}
            else Activate(target);
            return true;
        }
        if(target!=PanelTarget.Content){ReleaseMouse();return true;}
        float x=smoothUv.X*last.Image.Width-last.Image.Content.X,y=smoothUv.Y*last.Image.Height-last.Image.Content.Y;
        var contentUv=new Vector2(x/last.Desktop.Width,y/last.Desktop.Height);
        bool moved=EditorDesktop.Move(last.Desktop,process,contentUv.X,contentUv.Y,press||input.Back&&!backWasDown);
        if(!moved){ReleaseMouse();return true;}
        if(press){EditorDesktop.Button(true);triggerHeld=true;}
        if(!input.Select&&triggerHeld){EditorDesktop.Button(false);triggerHeld=false;}
        if(input.Back&&!backWasDown){EditorDesktop.Button(true,true);rightHeld=true;}
        if(!input.Back&&rightHeld){EditorDesktop.Button(false,true);rightHeld=false;}
        if(!triggerHeld&&!rightHeld&&Environment.TickCount64-lastScroll>90&&(Math.Abs(input.Right.X)>.25f||Math.Abs(input.Right.Y)>.25f))
        {
            var p=EditorDesktop.Map(last.Desktop,contentUv.X,contentUv.Y);lastScroll=Environment.TickCount64;
            Navigate?.Invoke(new(p.X,p.Y,Math.Abs(input.Right.X)>.25f?input.Right.X:0,Math.Abs(input.Right.Y)>.25f?input.Right.Y:0));
        }
        return true;
    }
    private void Activate(PanelTarget target)
    {
        switch(target)
        {
            case PanelTarget.PlayPause:Command?.Invoke(VrEditorCommand.PlayPause);break;
            case PanelTarget.Stop:Command?.Invoke(VrEditorCommand.Stop);break;
            case PanelTarget.Undo:Command?.Invoke(VrEditorCommand.Undo);break;
            case PanelTarget.Redo:Command?.Invoke(VrEditorCommand.Redo);break;
            case PanelTarget.ShowRobot:Toggle();break;
            case PanelTarget.Recenter:Recenter();break;
            case PanelTarget.Lock:ToggleLock();break;
        }
    }
    private string Hint(PanelTarget target)=>target switch
    {
        PanelTarget.Move=>layout.Locked?"Layout locked · Click Unlock to move":"Hold trigger to move · Release to place",
        PanelTarget.TopLeft or PanelTarget.TopRight or PanelTarget.BottomLeft or PanelTarget.BottomRight=>layout.Locked?"Layout locked · Click Unlock to resize":"Hold trigger and stretch · Opposite corner stays fixed",
        PanelTarget.Content=>"Trigger: click / drag · B: right-click · Right stick: timeline pan / zoom, or panel scroll",
        _=>"A: menu · Bring Here restores position and size · Lock Layout prevents accidental movement"
    };
    private void Position(Matrix4x4 inverseOrigin)
    {
        if(!EditorPanelLayout.Finite(layout.World)||!float.IsFinite(layout.Width))throw new InvalidOperationException("Invalid editor panel placement.");
        var pose=Native(layout.World*inverseOrigin);Check(overlay.SetOverlayTransformAbsolute(handle,ETrackingUniverseOrigin.TrackingUniverseStanding,ref pose));Check(overlay.SetOverlayWidthInMeters(handle,layout.Width));
    }
    private void SetChrome(PanelTarget hover,string message)=>Volatile.Write(ref chrome,new(hover,layout.Locked,message));
    private void Upload(ulong target,byte[] pixels,uint width,uint height)
    {
        var pin=GCHandle.Alloc(pixels,GCHandleType.Pinned);try{Check(overlay.SetOverlayRaw(target,pin.AddrOfPinnedObject(),width,height,4));}finally{pin.Free();}
    }
    private void Recover(Exception ex)
    {
        if(Error!=ex.Message)Log(ex);Error=ex.Message;Release();blocked=true;retryAfter=Environment.TickCount64+1000;
        SetChrome(PanelTarget.None,"Panel interaction paused · Release controls and retry; A → Bring editor workspace here");
    }
    private void ReleaseMouse()
    {
        bool left=triggerHeld,right=rightHeld;triggerHeld=rightHeld=false;
        try{if(left)EditorDesktop.Button(false);}catch(Exception ex){Log(ex);}
        try{if(right)EditorDesktop.Button(false,true);}catch(Exception ex){Log(ex);}
    }
    internal static Vector2 Coordinates(Vector3 local,float width,float height)=>new(local.X/width+.5f,.5f-local.Y/height);
    private void HideCursor(){if(cursorShown&&cursor!=0){try{overlay.HideOverlay(cursor);}catch(Exception ex){Log(ex);}cursorShown=false;}}
    private void Release(){ReleaseMouse();layout.End();Pointer=null;HideCursor();pointerReady=false;}
    internal static HmdMatrix34_t Native(Matrix4x4 m)=>new(){m0=m.M11,m1=m.M21,m2=m.M31,m3=m.M41,m4=m.M12,m5=m.M22,m6=m.M32,m7=m.M42,m8=m.M13,m9=m.M23,m10=m.M33,m11=m.M43};
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        stop.Cancel();Release();
        foreach(ulong item in new[]{cursor,handle})if(item!=0){try{overlay.HideOverlay(item);overlay.DestroyOverlay(item);}catch(Exception ex){Log(ex);}}
        cursor=handle=0;
        overlay.Dispose();
    }
}
