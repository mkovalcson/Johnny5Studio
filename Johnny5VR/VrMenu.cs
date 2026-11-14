using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;
using Valve.VR;

namespace Johnny5VR;

internal sealed record MenuRow(string Label,Action Activate)
{ internal Func<float>? SliderValue {get;init;} internal Action<float>? SetSlider {get;init;} internal float SliderStep {get;init;}=.05f; }
internal sealed class MenuNavigation
{
    internal bool Visible {get;private set;}
    internal int Selected {get;set;}
    private bool menuHeld,selectHeld,backHeld,stickHeld;
    internal (bool Opened,bool Activate,bool Back) Step(ControllerReading input,bool focus,int count)
    {
        bool opened=false,activate=false,back=false;
        if(focus)
        {
            if(input.Menu&&!menuHeld){Visible=!Visible;opened=Visible;}
            if(Visible)
            {
                float y=Math.Abs(input.Move.Y)>.5f?input.Move.Y:input.Right.Y;
                if(Math.Abs(y)>.6f&&!stickHeld&&count>0){Selected=(Selected+(y>0?-1:1)+count)%count;stickHeld=true;}
                if(Math.Abs(y)<.25f)stickHeld=false;
                activate=input.Select&&!selectHeld&&!opened;back=input.Back&&!backHeld;
            }
        }
        menuHeld=input.Menu;selectHeld=input.Select;backHeld=input.Back;
        if(!focus)stickHeld=true;
        Selected=count==0?0:Math.Clamp(Selected,0,count-1);
        return(opened,activate,back);
    }
    internal void Open(){Visible=true;selectHeld=true;}
    internal void Close()=>Visible=false;
}

internal sealed class VrMenu:IDisposable
{
    private readonly IPanelOverlay overlay;
    private ulong handle,cursor;
    private bool shown,cursorShown,disposed;
    private int firstVisible,lastContentSelection;
    private readonly Dictionary<string,(int Selected,int First)> pagePositions=new();
    private long retryAfter;
    private Matrix4x4 world=Matrix4x4.Identity;
    internal readonly MenuNavigation Navigation=new();
    internal MenuRow[] Rows {get;private set;}=[];
    internal bool FollowSelections {get;set;}
    internal bool LiveFollowing {get;set;}
    internal Action? RestoreRegularMenu;
    private bool regularMenuHeld;
    internal bool LivePlaybackStatus {get;set;}
    internal bool LivePoseStatus {get;set;}
    internal bool LiveBodyMotionStatus {get;set;}
    internal bool LiveBodyRecordingStatus {get;set;}
    private string title="Johnny 5",detail="",lastImage="";
    internal string Error {get;private set;}="";
    internal (Vector3 Start,Vector3 End)? Pointer {get;private set;}
    internal Action? GoBack;
    private long nextSliderAdjust;
    internal VrMenu(int adapter=0):this(new SteamVrPanelOverlay(adapter)){}
    internal VrMenu(IPanelOverlay overlay)
    {
        this.overlay=overlay;
        try
        {
            Check(overlay.CreateOverlay("local.johnny5vr.playback."+Environment.ProcessId,"Johnny 5 playback",ref handle),"create menu");
            Check(overlay.SetOverlaySortOrder(handle,10),"menu order");Check(overlay.SetOverlayWidthInMeters(handle,1.05f),"menu width");
            Check(overlay.SetOverlayInputMethod(handle,VROverlayInputMethod.None),"menu input");
            Check(overlay.CreateOverlay("local.johnny5vr.menu.cursor."+Environment.ProcessId,"Menu pointer",ref cursor),"create menu pointer");
            Check(overlay.SetOverlaySortOrder(cursor,11),"pointer order");Check(overlay.SetOverlayWidthInMeters(cursor,.018f),"pointer width");
            var pixels=new byte[24*24*4];for(int y=0;y<24;y++)for(int x=0;x<24;x++)if((x-12)*(x-12)+(y-12)*(y-12)<70){int i=(y*24+x)*4;pixels[i]=30;pixels[i+1]=pixels[i+2]=pixels[i+3]=255;}
            Upload(cursor,pixels,24,24);
        }
        catch{Dispose();throw;}
    }
    private static void Check(EVROverlayError error,string operation){if(error!=EVROverlayError.None)throw new InvalidOperationException($"VR menu ({operation}): {error}");}
    internal void Set(string heading,MenuRow[] rows,string message="")
    {
        pagePositions[title]=(Navigation.Selected<Rows.Length?Navigation.Selected:lastContentSelection,firstVisible);
        FollowSelections=false;LiveFollowing=false;title=heading;Rows=rows.Where(r=>r.Label!="Back"&&r.Label!="Close menu"&&!r.Label.StartsWith("Back to ",StringComparison.Ordinal)&&r.Label!="Return to calibration / close menu").ToArray();detail=message;
        var saved=pagePositions.GetValueOrDefault(heading);
        Navigation.Selected=Math.Clamp(saved.Selected,0,Rows.Length+1);lastContentSelection=Math.Min(Navigation.Selected,Math.Max(0,Rows.Length-1));firstVisible=Math.Clamp(saved.First,0,Math.Max(0,Rows.Length-8));lastImage="";
        LivePlaybackStatus=false;LivePoseStatus=false;LiveBodyMotionStatus=false;LiveBodyRecordingStatus=false;
    }

