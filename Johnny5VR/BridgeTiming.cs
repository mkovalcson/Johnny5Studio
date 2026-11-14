using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Johnny5VR;

// Once-a-second timing of the render loop and the editor bridge, written to
// Diagnostics/bridge-timing-<start>.csv. Compare standalone playback with VR
// editing to see which step makes frames late. Recording costs a few
// Stopwatch reads per frame; the file is written off the render thread.
internal static class BridgeTiming
{
    internal enum Section
    {
        Frame,          // whole Render() call, including the wait for SteamVR poses
        Work,           // Frame minus WaitPoses: time the render thread was busy
        Interval,       // time between frame starts
        Pump,           // PumpEditor (takes the newest editor scene and applies it)
        ApplyLive,      // Renderer.ApplyLive: rebuilds GPU data from an editor scene
        UpdatePose,     // VrSession.UpdatePose, including the wait and input/menu/panel steps
        WaitPoses,      // compositor.WaitGetPoses (idle time, not work)
        Menu,           // VR menu step and its image upload
        Panel,          // editor workspace panel step
        PanelUpload,    // editor workspace image upload to its overlay texture
        Draw,           // both eye renders and the flush
        Submit,         // handing both eye images to SteamVR
        Mirror,         // desktop window copy of the VR view, present and hand-off
        PanelChrome,    // background: drawing the panel frame around the editor image
        SceneRoundTrip, // background: scene request until the whole packet arrived (editor capture + encode + pipe)
        SceneDecode,    // background: decoding the scene packet and mesh bookkeeping
        DesktopRoundTrip,// background: editor window image request until it arrived (editor RenderTargetBitmap + pipe)
        DesktopCompose, // background: GDI+ composition of the editor window images
        EditorUiWait,EditorMotion,EditorCapture,EditorPose,EditorEncode,
        SceneCycle,     // background: start to start of scene requests (1000/SceneCycle = robot updates per second)
    }
    internal enum Value {SceneKB,DesktopKB,DynamicVertices,DynamicBufferAllocations,SceneChangedNodes,SceneChangedMaterials,SceneCachedBranches,SceneVisitedBranches,PoseMetadataBuilds,ContextFingerprintBuilds,SceneTargetCycleMs}
    private struct Stat {public int Count;public double Total,Max;}
    private static readonly object gate=new();
    private static readonly Stat[] sections=new Stat[Enum.GetValues<Section>().Length];
    private static readonly Stat[] values=new Stat[Enum.GetValues<Value>().Length];
    private static int late,vrFrames,vrReprojected,vrDropped,vrMisPresented,vrReasonCpu,vrReasonGpu,vrAsync,vrMotion;
    private static double vrGpuTotal,vrGpuMax;
    private static long frameStart,lastFrameStart,lastFlush=Stopwatch.GetTimestamp();
    private static double frameWait;
    private static int gc0,gc1,gc2;
    private static TimeSpan gcPause,playerCpu,editorCpu;
    private static long allocated;
    private static int editorPid;
    private static string? file;
    private static int rows;
    private static Task writes=Task.CompletedTask;
    internal static string Summary {get;private set;}="";
    internal static string? FilePath=>file;

    private static int hmdInvalid,leftInvalid,rightInvalid,focusLost,followTrackingHolds,followStops;
    private static string lastTrackingProblem="",lastFollowStop="";
    internal static void Tracking(bool head,bool left,bool right,bool focus,bool following,string detail)
    {
        lock(gate)
        {
            if(!head)hmdInvalid++;if(!left)leftInvalid++;if(!right)rightInvalid++;if(!focus)focusLost++;
            if(following&&(!head||!left||!right))followTrackingHolds++;
            if(detail.Length>0)lastTrackingProblem=DateTime.Now.ToString("HH:mm:ss.fff",CultureInfo.InvariantCulture)+" "+detail;
        }
        Transition(0,"Headset",head,detail,following);Transition(1,"Left controller",left,detail,following);
        Transition(2,"Right controller",right,detail,following);Transition(3,"Input focus (menu / dashboard / other app)",focus,"",following);
    }
    internal static void FollowStopped(string reason)
    {lock(gate){followStops++;lastFollowStop=DateTime.Now.ToString("HH:mm:ss.fff",CultureInfo.InvariantCulture)+" "+reason;}Event("Follow stopped: "+reason);}
    internal static void FollowStarted()=>Event("Follow started");

