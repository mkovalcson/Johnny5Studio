using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Johnny5.VrBridge;

namespace ServoAnimator;

internal static class VrDesktopCapture
{
    private delegate bool VrWindowEnum(IntPtr handle,IntPtr state);
    [DllImport("user32.dll",EntryPoint="EnumWindows")]private static extern bool EnumerateVrWindows(VrWindowEnum callback,IntPtr state);
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(IntPtr handle,out uint process);
    [DllImport("user32.dll")]private static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll")]private static extern bool IsIconic(IntPtr handle);
    [DllImport("user32.dll")]private static extern bool GetWindowRect(IntPtr handle,out NativeRect rect);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern int GetClassName(IntPtr handle,System.Text.StringBuilder name,int size);
    [StructLayout(LayoutKind.Sequential)]private struct NativeRect {public int Left,Top,Right,Bottom;}
    private static bool ContainsVrTooltip(DependencyObject root)
    {
        if(root is ToolTip)return true;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)if(ContainsVrTooltip(VisualTreeHelper.GetChild(root,i)))return true;
        return false;
    }
    internal static VrDesktopFrame Capture()
    {
        // Render the WPF visual tree itself: PrintWindow can interrupt/blank a
        // hardware-composited WPF window. Nothing is sent to its paint handler.
        var sources=PresentationSource.CurrentSources.OfType<HwndSource>().Where(s=>s.RootVisual is FrameworkElement).ToDictionary(s=>s.Handle);
        // Common file pickers, message boxes and native menus have no WPF
        // PresentationSource. Include only this editor process's visible dialogs.
        var ordered=new List<IntPtr>();EnumerateVrWindows((handle,_)=>
        {
            GetWindowThreadProcessId(handle,out uint pid);
            if(pid==(uint)Environment.ProcessId&&IsWindowVisible(handle)&&!IsIconic(handle))ordered.Add(handle);
            return true;
        },IntPtr.Zero);
        var views=new List<(IntPtr Handle,FrameworkElement View,Rect Bounds)>();
        foreach(var handle in ordered.Take(32))
        {
            if(!sources.TryGetValue(handle,out var source))
            {
                var name=new System.Text.StringBuilder(128);GetClassName(handle,name,name.Capacity);
                if(name.ToString() is "#32770" or "#32768"&&GetWindowRect(handle,out var native)&&native.Right>native.Left&&native.Bottom>native.Top)
                    views.Add((handle,null,new Rect(native.Left,native.Top,native.Right-native.Left,native.Bottom-native.Top)));
                continue;
            }
            var root=(FrameworkElement)source.RootVisual;
            if(root is Window window&&window.WindowState==WindowState.Minimized)continue;
            var view=root is Window w?w.Content as FrameworkElement:root;
            if(view==null||!view.IsVisible||!view.IsLoaded||view.ActualWidth<2||view.ActualHeight<2||ContainsVrTooltip(view))continue;
            var p=view.PointToScreen(new Point());var dpi=VisualTreeHelper.GetDpi(view);
            views.Add((handle,view,new Rect(p.X,p.Y,view.ActualWidth*dpi.DpiScaleX,view.ActualHeight*dpi.DpiScaleY)));
        }
        if(views.Count==0)return new([]);
        var bounds=views.Select(v=>v.Bounds).Aggregate(Rect.Union);
        if(bounds.Width>20000||bounds.Height>12000)return new([]);
        double scale=Math.Min(1,Math.Min(1920/bounds.Width,1200/bounds.Height));var images=new List<VrDesktopImage>();
        foreach(var item in views)
        {
            int width=Math.Clamp((int)Math.Ceiling(item.Bounds.Width*scale),1,1920),height=Math.Clamp((int)Math.Ceiling(item.Bounds.Height*scale),1,1200);
            if(item.View==null)
            {
                // Never send PrintWindow to a WPF HWND: that was the source of
                // editor repaint flicker. GDI dialogs use their native paint path.
                if(VrNativeDialogCapture.Capture(item.Handle,(int)item.Bounds.Width,(int)item.Bounds.Height,width,height) is {} native)
                    images.Add(new(item.Handle.ToInt64(),(int)item.Bounds.X,(int)item.Bounds.Y,(int)item.Bounds.Width,(int)item.Bounds.Height,width,height,native));
                continue;
            }
            var bitmap=new RenderTargetBitmap(width,height,width/item.View.ActualWidth*96,height/item.View.ActualHeight*96,PixelFormats.Pbgra32);
            bitmap.Render(item.View);var pixels=new byte[width*height*4];bitmap.CopyPixels(pixels,width*4,0);
            images.Add(new(item.Handle.ToInt64(),(int)Math.Round(item.Bounds.X),(int)Math.Round(item.Bounds.Y),Math.Max(1,(int)Math.Round(item.Bounds.Width)),Math.Max(1,(int)Math.Round(item.Bounds.Height)),width,height,pixels));
        }
        return new(images.ToArray());
    }
}
