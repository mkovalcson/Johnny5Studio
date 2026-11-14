using System.Runtime.InteropServices;
namespace Johnny5VR;
internal sealed record CleanOutputDisplay(string Id,string Device,string Name,Rectangle Bounds,int Refresh,bool Primary)
{
    public override string ToString()=>$"{Device.Replace(@"\\.\","")} · {Name} · {Bounds.Width} × {Bounds.Height} · {(Refresh>1?Refresh+" Hz":"refresh unknown")}{(Primary?" · Primary":"")}";
    internal static CleanOutputDisplay[] Enumerate()=>Screen.AllScreens.Select(screen=>
    {
        var adapter=DisplayDevice.New();var monitor=DisplayDevice.New();string name=screen.DeviceName,id=screen.DeviceName;
        if(EnumDisplayDevices(screen.DeviceName,0,ref monitor,1)){name=monitor.DeviceString;id=string.IsNullOrWhiteSpace(monitor.DeviceID)?id:monitor.DeviceID;}
        else for(uint i=0;EnumDisplayDevices(null,i,ref adapter,0);i++,adapter=DisplayDevice.New())if(adapter.DeviceName==screen.DeviceName){name=adapter.DeviceString;break;}
        var mode=new DevMode{dmSize=(ushort)Marshal.SizeOf<DevMode>()};int hz=EnumDisplaySettings(screen.DeviceName,-1,ref mode)?(int)mode.dmDisplayFrequency:0;
        return new CleanOutputDisplay(id,screen.DeviceName,name,screen.Bounds,hz,screen.Primary);
    }).OrderByDescending(d=>d.Primary).ThenBy(d=>d.Device).ToArray();
    internal static Rectangle Fit(int sourceWidth,int sourceHeight,int width,int height)
    {
        if(sourceWidth<=0||sourceHeight<=0||width<=0||height<=0)return Rectangle.Empty;
        double scale=Math.Min((double)width/sourceWidth,(double)height/sourceHeight);int w=Math.Max(1,(int)Math.Round(sourceWidth*scale)),h=Math.Max(1,(int)Math.Round(sourceHeight*scale));
        return new((width-w)/2,(height-h)/2,w,h);
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct DisplayDevice
    {
        public int cb;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string DeviceName;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string DeviceString;public uint StateFlags;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string DeviceID;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string DeviceKey;
        public static DisplayDevice New()=>new(){cb=Marshal.SizeOf<DisplayDevice>()};
    }
    // Display-mode union occupies 16 bytes between dmDriverExtra and dmColor.
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string dmDeviceName;
        public ushort dmSpecVersion,dmDriverVersion,dmSize,dmDriverExtra;public uint dmFields;
        public int dmPositionX,dmPositionY;public uint dmDisplayOrientation,dmDisplayFixedOutput;
        public short dmColor,dmDuplex,dmYResolution,dmTTOption,dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string dmFormName;
        public ushort dmLogPixels;public uint dmBitsPerPel,dmPelsWidth,dmPelsHeight,dmDisplayFlags,dmDisplayFrequency,dmICMMethod,dmICMIntent,dmMediaType,dmDitherType,dmReserved1,dmReserved2,dmPanningWidth,dmPanningHeight;
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern bool EnumDisplayDevices(string? device,uint index,ref DisplayDevice display,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern bool EnumDisplaySettings(string device,int mode,ref DevMode value);
}
internal sealed class CleanOutputWindow:Form
{
    protected override bool ShowWithoutActivation=>true;
    protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle|=0x08000000;return p;}}
    internal CleanOutputWindow(){Text="J5 Studio VR · Clean Output";BackColor=Color.Black;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;AutoScaleMode=AutoScaleMode.None;MinimizeBox=false;MaximizeBox=false;}
    internal void Place(CleanOutputDisplay display,bool fullscreen,int width,int height)
    {
        FormBorderStyle=fullscreen?FormBorderStyle.None:FormBorderStyle.Sizable;
        if(fullscreen)Bounds=display.Bounds;
        else{ClientSize=new(width,height);Location=new(display.Bounds.X+Math.Max(0,(display.Bounds.Width-Width)/2),display.Bounds.Y+Math.Max(0,(display.Bounds.Height-Height)/2));}
    }
}
