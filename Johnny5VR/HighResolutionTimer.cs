using System.Runtime.InteropServices;

namespace Johnny5VR;

// Windows' normal timer ticks every 15.6 ms, so Task.Delay(12) really waits about 25 ms
// and the bridge ran at 21 Hz instead of 30. A high-resolution waitable timer
// (Windows 10 1803 and later) wakes within about a millisecond. Falls back to Task.Delay.
internal sealed class HighResolutionTimer:IDisposable
{
    [DllImport("kernel32.dll",SetLastError=true,CharSet=CharSet.Unicode)]
    private static extern IntPtr CreateWaitableTimerExW(IntPtr attributes,string? name,uint flags,uint access);
    [DllImport("kernel32.dll",SetLastError=true)]
    private static extern bool SetWaitableTimer(IntPtr timer,ref long dueTime,int period,IntPtr completion,IntPtr argument,bool resume);
    [DllImport("kernel32.dll",SetLastError=true)]
    private static extern uint WaitForSingleObject(IntPtr handle,uint milliseconds);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
    private const uint HighResolution=0x2,AllAccess=0x1F0003;
    private IntPtr handle;
    internal HighResolutionTimer(){handle=CreateWaitableTimerExW(IntPtr.Zero,null,HighResolution,AllAccess);}
    internal bool HighResolutionAvailable=>handle!=IntPtr.Zero;
    internal async Task Wait(TimeSpan delay,CancellationToken token)
    {
        if(delay<=TimeSpan.Zero)return;
        long due=-delay.Ticks; // negative = relative, in 100 ns units (TimeSpan ticks)
        if(handle==IntPtr.Zero||!SetWaitableTimer(handle,ref due,0,IntPtr.Zero,IntPtr.Zero,false)){await Task.Delay(delay,token);return;}
        var timer=handle;uint limit=(uint)Math.Ceiling(delay.TotalMilliseconds)+50;
        await Task.Run(()=>WaitForSingleObject(timer,limit),token);
        token.ThrowIfCancellationRequested();
    }
    public void Dispose(){if(handle!=IntPtr.Zero){CloseHandle(handle);handle=IntPtr.Zero;}}
}
