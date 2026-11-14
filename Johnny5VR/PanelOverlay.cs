using Valve.VR;

namespace Johnny5VR;

internal interface IPanelOverlay:IDisposable
{
    EVROverlayError CreateOverlay(string key,string name,ref ulong handle);
    EVROverlayError SetOverlayWidthInMeters(ulong handle,float width);
    EVROverlayError SetOverlayInputMethod(ulong handle,VROverlayInputMethod method);
    EVROverlayError SetOverlaySortOrder(ulong handle,uint order);
    EVROverlayError SetOverlayRaw(ulong handle,IntPtr data,uint width,uint height,uint depth);
    EVROverlayError SetOverlayTransformAbsolute(ulong handle,ETrackingUniverseOrigin origin,ref HmdMatrix34_t pose);
    EVROverlayError ShowOverlay(ulong handle);
    EVROverlayError HideOverlay(ulong handle);
    EVROverlayError DestroyOverlay(ulong handle);
}
internal sealed class SteamVrPanelOverlay:IPanelOverlay
{
    private readonly CVROverlay overlay=OpenVR.Overlay??throw new InvalidOperationException("SteamVR overlays are unavailable.");
    private readonly OverlayTextureStore textures;
    private readonly HashSet<ulong> handles=[];
    internal SteamVrPanelOverlay(int adapter=0)=>textures=new(adapter);
    public EVROverlayError CreateOverlay(string key,string name,ref ulong handle)
    {var error=overlay.CreateOverlay(key,name,ref handle);if(error==EVROverlayError.None){handles.Add(handle);OverlayCapture.Create(handle);}return error;}
    public EVROverlayError SetOverlayWidthInMeters(ulong handle,float width)
    {var error=overlay.SetOverlayWidthInMeters(handle,width);if(error==EVROverlayError.None)OverlayCapture.Width(handle,width);return error;}
    public EVROverlayError SetOverlayInputMethod(ulong handle,VROverlayInputMethod method)=>overlay.SetOverlayInputMethod(handle,method);
    public EVROverlayError SetOverlaySortOrder(ulong handle,uint order)
    {var error=overlay.SetOverlaySortOrder(handle,order);if(error==EVROverlayError.None)OverlayCapture.Order(handle,order);return error;}
    public EVROverlayError SetOverlayRaw(ulong handle,IntPtr data,uint width,uint height,uint depth)
    {
        var error=textures.Submit(handle,data,checked((int)width),checked((int)height),depth,(ulong h,ref Texture_t texture)=>overlay.SetOverlayTexture(h,ref texture));
        if(error==EVROverlayError.None)OverlayCapture.Image(handle,textures.CurrentImage(handle));return error;
    }
    public EVROverlayError SetOverlayTransformAbsolute(ulong handle,ETrackingUniverseOrigin origin,ref HmdMatrix34_t pose)
    {var error=overlay.SetOverlayTransformAbsolute(handle,origin,ref pose);if(error==EVROverlayError.None)OverlayCapture.Pose(handle,VrSession.Pose(pose),origin==ETrackingUniverseOrigin.TrackingUniverseStanding);return error;}
    public EVROverlayError ShowOverlay(ulong handle)
    {var error=overlay.ShowOverlay(handle);if(error==EVROverlayError.None)OverlayCapture.Show(handle,true);return error;}
    public EVROverlayError HideOverlay(ulong handle)
    {var error=overlay.HideOverlay(handle);if(error==EVROverlayError.None)OverlayCapture.Show(handle,false);return error;}
    public EVROverlayError DestroyOverlay(ulong handle){overlay.ClearOverlayTexture(handle);var error=overlay.DestroyOverlay(handle);textures.Remove(handle);OverlayCapture.Remove(handle);handles.Remove(handle);return error;}
    public void Dispose(){foreach(var h in handles)OverlayCapture.Remove(h);handles.Clear();textures.Dispose();}
}