    // Problem log: Diagnostics/follow-problems-<start>.txt, one line per event with
    // milliseconds, so even dropouts too short to stop Follow are recorded with
    // their length. A device is logged only after it has been valid once, so the
    // headset warming up at launch is not reported.
    private static readonly bool[] everValid=new bool[4],down=new bool[4];
    private static readonly long[] downAt=new long[4];
    private static string? problemFile;
    private static Task problemWrites=Task.CompletedTask;
    internal static string? ProblemFilePath=>problemFile;
    internal static string LastProblem {get;private set;}="";
    private static void Transition(int device,string name,bool valid,string detail,bool following)
    {
        if(valid)
        {
            everValid[device]=true;
            if(down[device]){down[device]=false;Event($"{name} back after {Stopwatch.GetElapsedTime(downAt[device]).TotalMilliseconds:F0} ms"+(following?" (following)":""));}
            return;
        }
        if(!everValid[device]||down[device])return;
        down[device]=true;downAt[device]=Stopwatch.GetTimestamp();
        string part=detail.Split("; ").FirstOrDefault(p=>p.StartsWith(name.Split(' ')[0],StringComparison.Ordinal))??"";
        Event($"{name} lost"+(part.Length>0?" · "+part:"")+(following?" (following)":""));
    }
    internal static void Event(string text)
    {
        string line=DateTime.Now.ToString("HH:mm:ss.fff",CultureInfo.InvariantCulture)+"  "+text;
        LastProblem=line;
        try
        {
            if(problemFile==null){Directory.CreateDirectory(Diagnostics.Folder);problemFile=Path.Combine(Diagnostics.Folder,$"follow-problems-{DateTime.Now:yyyyMMdd-HHmmss}.txt");}
            string path=problemFile;
            problemWrites=problemWrites.ContinueWith(_=>{try{File.AppendAllText(path,line+Environment.NewLine);}catch{}},TaskScheduler.Default);
        }
        catch{}
    }
    private static string TrackingColumns()
    {
        lock(gate)
        {
            string Quote(string value)=>"\""+value.Replace("\"","\"\"")+"\"";
            string result=$",{hmdInvalid},{leftInvalid},{rightInvalid},{focusLost},{followTrackingHolds},{followStops},"+Quote(lastTrackingProblem)+","+Quote(lastFollowStop);
            hmdInvalid=leftInvalid=rightInvalid=focusLost=followTrackingHolds=followStops=0;return result;
        }
    }
    internal static long Start()=>Stopwatch.GetTimestamp();
    internal static void Stop(Section section,long start)
    {
        double ms=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        if(section==Section.WaitPoses)frameWait+=ms;
        Add(sections,(int)section,ms);
    }
    internal static void Measure(Section section,double milliseconds){if(double.IsFinite(milliseconds)&&milliseconds>=0)Add(sections,(int)section,milliseconds);}
    internal static void Record(Value value,double amount)=>Add(values,(int)value,amount);
    private static void Add(Stat[] stats,int index,double amount)
    {
        lock(gate){ref var s=ref stats[index];s.Count++;s.Total+=amount;if(amount>s.Max)s.Max=amount;}
    }

    internal static void BeginFrame(double budgetMs)
    {
        long now=Stopwatch.GetTimestamp();frameWait=0;
        if(lastFrameStart!=0)
        {
            double interval=(now-lastFrameStart)*1000.0/Stopwatch.Frequency;
            Add(sections,(int)Section.Interval,interval);
            if(interval>budgetMs*1.5)lock(gate)late++;
        }
        lastFrameStart=frameStart=now;
    }
    internal static void EndFrame()
    {
        double total=Stopwatch.GetElapsedTime(frameStart).TotalMilliseconds;
        Add(sections,(int)Section.Frame,total);Add(sections,(int)Section.Work,Math.Max(0,total-frameWait));
    }

    // SteamVR frame timing. Reason bits from openvr.h:
    // ReprojectionReason_Cpu 0x01, ReprojectionReason_Gpu 0x02, ReprojectionAsync 0x04, ReprojectionMotion 0x08.
    internal static void RecordCompositor(in Valve.VR.Compositor_FrameTiming t)
    {
        lock(gate)
        {
            vrFrames++;vrGpuTotal+=t.m_flTotalRenderGpuMs;vrGpuMax=Math.Max(vrGpuMax,t.m_flTotalRenderGpuMs);
            if(t.m_nNumFramePresents>1)vrReprojected++;
            vrDropped+=(int)t.m_nNumDroppedFrames;vrMisPresented+=(int)t.m_nNumMisPresented;
            if((t.m_nReprojectionFlags&0x01)!=0)vrReasonCpu++;
            if((t.m_nReprojectionFlags&0x02)!=0)vrReasonGpu++;
            if((t.m_nReprojectionFlags&0x04)!=0)vrAsync++;
            if((t.m_nReprojectionFlags&0x08)!=0)vrMotion++;
        }
    }

    internal static void EditorProcess(int pid)
    {
        if(pid==editorPid)return;
        editorPid=pid;editorCpu=Cpu(pid);
    }
    private static TimeSpan Cpu(int pid)
    {
        if(pid==0)return TimeSpan.Zero;
        try{using var p=Process.GetProcessById(pid);return p.HasExited?TimeSpan.Zero:p.TotalProcessorTime;}catch{return TimeSpan.Zero;}
    }

