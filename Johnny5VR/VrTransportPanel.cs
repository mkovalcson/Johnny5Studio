using System.Numerics;
using System.Runtime.InteropServices;
using Valve.VR;
namespace Johnny5VR;
internal sealed record TransportPanelState(string Title,double Time,bool Recording,bool HasTake,bool Following,string Status)
{
    internal bool Rehearsal {get;init;}
    internal bool Pending {get;init;}
    internal bool Replaying {get;init;}
    internal int Countdown {get;init;}
    internal double Elapsed {get;init;}
}
internal sealed class VrTransportPanel:IDisposable
{
    private readonly IPanelOverlay overlay;
    private ulong handle;
    private bool held,grabbing;
    private Matrix4x4? world;
    private Vector3 grabHand,grabPanel;
    private string previous="";
    private long nextPaint,retryAfter;
    private string lastError="";
    internal bool Enabled {get;set;}=true;
    internal Func<TransportPanelState>? Source;
    internal Action<int>? Action;
    internal (Vector3 Start,Vector3 End)? Pointer {get;private set;}
    internal VrTransportPanel(int adapter):this(new SteamVrPanelOverlay(adapter)){}
    internal VrTransportPanel(IPanelOverlay overlay){this.overlay=overlay;}
    internal void Recenter(){Enabled=true;world=null;grabbing=false;}
    internal void Suspend(){grabbing=false;if(handle!=0)overlay.HideOverlay(handle);Pointer=null;}
    internal static int Hit(Vector2 uv)
    {
        float x=uv.X*1200,y=uv.Y*330;
        if(y>=0&&y<=65&&x>=0&&x<=1200)return x>=1100?4:3;
        if(y<145||y>240)return -1;
        for(int i=0;i<3;i++)if(x>=30+i*390&&x<=390+i*390)return i;
        return -1;
    }
    internal static (int Action,string Label)[] Buttons(TransportPanelState s)=>
        s.Pending?[(1,"Cancel start")]:
        s.Recording||s.Replaying?[(1,"■  Stop")]:
        s.HasTake?[(5,"▶  Replay"),(6,"Keep"),(7,"Discard")]:
        s.Rehearsal?[(0,"Play / Pause"),(1,"■  Stop"),(2,"●  Record"),(8,"●  Overdub")]:
        [(0,"▶ / Ⅱ  Play / Pause"),(1,"■  Stop"),(2,"●  Record body")];
    internal static int Hit(Vector2 uv,TransportPanelState state)
    {
        int hit=Hit(uv);
        if(uv.Y*330<=65)return hit;
        if(uv.Y*330 is <145 or >240)return -1;
        var buttons=Buttons(state);float width=1170f/buttons.Length,x=uv.X*1200-30;
        if(x<0)return -1;
        int i=(int)(x/width);
        return i<buttons.Length&&x-i*width<=width-30?buttons[i].Action:-1;
    }
    internal static Matrix4x4 Placement(Matrix4x4 head)
    {
        var forward=Vector3.TransformNormal(-Vector3.UnitZ,head);forward.Y=0;if(forward.LengthSquared()<.001f)forward=-Vector3.UnitZ;forward=Vector3.Normalize(forward);
        var right=Vector3.Cross(forward,Vector3.UnitY);
        return Matrix4x4.CreateWorld(head.Translation+forward*1.2f-right*.65f-Vector3.UnitY*.45f,forward,Vector3.UnitY);
    }
    internal bool Step(Matrix4x4 head,Matrix4x4 origin,Matrix4x4? controller,ControllerReading input,bool visible,bool interactive)
    {
        Pointer=null;
        try
        {
            if(Environment.TickCount64<retryAfter)return false;
            if(!Enabled||!visible){Suspend();return false;}
            var state=Source?.Invoke()??new TransportPanelState("Transport",0,false,false,false,"");
            var worldHead=head*origin;world??=Placement(worldHead);
            int hovered=-1;Matrix4x4? hand=controller.HasValue?controller.Value*origin:null;
            bool capture=state.Pending||state.Recording;
            bool canUse=interactive&&(!state.Following||capture);
            if(canUse&&hand is {} ray&&EditorPanelLayout.RayPlane(world.Value,ray.Translation,Vector3.TransformNormal(-Vector3.UnitZ,ray),out var point)&&Matrix4x4.Invert(world.Value,out var inv))
            {
                var uv=EditorVrPanel.Coordinates(Vector3.Transform(point,inv),.72f,.72f*330/1200);
                if(uv.X>=0&&uv.X<=1&&uv.Y>=0&&uv.Y<=1){hovered=Hit(uv,state);if(!capture||hovered==1)Pointer=(ray.Translation,point);}
            }
            if(!canUse){grabbing=false;hovered=-1;}
            bool pressed=input.Select&&!held;
            if(canUse&&!capture&&pressed&&hovered==3&&hand.HasValue){grabbing=true;grabHand=hand.Value.Translation;grabPanel=world.Value.Translation;}
            if(grabbing&&input.Select&&hand.HasValue)
            {
                var next=grabPanel+hand.Value.Translation-grabHand;
                if(Vector3.Distance(next,worldHead.Translation) is >.35f and <3.5f){var value=world.Value;value.Translation=next;world=value;}
            }
            else if(!input.Select)grabbing=false;
            if(canUse&&pressed&&hovered>=0&&hovered is not (3 or 4))Action?.Invoke(hovered);
            if(canUse&&!capture&&pressed&&hovered==4){Enabled=false;Action?.Invoke(3);Suspend();return true;}
            if(handle==0){Check(overlay.CreateOverlay("local.johnny5vr.transport."+Environment.ProcessId,"J5 compact transport",ref handle));Check(overlay.SetOverlayInputMethod(handle,VROverlayInputMethod.None));Check(overlay.SetOverlayWidthInMeters(handle,.72f));Check(overlay.SetOverlaySortOrder(handle,7));}
            string key=$"{state.Title}|{state.Time:0.0}|{state.Recording}|{state.HasTake}|{state.Following}|{hovered}|{state.Status}|{state.Pending}|{state.Replaying}|{state.Countdown}|{state.Elapsed:0.0}|{state.Rehearsal}";
            if(key!=previous&&Environment.TickCount64>=nextPaint)
            {
                using var bitmap=Draw(state,hovered);var bits=bitmap.LockBits(new(0,0,1200,330),System.Drawing.Imaging.ImageLockMode.ReadOnly,System.Drawing.Imaging.PixelFormat.Format32bppArgb);var pixels=new byte[1200*330*4];
                try{for(int y=0;y<330;y++)Marshal.Copy(bits.Scan0+y*bits.Stride,pixels,y*1200*4,1200*4);}finally{bitmap.UnlockBits(bits);}
                for(int i=0;i<pixels.Length;i+=4)(pixels[i],pixels[i+2])=(pixels[i+2],pixels[i]);var pin=GCHandle.Alloc(pixels,GCHandleType.Pinned);
                try{Check(overlay.SetOverlayRaw(handle,pin.AddrOfPinnedObject(),1200,330,4));}finally{pin.Free();}previous=key;nextPaint=Environment.TickCount64+100;
            }
            Matrix4x4.Invert(origin,out var inverse);var native=EditorVrPanel.Native(world.Value*inverse);Check(overlay.SetOverlayTransformAbsolute(handle,ETrackingUniverseOrigin.TrackingUniverseStanding,ref native));Check(overlay.ShowOverlay(handle));
            lastError="";return canUse&&(capture?pressed&&hovered==1:hovered>=0||grabbing);
        }
        catch(Exception ex){if(lastError!=ex.Message)Diagnostics.Log(ex);lastError=ex.Message;retryAfter=Environment.TickCount64+500;Suspend();return false;}
        finally{held=input.Select;}
    }
    internal static Bitmap Draw(TransportPanelState state,int selected=-1)
    {
        var bitmap=new Bitmap(1200,330);using var g=Graphics.FromImage(bitmap);g.Clear(Color.FromArgb(248,22,29,40));g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var title=new Font("Segoe UI",29,FontStyle.Bold,GraphicsUnit.Pixel);using var button=new Font("Segoe UI",34,FontStyle.Bold,GraphicsUnit.Pixel);using var small=new Font("Segoe UI",28,FontStyle.Regular,GraphicsUnit.Pixel);
        g.FillRectangle(Brushes.DarkSlateGray,0,0,1200,65);g.DrawString((state.Rehearsal?"REHEARSAL":"TRANSPORT")+(state.Pending||state.Recording?"":"  ·  Hold Trigger here to move"),title,Brushes.LightCyan,25,14);if(!state.Pending&&!state.Recording)g.DrawString("×",button,Brushes.White,1135,10);
        using var trim=new StringFormat{Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap};
        g.DrawString(state.Title,title,Brushes.White,new RectangleF(25,85,865,45),trim);g.DrawString(state.Countdown>0?$"In {state.Countdown}…":state.Recording?$"{state.Elapsed:0.0} s":$"{state.Time:0.0} s",button,Brushes.PaleGoldenrod,950,82);
        var buttons=Buttons(state);float width=1170f/buttons.Length;
        for(int i=0;i<buttons.Length;i++)
        {
            var entry=buttons[i];var rect=new RectangleF(30+i*width,145,width-30,95);
            g.FillRectangle(selected==entry.Action?Brushes.SteelBlue:entry.Action is 2 or 8?Brushes.DarkRed:Brushes.DarkSlateBlue,rect);
            using var center=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center};g.DrawString(entry.Label,button,Brushes.White,rect,center);
        }
        string status=state.Pending||state.Status.StartsWith("Recording error:",StringComparison.Ordinal)?state.Status:state.Recording?"● RECORDING · Stop here or press Right B":state.Replaying?"Replaying draft with sequence audio · Stop to return":state.HasTake?$"Draft · {state.Elapsed:0.0} s · Keep adds one Undo step":state.Status;
        g.DrawString(status,small,state.Recording?Brushes.OrangeRed:Brushes.LightGray,new RectangleF(25,266,1150,45),trim);return bitmap;
    }
    private static void Check(EVROverlayError error){if(error!=EVROverlayError.None)throw new InvalidOperationException("Transport panel: "+error);}
    public void Dispose(){if(handle!=0)overlay.DestroyOverlay(handle);overlay.Dispose();}
}
