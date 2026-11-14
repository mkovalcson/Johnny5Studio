using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Valve.VR;
using Device=SharpDX.Direct3D11.Device;

namespace Johnny5VR;

// Persistent, double-buffered D3D textures avoid SetOverlayRaw's asynchronous
// image loader and its raw-image size limits. Keep the last image on failure.
internal sealed class OverlayTextureStore:IDisposable
{
    internal delegate EVROverlayError SubmitTexture(ulong handle,ref Texture_t texture);
    private static long imageId;
    private sealed class Surface:IDisposable
    {
        internal readonly int Width,Height;
        internal readonly Texture2D[] Buffers;
        internal int Next;
        internal readonly OverlayImage[] Shared;
        internal Surface(Device device,int width,int height)
        {
            Width=width;Height=height;
            var desc=new Texture2DDescription{Width=width,Height=height,ArraySize=1,MipLevels=1,Format=Format.R8G8B8A8_UNorm,
                SampleDescription=new(1,0),Usage=ResourceUsage.Default,BindFlags=BindFlags.ShaderResource|BindFlags.RenderTarget,OptionFlags=ResourceOptionFlags.Shared};
            Buffers=[new(device,desc),new(device,desc)];
            Shared=Buffers.Select(buffer=>{using var resource=buffer.QueryInterface<SharpDX.DXGI.Resource>();return new OverlayImage(Interlocked.Increment(ref imageId),resource.SharedHandle,width,height);}).ToArray();
        }
        public void Dispose(){foreach(var buffer in Buffers)buffer.Dispose();}
    }
    private readonly Device device;
    private readonly DeviceContext context;
    private readonly Dictionary<ulong,Surface> images=[];
    private readonly Queue<Surface> retired=[];
    private bool disposed;
    internal int Count=>images.Count;
    internal OverlayTextureStore(int adapter)
    {
        using var factory=new Factory1();using var gpu=factory.GetAdapter1(adapter);
        device=new(gpu,DeviceCreationFlags.BgraSupport,FeatureLevel.Level_11_0);context=device.ImmediateContext;
    }
    internal EVROverlayError Submit(ulong handle,IntPtr pixels,int width,int height,uint bytesPerPixel,SubmitTexture submit)
    {
        if(width<1||height<1||width>4096||height>4096||bytesPerPixel!=4||pixels==IntPtr.Zero)throw new ArgumentException("Invalid overlay image dimensions or format.");
        images.TryGetValue(handle,out var previous);bool replace=previous==null||previous.Width!=width||previous.Height!=height;
        var surface=replace?new Surface(device,width,height):previous!;
        try
        {
            var buffer=surface.Buffers[surface.Next];
            context.UpdateSubresource(new SharpDX.DataBox(pixels,width*4,0),buffer);context.Flush();
            var texture=new Texture_t{handle=buffer.NativePointer,eType=ETextureType.DirectX,eColorSpace=EColorSpace.Gamma};
            var error=submit(handle,ref texture);
            if(error!=EVROverlayError.None){if(replace)surface.Dispose();return error;}
            surface.Next=1-surface.Next;
            if(replace){images[handle]=surface;if(previous!=null)retired.Enqueue(previous);}
            while(retired.Count>4)retired.Dequeue().Dispose();
            return error;
        }
        catch{if(replace)surface.Dispose();throw;}
    }
    internal OverlayImage CurrentImage(ulong handle){var surface=images[handle];return surface.Shared[1-surface.Next];}
    internal void Remove(ulong handle){if(images.Remove(handle,out var surface))surface.Dispose();}
    internal byte[] ReadCurrentPixel(ulong handle)
    {
        var surface=images[handle];var desc=surface.Buffers[0].Description;
        desc.Usage=ResourceUsage.Staging;desc.BindFlags=BindFlags.None;desc.OptionFlags=ResourceOptionFlags.None;desc.CpuAccessFlags=CpuAccessFlags.Read;
        using var staging=new Texture2D(device,desc);context.CopyResource(surface.Buffers[1-surface.Next],staging);
        var data=context.MapSubresource(staging,0,MapMode.Read,SharpDX.Direct3D11.MapFlags.None);
        try{var pixel=new byte[4];System.Runtime.InteropServices.Marshal.Copy(data.DataPointer,pixel,0,4);return pixel;}finally{context.UnmapSubresource(staging,0);}
    }
    public void Dispose(){if(disposed)return;disposed=true;context.ClearState();context.Flush();foreach(var image in images.Values)image.Dispose();images.Clear();while(retired.TryDequeue(out var image))image.Dispose();context.Dispose();device.Dispose();}
}
