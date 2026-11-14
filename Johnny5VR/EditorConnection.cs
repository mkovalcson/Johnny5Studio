using System.Diagnostics;
using System.IO.Pipes;
using Johnny5.VrBridge;

namespace Johnny5VR;

internal sealed class EditorConnection:IDisposable
{
    internal Process Process {get;}
    internal string Config {get;}
    private readonly string session=Guid.NewGuid().ToString();
    private readonly CancellationTokenSource stop=new();
    // One scene update per 33 ms (30 Hz), measured start to start. The editor
    // builds each scene on its UI thread (about 15 ms), so faster would starve it.
    internal const double SceneCycleMs=1000.0/30;
    // While following, scenes come faster when the editor has time to spare: the cycle
    // is long enough that scene work (UI wait, motion, capture, pose) uses at most 40% of
    // the editor's UI thread, and the player's ApplyLive at most 25% of its render thread.
    // Never faster than 75 Hz, never slower than the normal 30 Hz.
    internal const double FastestFollowCycleMs=1000.0/75,EditorUiShare=.4,PlayerApplyShare=.25;
    private double editorSceneMs=SceneCycleMs*EditorUiShare;
    private double playerApplyMs;
    internal void ReportApply(double ms){if(double.IsFinite(ms)&&ms>=0)Volatile.Write(ref playerApplyMs,Volatile.Read(ref playerApplyMs)*.8+ms*.2);}
    internal static double TargetCycle(bool following,double editorSceneMs,double playerApplyMs)=>
        following?Math.Clamp(Math.Max(editorSceneMs/EditorUiShare,playerApplyMs/PlayerApplyShare),FastestFollowCycleMs,SceneCycleMs):SceneCycleMs;
    internal double CycleMs=>TargetCycle(FollowActive,editorSceneMs,Volatile.Read(ref playerApplyMs));
    private readonly Dictionary<long,VrMesh> meshes=[];
    private readonly Dictionary<long,VrTexture> textures=[];
    private VrFrame? pending;
    private EditorDesktopFrame? desktop;
    internal long DesktopFrames;
    internal EditorDesktopFrame? Desktop=>Volatile.Read(ref desktop);
    private int pauseRequested;
    private readonly System.Collections.Concurrent.ConcurrentQueue<VrEditorCommand> commands=new();
    private VrEditorNavigation? navigation;
    private VrPoseRequest? posePreview;
    private VrMotionBatch? motionPreview;
    private readonly System.Collections.Concurrent.ConcurrentQueue<VrRecordingRequest> recordingActions=new();
    internal void Recording(VrRecordingRequest request){if(recordingActions.Count<32)recordingActions.Enqueue(request);}
    internal void Motion(VrMotionBatch batch)=>Interlocked.Exchange(ref motionPreview,batch);
    private readonly System.Collections.Concurrent.ConcurrentQueue<VrPoseRequest> poseActions=new();
    internal volatile bool FollowActive;
    internal volatile bool UseFollowBackdrop=true;
    internal volatile bool Active=true;
    // Each editor window image costs the editor's UI thread 100–300 ms, which
    // delays its robot scene updates. Ask for images only while someone sees them.
    internal volatile bool DesktopWanted=true;
    internal string Status {get;private set;}="Starting editor…";
    internal long FramesReceived,TransferredMeshes,TransferredTextures;
    internal EditorConnection(string executable,string config)
    {
        if(!File.Exists(executable))throw new FileNotFoundException("The VR editor build is missing. Use the complete portable release.",executable);
        Config=Path.GetFullPath(config);
        var info=new ProcessStartInfo(executable){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(executable)!};
        foreach(string arg in new[]{"--vr-editor","--vr-pipe",session,"--vr-config",Path.GetFullPath(config)})info.ArgumentList.Add(arg);
        Process=Process.Start(info)??throw new InvalidOperationException("The Animation Editor could not start.");
        _=Task.Run(Receive);
        _=Task.Run(ReceiveDesktop);
    }
    internal VrFrame? Take()
    {
        // Latest wins: the bridge never waits for the render thread to take a scene.
        return Interlocked.Exchange(ref pending,null);
    }
    internal void Pause()=>Interlocked.Exchange(ref pauseRequested,1);
    internal void Command(VrEditorCommand command){if(commands.Count<32)commands.Enqueue(command);}
    internal void Navigate(VrEditorNavigation value)=>Interlocked.Exchange(ref navigation,value);
    internal void Pose(VrPoseRequest value)
    {
        if(value.Action==VrPoseAction.Set)Interlocked.Exchange(ref posePreview,value);
        else if(poseActions.Count<32)
        {
            if(Interlocked.Exchange(ref posePreview,null) is {} last)poseActions.Enqueue(last);
            poseActions.Enqueue(value);
        }
    }
    private async Task ReceiveDesktop()
    {
        while(!stop.IsCancellationRequested&&!Process.HasExited)
        {
            try
            {
                using var pipe=new NamedPipeClientStream(".","Johnny5VR.Desktop."+session,PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);timeout.CancelAfter(TimeSpan.FromMinutes(3));await pipe.ConnectAsync(timeout.Token);
                using var cancel=stop.Token.Register(()=>pipe.Dispose());using var reader=new BinaryReader(pipe,System.Text.Encoding.UTF8,true);
                while(!stop.IsCancellationRequested&&!Process.HasExited)
                {
                    if(!Active||!DesktopWanted){await Task.Delay(50,stop.Token);continue;}
                    var requestStart=BridgeTiming.Start();
                    await pipe.WriteAsync(new byte[]{1},stop.Token);await pipe.FlushAsync(stop.Token);
                    int count=await Task.Run(()=>SceneWire.Count(reader,DesktopWire.MaxPacket),stop.Token);var bytes=new byte[count];await pipe.ReadExactlyAsync(bytes,stop.Token);
                    BridgeTiming.Stop(BridgeTiming.Section.DesktopRoundTrip,requestStart);BridgeTiming.Record(BridgeTiming.Value.DesktopKB,count/1024.0);
                    var composeStart=BridgeTiming.Start();
                    var composed=EditorDesktop.FromRendered(DesktopWire.Decode(bytes));bool content=composed!=null&&EditorDesktop.HasRenderedContent(composed);
                    BridgeTiming.Stop(BridgeTiming.Section.DesktopCompose,composeStart);
                    if(content){Volatile.Write(ref desktop,composed);Interlocked.Increment(ref DesktopFrames);}
                    await Task.Delay(67,stop.Token);
                }
            }
            catch(Exception ex) when(ex is IOException or OperationCanceledException or ObjectDisposedException)
            {if(stop.IsCancellationRequested||Process.HasExited)return;}
            catch(Exception ex){Diagnostics.Log(ex);}
            try{await Task.Delay(1000,stop.Token);}catch(OperationCanceledException){return;}
        }
    }
    private async Task Receive()
    {
        try
        {
            using var pipe=new NamedPipeClientStream(".","Johnny5VR.Editor."+session,PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
            using var connectionTimeout=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);connectionTimeout.CancelAfter(TimeSpan.FromMinutes(3));
            await pipe.ConnectAsync(connectionTimeout.Token);
            using var cancel=stop.Token.Register(()=>pipe.Dispose());
            using var writer=new BinaryWriter(pipe,System.Text.Encoding.UTF8,true);using var reader=new BinaryReader(pipe,System.Text.Encoding.UTF8,true);
            using var pace=new HighResolutionTimer();
            var sceneState=new VrSceneAccumulator();
            // Motion goes only with a scene request (motion:true), so each scene reflects
            // exactly one motion step and the robot moves evenly between scenes.
            void SendPause(bool motion=false)
            {
                if(Interlocked.Exchange(ref pauseRequested,0)!=0)writer.Write((byte)2);
                while(commands.TryDequeue(out var command)){writer.Write((byte)3);writer.Write((byte)command);}
                if(Interlocked.Exchange(ref navigation,null) is {} nav){writer.Write((byte)4);writer.Write(nav.X);writer.Write(nav.Y);writer.Write(nav.Pan);writer.Write(nav.Zoom);}
                void SendPose(VrPoseRequest request){writer.Write((byte)5);writer.Write(request.Id);writer.Write(request.Token);writer.Write((byte)request.Action);writer.Write(request.Key);writer.Write(request.Value);}
                while(poseActions.TryDequeue(out var pose))SendPose(pose);
                if(Interlocked.Exchange(ref posePreview,null) is {} preview)SendPose(preview);
                while(recordingActions.TryDequeue(out var recording)){writer.Write((byte)7);writer.Write(recording.Id);writer.Write(recording.Token);writer.Write((byte)recording.Action);writer.Write((int)recording.Tracks);}
                if(motion&&Interlocked.Exchange(ref motionPreview,null) is {} batch)
                {
                    writer.Write((byte)6);VrMotionWire.Write(writer,batch);
                }
                writer.Flush();
            }
            bool? sentFollow=null,sentBackdrop=null;long cycleStart=0;
            while(!stop.IsCancellationRequested&&!Process.HasExited)
            {
                if(sentFollow!=FollowActive||sentBackdrop!=UseFollowBackdrop)
                {sentFollow=FollowActive;sentBackdrop=UseFollowBackdrop;writer.Write((byte)8);writer.Write(sentFollow.Value);writer.Write(sentBackdrop.Value);writer.Flush();}
                SendPause(motion:true);
                if(!Active){await Task.Delay(100,stop.Token);continue;}
                var requestStart=BridgeTiming.Start();
                if(cycleStart!=0)BridgeTiming.Stop(BridgeTiming.Section.SceneCycle,cycleStart);
                cycleStart=requestStart;
                writer.Write((byte)1);writer.Write(meshes.Count);foreach(long id in meshes.Keys)writer.Write(id);writer.Write(textures.Count);foreach(long id in textures.Keys)writer.Write(id);writer.Write(sceneState.Sequence);writer.Flush();
                int bytes=await Task.Run(()=>SceneWire.Count(reader,SceneWire.MaxPacket),stop.Token);double encodeMs=reader.ReadDouble();BridgeTiming.Measure(BridgeTiming.Section.EditorEncode,encodeMs);var data=new byte[bytes];await pipe.ReadExactlyAsync(data,stop.Token);
                BridgeTiming.Stop(BridgeTiming.Section.SceneRoundTrip,requestStart);BridgeTiming.Record(BridgeTiming.Value.SceneKB,bytes/1024.0);
                var decodeStart=BridgeTiming.Start();
                var packetFrame=SceneWire.Decode(data);
                var metrics=packetFrame.Metrics;
                BridgeTiming.Measure(BridgeTiming.Section.EditorUiWait,metrics.UiWaitMs);
                if(metrics.MotionMs>0)BridgeTiming.Measure(BridgeTiming.Section.EditorMotion,metrics.MotionMs);
                BridgeTiming.Measure(BridgeTiming.Section.EditorCapture,metrics.CaptureMs);BridgeTiming.Measure(BridgeTiming.Section.EditorPose,metrics.PoseMs);
                BridgeTiming.Record(BridgeTiming.Value.SceneChangedNodes,packetFrame.Nodes.Length);BridgeTiming.Record(BridgeTiming.Value.SceneChangedMaterials,packetFrame.Materials.Length);
                BridgeTiming.Record(BridgeTiming.Value.SceneCachedBranches,metrics.CachedBranches);BridgeTiming.Record(BridgeTiming.Value.SceneVisitedBranches,metrics.VisitedBranches);
                BridgeTiming.Record(BridgeTiming.Value.PoseMetadataBuilds,metrics.MetadataBuilds);BridgeTiming.Record(BridgeTiming.Value.ContextFingerprintBuilds,metrics.FingerprintBuilds);
                editorSceneMs=editorSceneMs*.8+(metrics.UiWaitMs+metrics.MotionMs+metrics.CaptureMs+metrics.PoseMs)*.2;
                BridgeTiming.Record(BridgeTiming.Value.SceneTargetCycleMs,CycleMs);
                var frame=sceneState.Apply(packetFrame);
                Interlocked.Increment(ref FramesReceived);Interlocked.Add(ref TransferredMeshes,frame.Meshes.Length);Interlocked.Add(ref TransferredTextures,frame.Textures.Length);
                foreach(var m in frame.Meshes)meshes[m.Id]=m;foreach(var t in frame.Textures)textures[t.Id]=t;
                var usedMeshes=frame.Nodes.Select(n=>n.Mesh).ToHashSet();
                if(frame.Treads is {} treads)foreach(var layer in treads.Layers)usedMeshes.UnionWith(layer.Frames);foreach(long id in meshes.Keys.Where(k=>!usedMeshes.Contains(k)&&meshes[k].Dynamic).ToArray())meshes.Remove(id);
                var usedTextures=frame.Materials.Select(m=>m.Texture).Where(t=>t!=0).ToHashSet();foreach(long id in textures.Keys.Where(k=>!usedTextures.Contains(k)).ToArray())textures.Remove(id);
                if(usedMeshes.Any(id=>!meshes.ContainsKey(id))||usedTextures.Any(id=>!textures.ContainsKey(id)))throw new InvalidDataException("Editor scene assets are incomplete.");
                if(meshes.Count>16000)foreach(long id in meshes.Keys.Where(k=>!usedMeshes.Contains(k)).ToArray())meshes.Remove(id);
                var ready=frame with{Meshes=usedMeshes.Select(id=>meshes[id]).ToArray(),Textures=textures.Values.ToArray()};
                BridgeTiming.Stop(BridgeTiming.Section.SceneDecode,decodeStart);
                Volatile.Write(ref pending,ready);Status=frame.Status;
                // Wait out the rest of the cycle, still sending pause/commands (motion waits
                // for the next scene request).
                // Switching to standalone drops a scene that arrived after its consumer stopped.
                while(true)
                {
                    if(!Active){Interlocked.Exchange(ref pending,null);break;}
                    double left=CycleMs-Stopwatch.GetElapsedTime(cycleStart).TotalMilliseconds;
                    if(left<2)break;
                    await pace.Wait(TimeSpan.FromMilliseconds(Math.Min(left,50)),stop.Token);
                    SendPause();
                }
            }
        }
        catch(Exception ex) when(ex is IOException or OperationCanceledException or ObjectDisposedException){Status=stop.IsCancellationRequested?"Editor disconnected":Process.HasExited?"Editor closed":"Editor connection ended: "+ex.Message;}
        catch(Exception ex){Status="Editor connection error: "+ex.Message;Diagnostics.Log(ex);}
    }
    internal void RequestClose(){Pause();if(!Process.HasExited)Process.CloseMainWindow();}
    public void Dispose(){stop.Cancel();/* Never kill an editor with potentially unsaved work. */}
}
