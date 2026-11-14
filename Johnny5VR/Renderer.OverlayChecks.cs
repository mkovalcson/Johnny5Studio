using System.Numerics;
using System.Runtime.InteropServices;
using SharpDX.Direct3D11;
using Valve.VR;
namespace Johnny5VR;
internal sealed partial class Renderer
{
    internal void CheckCapturedOverlayPixels(Action<bool,string> check)
    {
        if(cleanTarget==null)throw new InvalidOperationException("No Clean Output target.");
        const ulong red=ulong.MaxValue-1,green=ulong.MaxValue-2;
        using var source=new OverlayTextureStore(0);using var destination=new RenderTargetView(device,cleanTarget.Resolved);
        var pixels=new byte[4*4*4];var pin=GCHandle.Alloc(pixels,GCHandleType.Pinned);
        var projection=Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI/2,(float)cleanTarget.Width/cleanTarget.Height,.04f,150);
        void Upload(ulong handle,byte r,byte g,byte b,byte alpha)
        {
            for(int i=0;i<pixels.Length;i+=4){pixels[i]=r;pixels[i+1]=g;pixels[i+2]=b;pixels[i+3]=alpha;}
            source.Submit(handle,pin.AddrOfPinnedObject(),4,4,4,(ulong h,ref Texture_t texture)=>EVROverlayError.None);OverlayCapture.Image(handle,source.CurrentImage(handle));
        }
        void Background()=>context.ClearRenderTargetView(destination,new SharpDX.Mathematics.Interop.RawColor4(0,0,1,1));
        byte[] Pixel(int x,int y)
        {
            var desc=cleanTarget.Resolved.Description;desc.BindFlags=BindFlags.None;desc.CpuAccessFlags=CpuAccessFlags.Read;desc.Usage=ResourceUsage.Staging;desc.OptionFlags=ResourceOptionFlags.None;
            using var copy=new Texture2D(device,desc);context.CopyResource(cleanTarget.Resolved,copy);var map=context.MapSubresource(copy,0,MapMode.Read,SharpDX.Direct3D11.MapFlags.None);
            try{var pixel=new byte[4];Marshal.Copy(map.DataPointer+y*map.RowPitch+x*4,pixel,0,4);return pixel;}finally{context.UnmapSubresource(copy,0);}
        }
        try
        {
            OverlayCapture.Create(red);OverlayCapture.Width(red,1);OverlayCapture.Order(red,3);OverlayCapture.Pose(red,Matrix4x4.CreateTranslation(0,0,-2),true);Upload(red,255,0,0,128);
            check(!OverlayCapture.Visible().Any(o=>o.Handle==red),"Hidden overlays are not captured.");
            OverlayCapture.Show(red,true);Background();DrawCapturedOverlays(cleanTarget,projection,Vector3.Zero,OverlayCapture.Visible());
            var centre=Pixel(cleanTarget.Width/2,cleanTarget.Height/2);
            check(Math.Abs(centre[0]-128)<3&&centre[1]==0&&Math.Abs(centre[2]-127)<3,"Shared GPU overlay blends correct colors and transparency over the scene.");
            check(Pixel(0,0)[2]==255,"Overlay compositing preserves scene pixels outside its panel.");
            OverlayCapture.Create(green);OverlayCapture.Width(green,1);OverlayCapture.Order(green,10);OverlayCapture.Pose(green,Matrix4x4.CreateTranslation(0,0,-3),true);Upload(green,0,255,0,255);OverlayCapture.Show(green,true);
            Background();DrawCapturedOverlays(cleanTarget,projection,Vector3.Zero,OverlayCapture.Visible());centre=Pixel(cleanTarget.Width/2,cleanTarget.Height/2);
            check(centre[1]==255&&centre[0]==0&&centre[2]==0,"Higher-sort overlays draw above other panels even when farther away.");
            OverlayCapture.Show(green,false);Background();DrawCapturedOverlays(cleanTarget,projection,Vector3.Zero,OverlayCapture.Visible());
            check(Pixel(cleanTarget.Width/2,cleanTarget.Height/2)[0]>120,"Hiding an overlay immediately removes it from captured output.");
            Upload(red,255,255,0,255);for(int i=0;i<8;i++){pixels[i*4]=255;pixels[i*4+1]=0;}for(int i=8;i<16;i++){pixels[i*4]=0;pixels[i*4+1]=255;}
            source.Submit(red,pin.AddrOfPinnedObject(),4,4,4,(ulong h,ref Texture_t texture)=>EVROverlayError.None);OverlayCapture.Image(red,source.CurrentImage(red));
            Background();DrawCapturedOverlays(cleanTarget,projection,Vector3.Zero,OverlayCapture.Visible());
            var top=Pixel(cleanTarget.Width/2,cleanTarget.Height/2-20);var bottom=Pixel(cleanTarget.Width/2,cleanTarget.Height/2+20);
            check(top[0]>200&&top[1]<40&&bottom[1]>200&&bottom[0]<40,"Overlay text/image orientation is upright, not vertically flipped.");
            var moved=Matrix4x4.CreateRotationY(.1f)*Matrix4x4.CreateTranslation(.75f,0,-2);var native=EditorVrPanel.Native(moved);check(VrSession.Pose(native)==moved,"Overlay capture preserves the existing native VR transform.");
            OverlayCapture.Width(red,.3f);OverlayCapture.Pose(red,Matrix4x4.CreateTranslation(.75f,0,-2),true);Background();DrawCapturedOverlays(cleanTarget,projection,Vector3.Zero,OverlayCapture.Visible());
            check(Pixel(cleanTarget.Width/2,cleanTarget.Height/2)[2]==255,"Moving a panel does not leave an image at its previous position.");
            OverlayCapture.Pose(red,Matrix4x4.CreateTranslation(1.5f,0,-2),true);var head=Matrix4x4.CreateTranslation(1.5f,0,0);Matrix4x4.Invert(head,out var view);Background();DrawCapturedOverlays(cleanTarget,view*projection,head.Translation,OverlayCapture.Visible());
            check(Pixel(cleanTarget.Width/2,cleanTarget.Height/2)[2]==0,"Overlay follows tracking-space head motion independently of locomotion origin.");
            OverlayCapture.Pose(red,Matrix4x4.CreateRotationY(MathF.PI)*Matrix4x4.CreateTranslation(0,0,-2),true);Background();DrawCapturedOverlays(cleanTarget,projection,Vector3.Zero,OverlayCapture.Visible());
            check(Pixel(cleanTarget.Width/2,cleanTarget.Height/2)[2]==255,"Back-facing glove/editor panels are not shown through their backs.");
            OverlayCapture.Remove(red);OverlayCapture.Remove(green);check(!OverlayCapture.Visible().Any(o=>o.Handle==red||o.Handle==green),"Destroyed overlay textures leave the capture registry.");
            ConfigureOverlayCapture(false);check(!IncludeCleanOverlays&&capturedTextures.Count==0,"Scene-only mode releases capture resources without touching VR overlays.");
        }
        finally{OverlayCapture.Remove(red);OverlayCapture.Remove(green);pin.Free();ClearCapturedOverlays();}
    }
}
