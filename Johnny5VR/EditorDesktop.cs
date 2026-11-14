using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Johnny5.VrBridge;

namespace Johnny5VR;

internal sealed record EditorWindowRect(IntPtr Handle,Rectangle Bounds);
internal sealed record EditorDesktopFrame(byte[] Rgba,int Width,int Height,Rectangle DesktopBounds,EditorWindowRect[] Windows);

internal static class EditorDesktop
{
    internal static bool HasRenderedContent(EditorDesktopFrame frame)
    {
        var colors=new HashSet<int>();
        for(int y=0;y<frame.Height;y+=8)for(int x=0;x<frame.Width;x+=8)
        {
            int i=(y*frame.Width+x)*4;colors.Add(frame.Rgba[i]|frame.Rgba[i+1]<<8|frame.Rgba[i+2]<<16);
            if(colors.Count>8)return true;
        }
        return false;
    }
    internal static EditorDesktopFrame? FromRendered(VrDesktopFrame frame)
    {
        if(frame.Windows.Length==0)return null;
        var windows=frame.Windows.Select(w=>new EditorWindowRect(new IntPtr(w.Handle),new(w.X,w.Y,w.Width,w.Height))).ToArray();
        var bounds=windows.Select(w=>w.Bounds).Aggregate(Rectangle.Union);
        float scale=Math.Min(1,Math.Min(1920f/bounds.Width,1200f/bounds.Height));int width=Math.Max(1,(int)(bounds.Width*scale)),height=Math.Max(1,(int)(bounds.Height*scale));
        using var bitmap=new Bitmap(width,height,PixelFormat.Format32bppArgb);
        using(var g=Graphics.FromImage(bitmap))
        {
            g.Clear(Color.FromArgb(22,29,40));
            foreach(var image in frame.Windows.Reverse())
            {
                using var window=new Bitmap(image.PixelWidth,image.PixelHeight,PixelFormat.Format32bppPArgb);
                var data=window.LockBits(new(0,0,image.PixelWidth,image.PixelHeight),ImageLockMode.WriteOnly,PixelFormat.Format32bppPArgb);
                try{for(int y=0;y<image.PixelHeight;y++)Marshal.Copy(image.Bgra,y*image.PixelWidth*4,data.Scan0+y*data.Stride,image.PixelWidth*4);}finally{window.UnlockBits(data);}
                g.DrawImage(window,new RectangleF((image.X-bounds.X)*scale,(image.Y-bounds.Y)*scale,image.Width*scale,image.Height*scale));
            }
        }
        var rgba=new byte[width*height*4];var bits=bitmap.LockBits(new(0,0,width,height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
        try{for(int y=0;y<height;y++)Marshal.Copy(bits.Scan0+y*bits.Stride,rgba,y*width*4,width*4);}finally{bitmap.UnlockBits(bits);}
        for(int i=0;i<rgba.Length;i+=4){(rgba[i],rgba[i+2])=(rgba[i+2],rgba[i]);rgba[i+3]=255;}
        return new(rgba,width,height,bounds,windows);
    }
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
    [DllImport("user32.dll")]private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")]private static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")]private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")]private static extern uint SendInput(uint count,Input[] inputs,int size);
    [StructLayout(LayoutKind.Sequential)]private struct MouseInput{public int X,Y;public uint Data,Flags,Time;public UIntPtr Extra;}
    [StructLayout(LayoutKind.Explicit,Size=40)]private struct Input{[FieldOffset(0)]public uint Type;[FieldOffset(8)]public MouseInput Mouse;}
    internal static Point Map(EditorDesktopFrame frame,float u,float v)=>new(frame.DesktopBounds.X+(int)(Math.Clamp(u,0,1)*Math.Max(0,frame.DesktopBounds.Width-1)),frame.DesktopBounds.Y+(int)(Math.Clamp(v,0,1)*Math.Max(0,frame.DesktopBounds.Height-1)));
    internal static bool Move(EditorDesktopFrame frame,int process,float u,float v,bool focus)
    {
        Point p=Map(frame,u,v);var target=frame.Windows.FirstOrDefault(w=>w.Bounds.Contains(p));if(target==null)return false;
        GetWindowThreadProcessId(target.Handle,out var pid);if(pid!=(uint)process)return false;
        if(focus)SetForegroundWindow(target.Handle);
        GetWindowThreadProcessId(WindowFromPoint(p),out var under);
        if(under!=(uint)process)return false;
        return SetCursorPos(p.X,p.Y);
    }
    internal static void Button(bool down,bool right=false)=>Mouse(right?(down?8u:16u):(down?2u:4u));
    internal static void Wheel(int direction)=>Mouse(0x800,unchecked((uint)(direction*120)));
    private static void Mouse(uint flags,uint data=0){var input=new Input{Mouse=new(){Flags=flags,Data=data}};if(SendInput(1,[input],Marshal.SizeOf<Input>())!=1)throw new InvalidOperationException("Windows could not deliver editor input. Run both applications at the same privilege level.");}
}
