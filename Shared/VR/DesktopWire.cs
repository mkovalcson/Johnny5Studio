using System.IO;
using System.IO.Compression;
using System.Text;

namespace Johnny5.VrBridge;

internal sealed record VrDesktopImage(long Handle,int X,int Y,int Width,int Height,int PixelWidth,int PixelHeight,byte[] Bgra);
internal sealed record VrDesktopFrame(VrDesktopImage[] Windows);
internal static class DesktopWire
{
    internal const int MaxPacket=24*1024*1024;
    internal static byte[] Encode(VrDesktopFrame frame)
    {
        using var stream=new MemoryStream();
        using(var zip=new GZipStream(stream,CompressionLevel.Fastest,true))using(var w=new BinaryWriter(zip,Encoding.UTF8))
        {
            w.Write(1);w.Write(frame.Windows.Length);
            foreach(var window in frame.Windows)
            {
                w.Write(window.Handle);w.Write(window.X);w.Write(window.Y);w.Write(window.Width);w.Write(window.Height);w.Write(window.PixelWidth);w.Write(window.PixelHeight);w.Write(window.Bgra.Length);w.Write(window.Bgra);
            }
        }
        return stream.ToArray();
    }
    internal static VrDesktopFrame Decode(byte[] data)
    {
        if(data.Length>MaxPacket)throw new InvalidDataException("Desktop packet is too large.");
        using var stream=new MemoryStream(data);using var zip=new GZipStream(stream,CompressionMode.Decompress);using var r=new BinaryReader(zip,Encoding.UTF8);
        if(r.ReadInt32()!=1)throw new InvalidDataException("Desktop protocol differs.");
        int count=SceneWire.Count(r,32),total=0;var windows=new VrDesktopImage[count];
        for(int i=0;i<count;i++)
        {
            long handle=r.ReadInt64();int x=r.ReadInt32(),y=r.ReadInt32(),width=r.ReadInt32(),height=r.ReadInt32(),pw=r.ReadInt32(),ph=r.ReadInt32();
            if(width<1||height<1||width>20000||height>12000||pw<1||ph<1||pw>1920||ph>1200)throw new InvalidDataException("Invalid desktop image bounds.");
            int length=SceneWire.Count(r,MaxPacket);total=checked(total+length);
            if(length!=checked(pw*ph*4)||total>MaxPacket)throw new InvalidDataException("Invalid desktop pixel count.");
            var pixels=r.ReadBytes(length);if(pixels.Length!=length)throw new EndOfStreamException();
            windows[i]=new(handle,x,y,width,height,pw,ph,pixels);
        }
        return new(windows);
    }
}
