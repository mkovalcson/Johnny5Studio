using System.Numerics;
using System.Runtime.InteropServices;
using System.Drawing.Imaging;
using Valve.VR;

namespace Johnny5VR;

internal sealed class PoseHandleOverlay:IDisposable
{
    private readonly IPanelOverlay overlay;
    private readonly List<(ulong Handle,string Image)> labels=[];
    private ulong dialHandle,statusHandle,optionsHandle,hoverHandle;
    private string dialImage="",displayedDialKey="",statusImage="",optionsImage="",hoverImage="";
    private readonly bool background;
    private sealed record Raster(byte[] Pixels,int Width,int Height);
    private sealed record PreparedLabel(int Index,string Key,Raster Image);
    private Task<PreparedLabel[]>? labelJob;
    private Task<(string Key,string Control,Raster Image)>? dialJob;
    internal Task? PendingLabels => labelJob;
    internal Task? PendingDial => dialJob;
    internal PoseHandleOverlay(int adapter):this(new SteamVrPanelOverlay(adapter)){}
    internal PoseHandleOverlay(IPanelOverlay overlay,bool background=true){this.overlay=overlay;this.background=background;}
    private static readonly object preloadLock=new();
    private static (string Key,Raster Image)? preloadedDial;
    private static string DialResourceKey(PoseDial dial)=>$"{dial.Control.Key}|{dial.Control.Name}|{dial.Minimum}|{dial.Maximum}|{dial.Control.Value:0.0}";
    internal static void Preload(Johnny5.VrBridge.VrPoseState state)
    {
        // Decode actual icons and exercise the same label/font paths before interaction.
        foreach(var control in state.Handles)
        {
            PoseControlIcons.For(control.Key);
            using var bitmap=new Bitmap(600,100);using var graphics=Graphics.FromImage(bitmap);
            DrawLabel(graphics,new(control,Vector3.Zero),false);
        }
        var selected=state.Handles.FirstOrDefault(h=>h.Key=="Servo:NeckTurn")??state.Handles.FirstOrDefault();
        if(selected!=null)
        {
            var range=RobotPoseInteraction.DialRange(selected);var dial=new PoseDial(selected,Vector3.Zero,range.Minimum,range.Maximum);
            using var bitmap=DrawDial(dial,false);var raster=Pixels(bitmap);
            lock(preloadLock)preloadedDial=(DialResourceKey(dial),raster);
        }
    }
    private static Raster PrepareDial(PoseDial dial,bool dragging)
    {
        lock(preloadLock)if(!dragging&&preloadedDial is {} ready&&ready.Key==DialResourceKey(dial))return ready.Image;
        using var bitmap=DrawDial(dial,dragging);return Pixels(bitmap);
    }
    internal void Draw(RobotPoseInteraction pose,Matrix4x4 head,Matrix4x4 inverseOrigin)
    {
        var right=new Vector3(pose.Orientation.M11,pose.Orientation.M12,pose.Orientation.M13);
        var up=new Vector3(pose.Orientation.M21,pose.Orientation.M22,pose.Orientation.M23);
        var banks=pose.Visible.GroupBy(t=>(X:MathF.Round(Vector3.Dot(t.Label,right),3),t.Width,t.Compact)).OrderBy(g=>g.Key.X)
            .Select(g=>g.OrderByDescending(t=>Vector3.Dot(t.Label,up)).ToArray()).ToArray();
        string token=pose.State?.Token??"";
        string Key(PoseTarget[] bank)
        {
            var center=(bank[0].Label+bank[^1].Label)/2;
            return token+"|"+string.Join(";",bank.Select(t=>$"{t.Control.Key}|{t.Control.Name}|{t.Control.Value:0.0}|{pose.Hovered==t.Control.Key||pose.Dial?.Control.Key==t.Control.Key}|{t.Width}|{t.Height}|{Vector3.Dot(t.Label-center,up):0.0000}"));
        }
        var keys=banks.Select(Key).ToArray();
        while(labels.Count<banks.Length)
        {
            ulong handle=0;Check(overlay.CreateOverlay("local.johnny5vr.pose."+Environment.ProcessId+"."+labels.Count,"Robot pose controls",ref handle));
            labels.Add((handle,""));Check(overlay.SetOverlayInputMethod(handle,VROverlayInputMethod.None));Check(overlay.SetOverlaySortOrder(handle,3));
        }
        void AcceptLabels()
        {
            if(labelJob is not {IsCompleted:true})return;
            var prepared=labelJob.GetAwaiter().GetResult();labelJob=null;
            foreach(var item in prepared)
                if(item.Index<keys.Length&&item.Key==keys[item.Index])
                {Upload(labels[item.Index].Handle,item.Image);labels[item.Index]=(labels[item.Index].Handle,item.Key);}
        }
        AcceptLabels();
        if(labelJob==null)
        {
            var missing=banks.Select((bank,i)=>(bank,i)).Where(p=>labels[p.i].Image!=keys[p.i]).ToArray();
            if(missing.Length>0)
            {
                string? hovered=pose.Hovered,selected=pose.Dial?.Control.Key;
                PreparedLabel[] Prepare()=>missing.Select(p=>new PreparedLabel(p.i,keys[p.i],RenderBank(p.bank,up,hovered,selected))).ToArray();
                labelJob=background?Task.Run(Prepare):Task.FromResult(Prepare());AcceptLabels();
            }
        }
        for(int i=0;i<labels.Count;i++)
        {
            var entry=labels[i];
            if(i>=banks.Length||!entry.Image.StartsWith(token+"|",StringComparison.Ordinal)){overlay.HideOverlay(entry.Handle);continue;}
            var bank=banks[i];Check(overlay.SetOverlayWidthInMeters(entry.Handle,bank[0].Width));
            var world=pose.Orientation;world.Translation=(bank[0].Label+bank[^1].Label)/2;world*=inverseOrigin;
            var native=EditorVrPanel.Native(world);Check(overlay.SetOverlayTransformAbsolute(entry.Handle,ETrackingUniverseOrigin.TrackingUniverseStanding,ref native));Check(overlay.ShowOverlay(entry.Handle));
        }
        if(pose.ExpandedLabel is {} enlarged&&!pose.Grabbing)
        {
            if(hoverHandle==0)
            {
                Check(overlay.CreateOverlay("local.johnny5vr.pose.hover."+Environment.ProcessId,"Expanded robot control",ref hoverHandle));
                Check(overlay.SetOverlayInputMethod(hoverHandle,VROverlayInputMethod.None));Check(overlay.SetOverlaySortOrder(hoverHandle,6));
            }
            string key=$"{enlarged.Control.Key}|{enlarged.Control.Name}|{enlarged.Control.Value:0.0}|{enlarged.Width}|{enlarged.Height}";
            if(key!=hoverImage)
            {
                using var bitmap=new Bitmap(600,Math.Max(1,(int)Math.Ceiling(enlarged.Height/enlarged.Width*600)));
                using(var g=Graphics.FromImage(bitmap)){g.Clear(Color.FromArgb(245,17,25,37));DrawLabel(g,enlarged,true);}
                Upload(hoverHandle,bitmap);hoverImage=key;
            }
            Check(overlay.SetOverlayWidthInMeters(hoverHandle,enlarged.Width));
            var world=pose.Orientation;world.Translation=enlarged.Label;world*=inverseOrigin;var native=EditorVrPanel.Native(world);
            Check(overlay.SetOverlayTransformAbsolute(hoverHandle,ETrackingUniverseOrigin.TrackingUniverseStanding,ref native));Check(overlay.ShowOverlay(hoverHandle));
        }
        else if(hoverHandle!=0)overlay.HideOverlay(hoverHandle);
        string message=pose.StatusMessage;
        if(message.Length==0&&banks.Length>0&&banks.Select((_,i)=>labels[i].Image.StartsWith(token+"|",StringComparison.Ordinal)).Any(ready=>!ready))message="Preparing pose controls…";
        if(pose.Dial is {} dial)
        {
            if(dialHandle==0)
            {
                Check(overlay.CreateOverlay("local.johnny5vr.pose.dial."+Environment.ProcessId,"Robot control range dial",ref dialHandle));
                Check(overlay.SetOverlayWidthInMeters(dialHandle,.60f));Check(overlay.SetOverlayInputMethod(dialHandle,VROverlayInputMethod.None));Check(overlay.SetOverlaySortOrder(dialHandle,4));
            }
            string scope=$"{token}|{dial.Control.Key}|{dial.Minimum}|{dial.Maximum}";
            string key=$"{scope}|{dial.Control.Value:0.0}|{pose.Grabbing}";
            void AcceptDial()
            {
                if(dialJob is not {IsCompleted:true})return;
                var ready=dialJob.GetAwaiter().GetResult();dialJob=null;
                if(ready.Control==scope){Upload(dialHandle,ready.Image);dialImage=ready.Key;displayedDialKey=ready.Control;}
            }
            AcceptDial();
            if(dialJob==null&&dialImage!=key)
            {
                bool dragging=pose.Grabbing;
                (string,string,Raster) Prepare()=> (key,scope,PrepareDial(dial,dragging));
                dialJob=background?Task.Run(Prepare):Task.FromResult(Prepare());AcceptDial();
            }
            if(displayedDialKey==scope)
            {
                var world=pose.Orientation;world.Translation=dial.Center;world*=inverseOrigin;var native=EditorVrPanel.Native(world);
                Check(overlay.SetOverlayTransformAbsolute(dialHandle,ETrackingUniverseOrigin.TrackingUniverseStanding,ref native));Check(overlay.ShowOverlay(dialHandle));
            }
            else{overlay.HideOverlay(dialHandle);message="Opening "+dial.Control.Name.Replace(" · Ganged","")+"…";}
        }
        else if(dialHandle!=0)overlay.HideOverlay(dialHandle);
        if(pose.Enabled&&pose.Dial!=null&&pose.Options.Length>0)
        {
            if(optionsHandle==0)
            {
                Check(overlay.CreateOverlay("local.johnny5vr.pose.options."+Environment.ProcessId,"Robot appearance and accessory",ref optionsHandle));
                Check(overlay.SetOverlayWidthInMeters(optionsHandle,RobotPoseInteraction.OptionsWidth));Check(overlay.SetOverlayInputMethod(optionsHandle,VROverlayInputMethod.None));Check(overlay.SetOverlaySortOrder(optionsHandle,4));
            }
            string image=$"{pose.State?.Appearance}|{pose.State?.Accessory}";
            if(image!=optionsImage){using var bitmap=DrawOptions(pose);Upload(optionsHandle,bitmap);optionsImage=image;}
            var world=pose.Orientation;world.Translation=pose.OptionsCenter;world*=inverseOrigin;var native=EditorVrPanel.Native(world);
            Check(overlay.SetOverlayTransformAbsolute(optionsHandle,ETrackingUniverseOrigin.TrackingUniverseStanding,ref native));Check(overlay.ShowOverlay(optionsHandle));
        }
        else if(optionsHandle!=0)overlay.HideOverlay(optionsHandle);
        DrawStatus(pose.Enabled?message:"",head,inverseOrigin);
    }
    private static Raster RenderBank(PoseTarget[] bank,Vector3 up,string? hovered,string? selected)
    {
        var center=(bank[0].Label+bank[^1].Label)/2;
        float height=Vector3.Dot(bank[0].Label-bank[^1].Label,up)+bank[0].Height;
        using var bitmap=new Bitmap(600,Math.Max(1,(int)Math.Ceiling(height/bank[0].Width*600)),PixelFormat.Format32bppArgb);
        using(var g=Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Transparent);
            foreach(var target in bank)
            {
                float y=bitmap.Height/2f-Vector3.Dot(target.Label-center,up)*600/bank[0].Width-bank[0].Height/bank[0].Width*300;
                var saved=g.Save();g.TranslateTransform(0,y);DrawLabel(g,target,hovered==target.Control.Key||selected==target.Control.Key);g.Restore(saved);
            }
        }
        return Pixels(bitmap);
    }
    private void DrawStatus(string message,Matrix4x4 head,Matrix4x4 inverseOrigin)
    {
        if(string.IsNullOrEmpty(message)){if(statusHandle!=0)overlay.HideOverlay(statusHandle);return;}
        if(statusHandle==0)
        {
            Check(overlay.CreateOverlay("local.johnny5vr.pose.status."+Environment.ProcessId,"Robot pose loading status",ref statusHandle));
            Check(overlay.SetOverlayWidthInMeters(statusHandle,.65f));Check(overlay.SetOverlayInputMethod(statusHandle,VROverlayInputMethod.None));Check(overlay.SetOverlaySortOrder(statusHandle,5));
        }
        if(message!=statusImage)
        {
            using var bitmap=new Bitmap(900,130);using(var g=Graphics.FromImage(bitmap))
            {
                g.Clear(Color.FromArgb(245,17,25,37));using var font=TextureFont(36,FontStyle.Bold);
                using var format=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center};
                g.DrawString(message,font,Brushes.LightCyan,new RectangleF(15,5,870,120),format);
            }
            Upload(statusHandle,bitmap);statusImage=message;
        }
        var forward=Vector3.TransformNormal(-Vector3.UnitZ,head);forward.Y=0;if(forward.LengthSquared()<.001f)forward=-Vector3.UnitZ;forward=Vector3.Normalize(forward);
        var world=Matrix4x4.CreateWorld(head.Translation+forward*.9f-Vector3.UnitY*.12f,forward,Vector3.UnitY)*inverseOrigin;
        var native=EditorVrPanel.Native(world);Check(overlay.SetOverlayTransformAbsolute(statusHandle,ETrackingUniverseOrigin.TrackingUniverseStanding,ref native));Check(overlay.ShowOverlay(statusHandle));
    }
    internal static void DrawLabel(Graphics g,PoseTarget target,bool selected)
    {
        using var accent=new SolidBrush(selected?Color.Gold:target.Control.Ganged?Color.MediumPurple:Color.Cyan);
        float height=target.Height/target.Width*600;
        if(target.Compact)
        {
            using var centered=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center};
            string name=target.Control.Name.Replace(" · Ganged","");
            var icon=PoseControlIcons.For(target.Control.Key);
            if(icon!=null)g.DrawImage(icon,new Rectangle(2,(int)(height/2-48),96,96));
            float pixels=82;
            while(pixels>16)
            {
                using var measure=TextureFont(pixels,FontStyle.Bold);
                if(g.MeasureString(name,measure,492,centered).Height<=height-46)break;
                pixels-=2;
            }
            using var title=TextureFont(pixels,FontStyle.Bold);using var value=TextureFont(34);
            g.DrawString(name,title,Brushes.Black,new RectangleF(105,2,492,height-46),centered);
            g.DrawString(name,title,accent,new RectangleF(103,0,492,height-46),centered);
            g.DrawString($"{target.Control.Value:0.0}",value,Brushes.White,new RectangleF(103,height-46,492,46),centered);
            return;
        }
        var sideIcon=PoseControlIcons.For(target.Control.Key);
        float iconSize=Math.Min(60,height-4);
        if(sideIcon!=null)g.DrawImage(sideIcon,new Rectangle(88,2,(int)iconSize,(int)iconSize));else g.FillEllipse(accent,4,10,18,18);
        string sideName=ServoAnimator.ControlLabels.Text(LabelName(target)).Replace(" · Ganged","").Replace(" Left"," L").Replace(" Right"," R");
        float size=Math.Min(62,Math.Max(16,(height-6)*.80f));
        using(var measure=TextureFont(size,FontStyle.Bold))size=Math.Min(size,size*455/Math.Max(455,g.MeasureString(sideName,measure).Width));
        using var font=TextureFont(size,FontStyle.Bold);using var number=TextureFont(Math.Min(size,28));
        using var format=new StringFormat{LineAlignment=StringAlignment.Center};
        g.DrawString(sideName,font,Brushes.Black,new RectangleF(152,2,448,height),format);
        g.DrawString(sideName,font,target.Control.Ganged?accent:Brushes.White,new RectangleF(150,0,448,height),format);
        g.DrawString($"{target.Control.Value:0.#}",number,accent,new RectangleF(0,0,80,height),format);
    }
    internal static string LabelName(PoseTarget target)=>target.Control.Key switch
    {
        "Servo:Microphone_RaiseLower"=>"Microphone Up/Down",
        "Servo:MFR_UpDown"=>"MFR Up/Down",
        "Servo:Whip_Antenna_RaiseLower"=>"Whip Antenna Up/Down",
        "Servo:Whip_Antenna_Rotate"=>"Whip Antenna Rotate",
        "Servo:URDFStageX"=>"Robot X", "Servo:URDFStageY"=>"Robot Y", "Servo:URDFStageHeading"=>"Robot Turn",
        _=>target.Control.Name
    };
    internal static Bitmap DrawOptions(RobotPoseInteraction pose)
    {
        var bitmap=new Bitmap(1024,432);using var g=Graphics.FromImage(bitmap);
        g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;g.Clear(Color.FromArgb(248,17,25,37));
        var right=new Vector3(pose.Orientation.M11,pose.Orientation.M12,pose.Orientation.M13);
        using var font=TextureFont(34,FontStyle.Bold);using var pen=new Pen(Color.LightSlateGray,3);
        using var format=new StringFormat{LineAlignment=StringAlignment.Center};
        foreach(var option in pose.Options)
        {
            float x=(Vector3.Dot(option.Center-pose.OptionsCenter,right)-option.Width/2)/RobotPoseInteraction.OptionsWidth*1024+512;
            float y=216-(option.Center.Y-pose.OptionsCenter.Y)/RobotPoseInteraction.OptionsHeight*432;
            bool selected=(option.Action==Johnny5.VrBridge.VrPoseAction.Appearance?pose.State?.Appearance:pose.State?.Accessory)==option.Value;
            g.DrawEllipse(pen,x+10,y-14,28,28);if(selected)g.FillEllipse(Brushes.Gold,x+16,y-8,16,16);
            g.DrawString(option.Name,font,selected?Brushes.Gold:Brushes.White,new RectangleF(x+48,y-45,option.Width/RobotPoseInteraction.OptionsWidth*1024-50,90),format);
        }
        return bitmap;
    }
    private static Font TextureFont(float pixels,FontStyle style=FontStyle.Regular) => new("Segoe UI",pixels,style,GraphicsUnit.Pixel);
    internal static Bitmap DrawDial(PoseDial dial,bool dragging,float textureDpi=96)
    {
        var bitmap=new Bitmap(1024,1024,PixelFormat.Format32bppArgb);bitmap.SetResolution(textureDpi,textureDpi);using var g=Graphics.FromImage(bitmap);
        g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;g.Clear(Color.FromArgb(248,17,25,37));
        using var title=TextureFont(37,FontStyle.Bold);using var text=TextureFont(31);using var large=TextureFont(53,FontStyle.Bold);
        using var accent=new SolidBrush(dial.Control.Ganged?Color.MediumPurple:Color.Cyan);
        using var centered=new StringFormat{Alignment=StringAlignment.Center};
        g.DrawString(ServoAnimator.ControlLabels.Text(dial.Control.Name),title,Brushes.White,new RectangleF(24,25,976,110),centered);
        g.DrawString(dial.Control.Ganged?"Ganged control · native editor units":"Individual control · native editor units",text,accent,new RectangleF(20,125,984,50),centered);
        const float cx=512,cy=486,radius=307;
        PointF At(float angle,float r){float a=angle*MathF.PI/180;return new(cx+MathF.Sin(a)*r,cy-MathF.Cos(a)*r);}
        using var track=new Pen(Color.FromArgb(75,94,116),20);g.DrawArc(track,cx-radius,cy-radius,2*radius,2*radius,135,270);
        using var tick=new Pen(Color.LightSlateGray,3);
        for(int i=0;i<=20;i++){float a=-135+i*13.5f;g.DrawLine(tick,At(a,radius-20),At(a,radius+15));}
        float defaultValue=RobotPoseInteraction.DefaultValue(dial.Control);
        if(defaultValue>=dial.Minimum&&defaultValue<=dial.Maximum)
        {
            float a=RobotPoseInteraction.ValueAngle(defaultValue,dial.Minimum,dial.Maximum);
            using var marker=new Pen(Color.FromArgb(106,237,159),8);g.DrawLine(marker,At(a,radius-50),At(a,radius+30));
        }
        using var needle=new Pen(Color.Gold,11){StartCap=System.Drawing.Drawing2D.LineCap.Round,EndCap=System.Drawing.Drawing2D.LineCap.Round};
        g.DrawLine(needle,new(cx,cy),At(RobotPoseInteraction.ValueAngle(dial.Control.Value,dial.Minimum,dial.Maximum),radius-40));
        g.FillEllipse(Brushes.Gold,cx-15,cy-15,30,30);
        g.DrawString($"{dial.Control.Value:0.0}",large,Brushes.White,new RectangleF(250,650,524,80),centered);
        g.DrawString($"{dial.Minimum:0.##}",text,Brushes.White,85,700);g.DrawString($"{dial.Maximum:0.##}",text,Brushes.White,new RectangleF(730,700,220,55),centered);
        using var green=new SolidBrush(Color.FromArgb(106,237,159));g.DrawString($"Green marker: default {defaultValue:0.##}",text,green,new RectangleF(20,765,984,48),centered);
        g.DrawString(dragging?"Hold trigger · point around arc · release to finish":"Hold trigger on dial · right stick up/down for fine steps",text,Brushes.White,new RectangleF(15,825,994,90),centered);
        using var button=new SolidBrush(Color.FromArgb(43,60,79));g.FillRectangle(button,10,912,1004,100);
        bool pageable=dial.Maximum-dial.Minimum<dial.Control.Maximum-dial.Control.Minimum;
        if(pageable){g.DrawString("< Range",text,Brushes.White,45,940);g.DrawString("Range >",text,Brushes.White,790,940);}
        g.DrawString("Reset default",text,green,new RectangleF(345,940,334,55),centered);
        return bitmap;
    }
    private static Raster Pixels(Bitmap bitmap)
    {
        int width=bitmap.Width,height=bitmap.Height;var data=bitmap.LockBits(new(0,0,width,height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);var bytes=new byte[width*height*4];
        try{for(int y=0;y<height;y++)Marshal.Copy(data.Scan0+y*data.Stride,bytes,y*width*4,width*4);}finally{bitmap.UnlockBits(data);}
        for(int p=0;p<bytes.Length;p+=4)(bytes[p],bytes[p+2])=(bytes[p+2],bytes[p]);return new(bytes,width,height);
    }
    private void Upload(ulong handle,Bitmap bitmap)=>Upload(handle,Pixels(bitmap));
    private void Upload(ulong handle,Raster image)
    {
        var pin=GCHandle.Alloc(image.Pixels,GCHandleType.Pinned);
        try{Check(overlay.SetOverlayRaw(handle,pin.AddrOfPinnedObject(),(uint)image.Width,(uint)image.Height,4));}finally{pin.Free();}
    }
    private static void Check(EVROverlayError error){if(error!=EVROverlayError.None)throw new InvalidOperationException("Pose labels: "+error);}
    public void Dispose(){foreach(var entry in labels)overlay.DestroyOverlay(entry.Handle);labels.Clear();if(dialHandle!=0)overlay.DestroyOverlay(dialHandle);dialHandle=0;if(hoverHandle!=0)overlay.DestroyOverlay(hoverHandle);hoverHandle=0;if(optionsHandle!=0)overlay.DestroyOverlay(optionsHandle);optionsHandle=0;if(statusHandle!=0)overlay.DestroyOverlay(statusHandle);statusHandle=0;overlay.Dispose();}
}
