using System.Numerics;
using System.Runtime.InteropServices;
using System.Globalization;
using Valve.VR;
namespace Johnny5VR;

// One rigid dorsal LCD per glove. No independent billboarding or moving A/B plates.
internal sealed class FollowControllerLabels:IDisposable
{
    private readonly IPanelOverlay overlay;
    private readonly ulong[] handles=new ulong[2];
    private readonly string[] previous=new string[2];
    internal const float Width=.075f;
    internal FollowControllerLabels(int adapter){overlay=new SteamVrPanelOverlay(adapter);}
    internal static Matrix4x4 DisplayWorld(Matrix4x4 controller)=>Matrix4x4.CreateRotationX(-MathF.PI/2)*Matrix4x4.CreateTranslation(0,.038f,.012f)*controller;
    internal static string ClockText(DateTime time)=>time.ToString("h:mm tt",CultureInfo.GetCultureInfo("en-US"));
    internal static Vector3 Anchor(Matrix4x4 controller,bool a)=>Vector3.Transform(new Vector3(0,.038f,a?.027f:-.003f),controller);
    internal void Draw(Matrix4x4 head,Matrix4x4? left,Matrix4x4? right,bool visible,bool mirrored,FollowMovementTarget target,bool fingerTracking=true,bool handMode=false)
    {
        string clock=ClockText(DateTime.Now);
        for(int i=0;i<2;i++)
        {
            var hand=i==0?left:right;
            if(!visible||hand==null){if(handles[i]!=0)Check(overlay.HideOverlay(handles[i]));continue;}
            if(handles[i]==0)
            {
                Check(overlay.CreateOverlay("local.johnny5vr.glove.lcd."+Environment.ProcessId+"."+i,"Glove LCD",ref handles[i]));
                Check(overlay.SetOverlayInputMethod(handles[i],VROverlayInputMethod.None));Check(overlay.SetOverlaySortOrder(handles[i],10));Check(overlay.SetOverlayWidthInMeters(handles[i],Width));
            }
            string key=i+"|"+mirrored+(i==0?"|"+clock+"|"+fingerTracking:"|"+handMode);
            if(previous[i]!=key)
            {
                using var bitmap=Display(i==0,mirrored,target,clock,fingerTracking,handMode);var data=bitmap.LockBits(new(0,0,640,400),System.Drawing.Imaging.ImageLockMode.ReadOnly,System.Drawing.Imaging.PixelFormat.Format32bppArgb);byte[] pixels=new byte[640*400*4];
                try{for(int y=0;y<400;y++)Marshal.Copy(data.Scan0+y*data.Stride,pixels,y*640*4,640*4);}finally{bitmap.UnlockBits(data);}
                for(int p=0;p<pixels.Length;p+=4)(pixels[p],pixels[p+2])=(pixels[p+2],pixels[p]);var pin=GCHandle.Alloc(pixels,GCHandleType.Pinned);
                try{Check(overlay.SetOverlayRaw(handles[i],pin.AddrOfPinnedObject(),640,400,4));}finally{pin.Free();}previous[i]=key;
            }
            var native=EditorVrPanel.Native(DisplayWorld(hand.Value));Check(overlay.SetOverlayTransformAbsolute(handles[i],ETrackingUniverseOrigin.TrackingUniverseStanding,ref native));Check(overlay.ShowOverlay(handles[i]));
        }
    }
    internal static Bitmap Display(bool left,bool mirrored,FollowMovementTarget target,string clock,bool fingerTracking=true,bool handMode=false)
    {
        var image=new Bitmap(640,400);using var g=Graphics.FromImage(image);g.Clear(Color.FromArgb(255,14,32,28));g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var header=new Font("Consolas",41,FontStyle.Bold,GraphicsUnit.Pixel);using var text=new Font("Consolas",48,FontStyle.Bold,GraphicsUnit.Pixel);using var small=new Font("Consolas",25,FontStyle.Bold,GraphicsUnit.Pixel);
        using var edge=new Pen(Color.FromArgb(95,126,106),4);g.DrawRectangle(edge,4,4,631,391);
        g.DrawString(left?clock:"FOLLOW",header,Brushes.PaleGreen,30,20);
        g.DrawString(left?(fingerTracking?"FTracking: On":"FTracking: Off"):(handMode?"Hand":"Movement"),
            small,left&&!fingerTracking?Brushes.Khaki:Brushes.PaleGreen,left?345:430,37);
        g.DrawLine(Pens.DarkSeaGreen,25,88,615,88);g.DrawLine(Pens.DarkSeaGreen,25,235,615,235);
        g.DrawString(left?"LEFT STICK CLICK: LASER":"RIGHT STICK CLICK: FTRACKING",small,Brushes.DarkSeaGreen,30,101);
        g.DrawString("B  "+(left?"Selections":"Stop / Back"),text,Brushes.PaleGreen,30,147);
        g.DrawString(left?"FOLLOW MAPPING":"MENU",small,Brushes.DarkSeaGreen,30,250);
        g.DrawString("A  "+(left?(mirrored?"Mirrored":"Unmirrored"):"Menu"),text,Brushes.PaleGreen,30,296);
        return image;
    }
    internal static Bitmap Plate(string button,string label)=>Display(true,true,FollowMovementTarget.Robot,"12:34 PM");
    private static void Check(EVROverlayError error){if(error!=EVROverlayError.None)throw new InvalidOperationException("Glove LCD: "+error);}
    public void Dispose(){foreach(var h in handles)if(h!=0)overlay.DestroyOverlay(h);overlay.Dispose();}
}
