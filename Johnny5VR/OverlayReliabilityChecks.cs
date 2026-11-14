using System.Numerics;
using System.Runtime.InteropServices;
using Johnny5.VrBridge;
using Valve.VR;

namespace Johnny5VR;

internal static class OverlayReliabilityChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var pose=Matrix4x4.CreateTranslation(.2f,1.2f,-.3f);var origin=Matrix4x4.CreateRotationY(.4f)*Matrix4x4.CreateTranslation(2,0,3);
        var ray=ControllerRays.FromPose(pose,origin)!.Value;
        check(Vector3.Distance(ray.Start,(pose*origin).Translation)<1e-6&&Math.Abs(Vector3.Distance(ray.Start,ray.End)-2.5)<1e-6,"Controller rays exist at full length without a panel hit.");
        var bad=pose;bad.M11=float.NaN;check(ControllerRays.FromPose(bad,origin)==null,"Invalid controller poses cannot create corrupt pointer geometry.");
        check(VrMenu.RowAt(new(.5f,251f/900),0,5)==1&&VrMenu.RowAt(new(.5f,.05f),0,5)==-1,"Ray pointing selects menu rows and ignores its header.");
        var fake=new MenuOverlay();using(var menu=new VrMenu(fake))
        {
            int chosen=0;menu.Set("Check",[new("First",()=>chosen=1),new("Second",()=>chosen=2),new("Third",()=>chosen=3)]);
            ControllerReading Input(bool a=false,bool select=false)=>new(Vector2.Zero,Vector2.Zero,"","",true,"",Menu:a,Select:select);
            var head=Matrix4x4.CreateTranslation(0,1.6f,0);fake.FailMenuImage=true;
            check(menu.Step(Input(a:true),true,head)&&menu.Error.Contains("GPU image upload")&&fake.Destroyed==0,"Menu RequestFailed is recoverable and cannot destroy the VR session overlays.");
            Thread.Sleep(550);menu.Step(Input(),true,head);
            check(menu.Error==""&&fake.MenuShown,"The menu retries its image and recovers from a transient upload failure.");
            var target=new Vector3(0,1.48f+(.5f-251f/900)*1.05f*900/1100,-1.3f);
            var controller=Matrix4x4.CreateWorld(head.Translation,Vector3.Normalize(target-head.Translation),Vector3.UnitY);
            menu.Step(Input(),true,head,controller);menu.Step(Input(select:true),true,head,controller);
            check(chosen==2&&menu.Pointer!=null,"A controller ray and trigger can choose a menu item without the sticks.");
        }
        var pixels=new byte[16*16*4];for(int i=0;i<pixels.Length;i+=4){pixels[i]=(byte)(i%251);pixels[i+1]=70;pixels[i+2]=190;pixels[i+3]=255;}
        var desktop=new VrDesktopFrame([new(7,-100,200,32,32,16,16,pixels)]);
        var decoded=DesktopWire.Decode(DesktopWire.Encode(desktop));
        check(decoded.Windows[0].Bgra.SequenceEqual(pixels)&&decoded.Windows[0].X==-100,"Editor-rendered desktop frames preserve pixels and negative monitor coordinates.");
        var composite=EditorDesktop.FromRendered(decoded)!;
        int centre=(16*composite.Width+16)*4;
        check(composite.DesktopBounds==new Rectangle(-100,200,32,32)&&Math.Abs(composite.Rgba[centre]-190)<2&&Math.Abs(composite.Rgba[centre+1]-70)<2,"WPF pixels compose with correct channels and screen mapping.");
        check(!EditorDesktop.HasRenderedContent(new(new byte[64*64*4],64,64,new(),[])),"A blank editor capture is rejected while retaining the last valid image.");
        bool rejected=false;try{DesktopWire.Decode(DesktopWire.Encode(desktop with{Windows=[desktop.Windows[0] with{Bgra=[1,2,3]}]}));}catch(InvalidDataException){rejected=true;}
        check(rejected,"Incomplete desktop pixel data cannot replace a good editor image.");
    }
    internal static void Gpu()
    {
        using var textures=new OverlayTextureStore(0);var pixels=new byte[1100*900*4];
        for(int i=0;i<pixels.Length;i+=4){pixels[i]=12;pixels[i+1]=90;pixels[i+2]=210;pixels[i+3]=255;}
        var pin=GCHandle.Alloc(pixels,GCHandleType.Pinned);var handles=new List<IntPtr>();
        try
        {
            EVROverlayError Accept(ulong id,ref Texture_t texture){if(texture.eType!=ETextureType.DirectX)throw new Exception("Not a GPU texture");handles.Add(texture.handle);return EVROverlayError.None;}
            for(int i=0;i<3;i++)textures.Submit(1,pin.AddrOfPinnedObject(),1100,900,4,Accept);
            if(handles[0]==handles[1]||handles[0]!=handles[2]||textures.Count!=1||!textures.ReadCurrentPixel(1).SequenceEqual(new byte[]{12,90,210,255}))throw new Exception("Overlay GPU buffers did not alternate or preserve pixels.");
            EVROverlayError Fail(ulong id,ref Texture_t texture)=>EVROverlayError.RequestFailed;
            pixels[0]=240;textures.Submit(1,pin.AddrOfPinnedObject(),1100,900,4,Fail);
            if(textures.ReadCurrentPixel(1)[0]!=12)throw new Exception("Failed overlay upload replaced the previous valid image.");
            File.WriteAllText(Path.Combine(Diagnostics.Folder,"overlay-gpu-check.txt"),"PASS: menu-sized Direct3D overlay textures preserve pixels, alternate reusable buffers, and retain the previous image after RequestFailed.");
        }
        finally{pin.Free();}
    }
    private sealed class MenuOverlay:IPanelOverlay
    {
        private ulong next;internal bool FailMenuImage,MenuShown;internal int Destroyed;
        public EVROverlayError CreateOverlay(string key,string name,ref ulong h){h=++next;return 0;}
        public EVROverlayError SetOverlayWidthInMeters(ulong h,float width)=>0;
        public EVROverlayError SetOverlayInputMethod(ulong h,VROverlayInputMethod m)=>0;
        public EVROverlayError SetOverlaySortOrder(ulong h,uint order)=>0;
        public EVROverlayError SetOverlayRaw(ulong h,IntPtr data,uint w,uint height,uint depth){if(h==1&&FailMenuImage){FailMenuImage=false;return EVROverlayError.RequestFailed;}return 0;}
        public EVROverlayError SetOverlayTransformAbsolute(ulong h,ETrackingUniverseOrigin origin,ref HmdMatrix34_t pose)=>0;
        public EVROverlayError ShowOverlay(ulong h){if(h==1)MenuShown=true;return 0;}
        public EVROverlayError HideOverlay(ulong h)=>0;
        public EVROverlayError DestroyOverlay(ulong h){Destroyed++;return 0;}
        public void Dispose(){}
    }
}
