using System.Numerics;
namespace Johnny5VR;
internal readonly record struct OverlayImage(long Id,IntPtr SharedHandle,int Width,int Height);
internal readonly record struct CapturedOverlay(ulong Handle,uint Order,float Width,Matrix4x4 World,OverlayImage Image);
// Metadata only: overlay pixels stay in the existing shared GPU textures.
internal static class OverlayCapture
{
    private sealed class Entry{internal uint Order;internal float Width=1;internal Matrix4x4 World=Matrix4x4.Identity;internal bool Positioned,Visible;internal OverlayImage Image;}
    private static readonly object gate=new();
    private static readonly Dictionary<ulong,Entry> entries=[];
    internal static void Create(ulong handle){lock(gate)entries[handle]=new();}
    internal static void Width(ulong handle,float value){lock(gate)if(entries.TryGetValue(handle,out var e))e.Width=value;}
    internal static void Order(ulong handle,uint value){lock(gate)if(entries.TryGetValue(handle,out var e))e.Order=value;}
    internal static void Pose(ulong handle,Matrix4x4 world,bool standing){lock(gate)if(entries.TryGetValue(handle,out var e)){e.World=world;e.Positioned=standing;}}
    internal static void Image(ulong handle,OverlayImage image){lock(gate)if(entries.TryGetValue(handle,out var e))e.Image=image;}
    internal static void Show(ulong handle,bool show){lock(gate)if(entries.TryGetValue(handle,out var e))e.Visible=show;}
    internal static void Remove(ulong handle){lock(gate)entries.Remove(handle);}
    internal static CapturedOverlay[] Visible()
    {
        lock(gate)return entries.Where(p=>p.Value.Visible&&p.Value.Positioned&&p.Value.Width>0&&p.Value.Image.SharedHandle!=IntPtr.Zero)
            .Select(p=>new CapturedOverlay(p.Key,p.Value.Order,p.Value.Width,p.Value.World,p.Value.Image)).ToArray();
    }
}
