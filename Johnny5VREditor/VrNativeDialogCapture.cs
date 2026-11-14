using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ServoAnimator;

// Limited to native dialog/menu HWNDs of the dedicated editor process.
// WPF windows and popups always use RenderTargetBitmap instead.
internal static class VrNativeDialogCapture
{
    [StructLayout(LayoutKind.Sequential)]private struct BitmapInfo
    {public uint Size;public int Width,Height;public ushort Planes,BitCount;public uint Compression,ImageSize;public int XPels,YPels;public uint Used,Important;}
    [DllImport("gdi32.dll")]private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")]private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")]private static extern IntPtr CreateDIBSection(IntPtr dc,ref BitmapInfo info,uint usage,out IntPtr bits,IntPtr section,uint offset);
    [DllImport("gdi32.dll")]private static extern IntPtr SelectObject(IntPtr dc,IntPtr value);
    [DllImport("gdi32.dll")]private static extern bool DeleteObject(IntPtr value);
    [DllImport("user32.dll")]private static extern bool PrintWindow(IntPtr window,IntPtr dc,uint flags);
    internal static byte[] Capture(IntPtr handle,int width,int height,int targetWidth,int targetHeight)
    {
        if(width<1||height<1||width>7680||height>4320)return null;
        IntPtr dc=CreateCompatibleDC(IntPtr.Zero);if(dc==IntPtr.Zero)return null;
        IntPtr bitmap=IntPtr.Zero,previous=IntPtr.Zero;
        try
        {
            var info=new BitmapInfo{Size=(uint)Marshal.SizeOf<BitmapInfo>(),Width=width,Height=-height,Planes=1,BitCount=32};
            bitmap=CreateDIBSection(dc,ref info,0,out var bits,IntPtr.Zero,0);
            if(bitmap==IntPtr.Zero||bits==IntPtr.Zero)return null;
            previous=SelectObject(dc,bitmap);
            if(!PrintWindow(handle,dc,2))return null;
            var pixels=new byte[checked(width*height*4)];Marshal.Copy(bits,pixels,0,pixels.Length);
            for(int i=3;i<pixels.Length;i+=4)pixels[i]=255;
            if(width==targetWidth&&height==targetHeight)return pixels;
            var source=BitmapSource.Create(width,height,96,96,PixelFormats.Pbgra32,null,pixels,width*4);
            var visual=new DrawingVisual();using(var draw=visual.RenderOpen())draw.DrawImage(source,new Rect(0,0,targetWidth,targetHeight));
            var resized=new RenderTargetBitmap(targetWidth,targetHeight,96,96,PixelFormats.Pbgra32);resized.Render(visual);
            var result=new byte[targetWidth*targetHeight*4];resized.CopyPixels(result,targetWidth*4,0);return result;
        }
        finally
        {
            if(previous!=IntPtr.Zero)SelectObject(dc,previous);
            if(bitmap!=IntPtr.Zero)DeleteObject(bitmap);DeleteDC(dc);
        }
    }
}
