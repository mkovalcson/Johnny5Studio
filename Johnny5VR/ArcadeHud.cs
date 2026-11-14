using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;
using Valve.VR;
namespace Johnny5VR;
internal sealed class ArcadeHud:IDisposable
{
    private readonly IPanelOverlay overlay;private ulong handle;private long nextPaint;
    private readonly Vector3 position;
    internal ArcadeHud(int adapter,Vector3 anchor)
    {
        position=anchor;overlay=new SteamVrPanelOverlay(adapter);
        Check(overlay.CreateOverlay("local.johnny5vr.arcade."+Environment.ProcessId,"Johnny 5 Laser Range score",ref handle));
        Check(overlay.SetOverlayWidthInMeters(handle,2));Check(overlay.SetOverlaySortOrder(handle,5));
    }
    internal void Draw(ArcadeGame game,Matrix4x4 head,Matrix4x4 origin,bool visible)
    {
        if(!visible){overlay.HideOverlay(handle);return;}
        if(Environment.TickCount64>=nextPaint)
        {
            nextPaint=Environment.TickCount64+200;using var bitmap=Image(game);var bits=bitmap.LockBits(new(0,0,bitmap.Width,bitmap.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
            var bytes=new byte[bitmap.Width*bitmap.Height*4];try{for(int y=0;y<bitmap.Height;y++)Marshal.Copy(bits.Scan0+y*bits.Stride,bytes,y*bitmap.Width*4,bitmap.Width*4);}finally{bitmap.UnlockBits(bits);}
            for(int i=0;i<bytes.Length;i+=4)(bytes[i],bytes[i+2])=(bytes[i+2],bytes[i]);var pin=GCHandle.Alloc(bytes,GCHandleType.Pinned);
            try{Check(overlay.SetOverlayRaw(handle,pin.AddrOfPinnedObject(),(uint)bitmap.Width,(uint)bitmap.Height,4));}finally{pin.Free();}
        }
        var forward=position-(head*origin).Translation;forward.Y=0;if(forward.LengthSquared()<.01f)forward=-Vector3.UnitZ;
        Matrix4x4.Invert(origin,out var inverse);var world=Matrix4x4.CreateWorld(position,Vector3.Normalize(forward),Vector3.UnitY)*inverse;var native=EditorVrPanel.Native(world);
        Check(overlay.SetOverlayTransformAbsolute(handle,ETrackingUniverseOrigin.TrackingUniverseStanding,ref native));Check(overlay.ShowOverlay(handle));
    }
    internal static Bitmap Image(ArcadeGame game)
    {
        var bitmap=new Bitmap(1400,350);using var g=Graphics.FromImage(bitmap);g.Clear(Color.FromArgb(255,17,28,40));g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var heading=new Font("Segoe UI",38,FontStyle.Bold,GraphicsUnit.Pixel);using var body=new Font("Segoe UI",27,FontStyle.Regular,GraphicsUnit.Pixel);
        g.DrawString("JOHNNY 5 · LASER RANGE",heading,Brushes.Gold,new RectangleF(28,14,1344,55));
        using var status=VrTextureText.FitFont(g,game.Status,new SizeF(1344,90),27);g.DrawString(game.Status,status,Brushes.LightCyan,new RectangleF(28,78,1344,90));
        string instruction=game.Phase switch{ArcadePhase.Introduction=>"Instructions · Pull a trigger to skip",ArcadePhase.Deploying=>"Johnny is moving to the start. Your lookout teleport follows automatically.",ArcadePhase.Disassembling=>"Critical damage · Please wait for disassembly",ArcadePhase.Reassembling=>"Reassembling in the safe bay · Shield protection follows",ArcadePhase.GameOver=>"Three lives used · Tap A for Restart or Exit",_=>game.Won?"Range cleared! Tap A for Restart or Exit":"Left stick: drive / turn · Right Y: aim · Trigger: fire"};
        using var help=VrTextureText.FitFont(g,instruction,new SizeF(1344,70),27);g.DrawString(instruction,help,Brushes.White,new RectangleF(28,176,1344,70));
        g.DrawString("Tap A: menu · Hold A: aim teleport arc · Release on a GREEN lookout to teleport",body,Brushes.Silver,new RectangleF(28,262,1344,70));return bitmap;
    }
    private static void Check(EVROverlayError error){if(error!=0)throw new InvalidOperationException("Laser Range score display: "+error);}
    public void Dispose(){if(handle!=0){overlay.HideOverlay(handle);overlay.DestroyOverlay(handle);handle=0;}overlay.Dispose();}
}