    internal void Detail(string message)=>detail=message;
    internal void PlaybackStatus(string message,bool running)
    {
        if(!LivePlaybackStatus)return;detail=message;
        int index=Array.FindIndex(Rows,r=>r.Label is "Pause" or "Play / Resume");
        if(index>=0)Rows[index]=Rows[index] with{Label=running?"Pause":"Play / Resume"};
    }
    internal static int FooterAt(Vector2 uv)=>uv.Y>=820f/900&&uv.Y<=882f/900?(uv.X>=30f/1100&&uv.X<=530f/1100?0:uv.X>=570f/1100&&uv.X<=1070f/1100?1:-1):-1;
    internal static float SliderAt(float u)=>Math.Clamp((u*1100-600)/400,0,1);
    internal static int RowAt(Vector2 uv,int first,int count)
    {
        float x=uv.X*1100,y=uv.Y*900-150;
        int slot=(int)Math.Floor(y/62);
        return x>=30&&x<=1070&&slot>=0&&slot<8&&y-slot*62<=54&&first+slot<count?first+slot:-1;
    }
    internal bool Step(ControllerReading input,bool focus,Matrix4x4 head,Matrix4x4? controller=null)
    {
        Pointer=null;
        if(focus&&FollowSelections&&input.Menu&&!regularMenuHeld&&RestoreRegularMenu!=null)
        {Navigation.Close();RestoreRegularMenu();}
        regularMenuHeld=input.Menu;
        try{return StepCore(input,focus,head,controller);}
        catch(Exception ex)
        {
            if(Error!=ex.Message){try{Diagnostics.Log(ex);}catch{}}
            Error=ex.Message;lastImage="";retryAfter=Environment.TickCount64+500;HideCursor();
            return Navigation.Visible;
        }
    }
    private bool StepCore(ControllerReading input,bool focus,Matrix4x4 head,Matrix4x4? controller)
    {
        int before=Navigation.Selected;Vector2? hover=null;
        if(Navigation.Visible&&focus&&controller is {} c&&EditorPanelLayout.Finite(c)&&shown)
        {
            var direction=Vector3.TransformNormal(-Vector3.UnitZ,c);
            if(EditorPanelLayout.RayPlane(world,c.Translation,direction,out var point)&&Matrix4x4.Invert(world,out var inverse))
            {
                var uv=EditorVrPanel.Coordinates(Vector3.Transform(point,inverse),1.05f,1.05f*900/1100);
                if(uv.X>=0&&uv.X<=1&&uv.Y>=0&&uv.Y<=1)
                {
                    hover=uv;Pointer=(c.Translation,point);
                    int footer=FooterAt(uv);int row=footer>=0?Rows.Length+footer:RowAt(uv,firstVisible,Rows.Length);
                    if(row>=0&&input.Move.Length()<.3f&&input.Right.Length()<.3f)Navigation.Selected=row;
                }
            }
        }
        before=Navigation.Selected;bool wasVisible=Navigation.Visible;var result=Navigation.Step(input,focus,Rows.Length+2);
        if(Navigation.Selected<Rows.Length)lastContentSelection=Navigation.Selected;
        if(before!=Navigation.Selected&&Navigation.Selected<Rows.Length)firstVisible=Math.Max(0,Math.Min(Navigation.Selected-3,Rows.Length-8));
        if(result.Opened||Navigation.Visible&&!shown)
        {
            var forward=Vector3.TransformNormal(-Vector3.UnitZ,head);forward.Y=0;
            if(forward.LengthSquared()<.001f)forward=-Vector3.UnitZ;forward=Vector3.Normalize(forward);
            var position=head.Translation+forward*1.3f;position.Y-=.12f;
            world=Matrix4x4.CreateWorld(position,forward,Vector3.UnitY);
        }
        if(!Navigation.Visible){if(shown){overlay.HideOverlay(handle);shown=false;}HideCursor();return wasVisible;}
        if(Environment.TickCount64<retryAfter){HideCursor();return true;}
        var pose=EditorVrPanel.Native(world);Check(overlay.SetOverlayTransformAbsolute(handle,ETrackingUniverseOrigin.TrackingUniverseStanding,ref pose),"menu placement");
        bool slider=Navigation.Selected<Rows.Length&&Rows[Navigation.Selected].SetSlider!=null;
        if(slider&&shown&&focus)
        {
            var row=Rows[Navigation.Selected];
            if(input.Select&&hover is {} position&&position.X>=570f/1100&&position.X<=1030f/1100&&RowAt(position,firstVisible,Rows.Length)==Navigation.Selected)
                row.SetSlider!(SliderAt(position.X));
            else if(Math.Abs(input.Right.X)>.4f&&Environment.TickCount64>=nextSliderAdjust)
            {nextSliderAdjust=Environment.TickCount64+140;row.SetSlider!(Math.Clamp(row.SliderValue!()+MathF.Sign(input.Right.X)*row.SliderStep,0,1));}
        }
        if(shown&&(result.Back||result.Activate&&Navigation.Selected==Rows.Length)){if(GoBack!=null)GoBack();else Navigation.Close();}
        else if(shown&&result.Activate&&Navigation.Selected==Rows.Length+1)Navigation.Close();
        else if(result.Activate&&shown&&Navigation.Selected<Rows.Length&&!slider)Rows[Navigation.Selected].Activate();
        if(!Navigation.Visible){overlay.HideOverlay(handle);shown=false;HideCursor();return true;}
        string imageKey=title+"|"+detail+"|"+Navigation.Selected+"|"+firstVisible+"|"+string.Join('|',Rows.Select(r=>r.Label+"@"+r.SliderValue?.Invoke().ToString("F3")));
        if(imageKey!=lastImage){using var bitmap=Draw(title,detail,Rows.Select(r=>r.Label).ToArray(),Navigation.Selected,firstVisible,Rows.Select(r=>r.SliderValue?.Invoke()).ToArray());UploadBitmap(bitmap);lastImage=imageKey;}
        if(!shown){Check(overlay.ShowOverlay(handle),"show menu");shown=true;}
        if(hover is {} at&&focus)
        {
            var cursorPose=world;cursorPose.Translation=Vector3.Transform(new((at.X-.5f)*1.05f,(.5f-at.Y)*1.05f*900/1100,.003f),world);
            var native=EditorVrPanel.Native(cursorPose);Check(overlay.SetOverlayTransformAbsolute(cursor,ETrackingUniverseOrigin.TrackingUniverseStanding,ref native),"pointer placement");
            if(!cursorShown){Check(overlay.ShowOverlay(cursor),"show pointer");cursorShown=true;}
        }
        else HideCursor();
        Error="";return true;
    }
    internal static Bitmap Draw(string title,string detail,string[] rows,int selected,int? firstVisible=null,float?[]? sliders=null,float textureDpi=96)
    {
        var bitmap=new Bitmap(1100,900,PixelFormat.Format32bppArgb);bitmap.SetResolution(textureDpi,textureDpi);using var g=Graphics.FromImage(bitmap);
        g.Clear(Color.FromArgb(255,22,29,40));g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var font=new Font("Segoe UI",28,FontStyle.Regular,GraphicsUnit.Pixel);using var small=new Font("Segoe UI",19,FontStyle.Regular,GraphicsUnit.Pixel);
        var titleArea=new RectangleF(35,22,1030,66);using var heading=VrTextureText.FitFont(g,title,titleArea.Size,38,FontStyle.Bold);
        g.DrawString(title,heading,Brushes.White,titleArea);
        g.DrawString("Point or stick: browse    Trigger: select    A: show / hide    B: back",small,Brushes.LightCyan,35,92);
        int start=firstVisible??Math.Max(0,Math.Min(selected-3,rows.Length-8));
        using var format=new StringFormat{Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap,LineAlignment=StringAlignment.Center};
        for(int i=start;i<Math.Min(rows.Length,start+8);i++)
        {
            var rect=new RectangleF(30,150+(i-start)*62,1040,54);
            if(i==selected){using var brush=new SolidBrush(Color.FromArgb(44,96,146));g.FillRectangle(brush,rect);}
            if(sliders!=null&&sliders[i] is {} value)
            {
                g.FillRectangle(Brushes.DimGray,600,rect.Y+28,400,7);g.FillRectangle(Brushes.Turquoise,600,rect.Y+28,400*value,7);
                g.FillEllipse(Brushes.PaleGoldenrod,592+400*value,rect.Y+20,22,22);rect.Width=550;
            }
            string rowText=(i==selected?">  ":"    ")+rows[i];
            using var rowFont=VrTextureText.FitFont(g,rowText,rect.Size,28);g.DrawString(rowText,rowFont,Brushes.White,rect,format);
        }
        var detailArea=new RectangleF(35,655,1030,148);using var help=VrTextureText.FitFont(g,detail,detailArea.Size,19);
        g.DrawString(detail,help,Brushes.LightGray,detailArea);
        foreach(var footer in new[]{(X:30,Text:"‹  Back",Index:rows.Length),(X:570,Text:"Close  ×",Index:rows.Length+1)})
        {
            var area=new RectangleF(footer.X,820,500,62);g.FillRectangle(selected==footer.Index?Brushes.SteelBlue:Brushes.DarkSlateGray,area);
            using var centered=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center};g.DrawString(footer.Text,font,Brushes.White,area,centered);
        }
        g.DrawString($"{Math.Min(rows.Length,selected+1)} / {rows.Length}",small,Brushes.Gray,940,125);
        return bitmap;
    }

    private void Upload(ulong target,byte[] pixels,uint width,uint height)
    {
        var pin=GCHandle.Alloc(pixels,GCHandleType.Pinned);
        try{Check(overlay.SetOverlayRaw(target,pin.AddrOfPinnedObject(),width,height,4),"GPU image upload");}finally{pin.Free();}
    }
    private void UploadBitmap(Bitmap bitmap)
    {
        var bits=bitmap.LockBits(new(0,0,bitmap.Width,bitmap.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
        var rgba=new byte[bitmap.Width*bitmap.Height*4];
        try{for(int y=0;y<bitmap.Height;y++)Marshal.Copy(bits.Scan0+y*bits.Stride,rgba,y*bitmap.Width*4,bitmap.Width*4);}finally{bitmap.UnlockBits(bits);}
        for(int i=0;i<rgba.Length;i+=4)(rgba[i],rgba[i+2])=(rgba[i+2],rgba[i]);
        Upload(handle,rgba,(uint)bitmap.Width,(uint)bitmap.Height);
    }
    private void HideCursor(){if(cursorShown){try{overlay.HideOverlay(cursor);}catch{}cursorShown=false;}}
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        foreach(ulong item in new[]{cursor,handle})if(item!=0){try{overlay.HideOverlay(item);overlay.DestroyOverlay(item);}catch{}}
        cursor=handle=0;overlay.Dispose();
    }
}