    // Call once per frame from the render thread; writes a row each second.
    internal static void FlushIfDue(string mode,string panel,double refreshHz)
    {
        long now=Stopwatch.GetTimestamp();double seconds=(now-lastFlush)/(double)Stopwatch.Frequency;
        if(seconds<1)return;
        lastFlush=now;
        Stat[] s,v;int lateFrames,frames,reprojected,dropped,mis,reasonCpu,reasonGpu,async,motion;double gpuAvg,gpuMax;
        lock(gate)
        {
            s=(Stat[])sections.Clone();v=(Stat[])values.Clone();Array.Clear(sections);Array.Clear(values);
            lateFrames=late;frames=vrFrames;reprojected=vrReprojected;dropped=vrDropped;mis=vrMisPresented;reasonCpu=vrReasonCpu;reasonGpu=vrReasonGpu;async=vrAsync;motion=vrMotion;
            gpuAvg=vrFrames>0?vrGpuTotal/vrFrames:0;gpuMax=vrGpuMax;
            late=vrFrames=vrReprojected=vrDropped=vrMisPresented=vrReasonCpu=vrReasonGpu=vrAsync=vrMotion=0;vrGpuTotal=vrGpuMax=0;
        }
        int g0=GC.CollectionCount(0),g1=GC.CollectionCount(1),g2=GC.CollectionCount(2);var pause=GC.GetTotalPauseDuration();long alloc=GC.GetTotalAllocatedBytes();
        var self=Process.GetCurrentProcess().TotalProcessorTime;var editor=Cpu(editorPid);
        bool first=file==null;
        int d0=first?0:g0-gc0,d1=first?0:g1-gc1,d2=first?0:g2-gc2;double pauseMs=first?0:(pause-gcPause).TotalMilliseconds;double allocMb=first?0:(alloc-allocated)/1048576.0;
        double selfCores=first?0:(self-playerCpu).TotalSeconds/seconds,editorCores=first||editorPid==0||editor==TimeSpan.Zero?0:Math.Max(0,(editor-editorCpu).TotalSeconds/seconds);
        gc0=g0;gc1=g1;gc2=g2;gcPause=pause;allocated=alloc;playerCpu=self;editorCpu=editor;
        if(first)
        {
            Directory.CreateDirectory(Diagnostics.Folder);
            file=Path.Combine(Diagnostics.Folder,$"bridge-timing-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
            var header=new StringBuilder("time,mode,panel,refreshHz,frames,fps,lateFrames");
            foreach(var name in Enum.GetNames<Section>())header.Append($",{name}N,{name}AvgMs,{name}MaxMs");
            foreach(var name in Enum.GetNames<Value>())header.Append($",{name}Avg,{name}Max");
            header.Append(",gc0,gc1,gc2,gcPauseMs,allocMB,playerCores,editorCores,vrFrames,vrGpuAvgMs,vrGpuMaxMs,vrReprojected,vrDropped,vrMisPresented,vrReasonCpu,vrReasonGpu,vrAsync,vrMotionSmoothing");
            header.Append(",hmdInvalidFrames,leftInvalidFrames,rightInvalidFrames,inputFocusLostFrames,followTrackingHoldFrames,followStops,lastTrackingProblem,lastFollowStop");
            Append(header.ToString());
        }
        var c=CultureInfo.InvariantCulture;int frameCount=s[(int)Section.Frame].Count;
        var row=new StringBuilder();
        row.Append(string.Format(c,"{0:HH:mm:ss},{1},{2},{3:F0},{4},{5:F1},{6}",DateTime.Now,mode,panel,refreshHz,frameCount,frameCount/seconds,lateFrames));
        foreach(var x in s)row.Append(string.Format(c,",{0},{1:F2},{2:F2}",x.Count,x.Count>0?x.Total/x.Count:0,x.Max));
        foreach(var x in v)row.Append(string.Format(c,",{0:F0},{1:F0}",x.Count>0?x.Total/x.Count:0,x.Max));
        row.Append(string.Format(c,",{0},{1},{2},{3:F1},{4:F1},{5:F2},{6:F2},{7},{8:F2},{9:F2},{10},{11},{12},{13},{14},{15},{16}",d0,d1,d2,pauseMs,allocMb,selfCores,editorCores,frames,gpuAvg,gpuMax,reprojected,dropped,mis,reasonCpu,reasonGpu,async,motion));
        row.Append(TrackingColumns());
        if(rows<14400){rows++;Append(row.ToString());}

        string Ms(Section section)=>s[(int)section].Count>0?$"{s[(int)section].Total/s[(int)section].Count:F1}/{s[(int)section].Max:F1}":"–";
        Summary=$"Timing avg/max ms · work {Ms(Section.Work)} · apply {Ms(Section.ApplyLive)} · panel upload {Ms(Section.PanelUpload)} · draw {Ms(Section.Draw)}\n"+
            $"Late frames {lateFrames} · GC gen2 {d2}, pause {pauseMs:F0} ms, {allocMb:F0} MB/s · CPU cores: player {selfCores:F2}, editor {editorCores:F2}"+
            (frames>0?$"\nSteamVR: GPU {gpuAvg:F1}/{gpuMax:F1} ms · reprojected {reprojected}/{frames} (CPU {reasonCpu}, GPU {reasonGpu}) · dropped {dropped}":"")+
            (LastProblem.Length>0?"\nLast event: "+LastProblem:"");
    }
    private static void Append(string line)
    {
        string path=file!;
        writes=writes.ContinueWith(_=>{try{File.AppendAllText(path,line+Environment.NewLine);}catch{}},TaskScheduler.Default);
    }
}
