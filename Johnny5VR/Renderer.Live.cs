using System.Numerics;
using System.Runtime.InteropServices;
using Johnny5.VrBridge;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Buffer=SharpDX.Direct3D11.Buffer;

namespace Johnny5VR;

internal sealed partial class Renderer
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Surface {public Vector4 Diffuse,Emissive,Specular,Parameters;}
    private Buffer? surfaces;
    private ShaderResourceView? surfaceView,textureView;
    private Texture2D? textureArray;
    private SamplerState? textureSampler;
    private int surfaceCapacity,textureCapacity;
    private readonly Dictionary<long,int> textureSlots=[];
    private readonly Dictionary<long,(long Mesh,int Slot,bool Dynamic)> liveSlots=[];
    private readonly Dictionary<(long Node,long Mesh),int> liveStaticSlots=[];
    private readonly List<(Buffer Buffer,int Count)> liveBatches=[];
    private Buffer? liveDynamic;
    private int liveDynamicCount,liveDynamicCapacity,nextSlot=1;
    // Reused every scene update. Rebuilding these per update allocated about
    // 12 MB each time and caused one or two full garbage collections a second.
    private Vertex[] dynamicStaging=[];
    private Surface[] surfaceStaging=[];
    internal int DynamicBufferAllocations {get;private set;}
    internal int DynamicGeometryUploads {get;private set;}

    private (Vector3 Start,Vector3 End)[] rays=[];
    private DepthStencilState? pointerDepth;
    private Matrix4x4[] livePalette=[Matrix4x4.Identity];
    // Follow smoothing. Scenes arrive every 33–47 ms but the headset draws every 13 ms,
    // so driving and turning moved in visible steps. While SmoothLive is on, each piece
    // glides from where it is drawn to its new place over one measured scene interval.
    // This works the same whether the editor moves the robot or the room around it.
    internal bool SmoothLive {get;set;}
    private struct NodeMotion{public int Slot;public bool Valid,Moving;public Vector3 S0,T0,S1,T1;public Quaternion R0,R1;}
    private NodeMotion[] nodeMotion=[],nodeMotionSpare=[];
    private int nodeMotionCount;
    private Dictionary<long,int> motionIndex=[],motionIndexSpare=[];
    private long liveArrival;
    private bool blending;
    internal double LiveBlendIntervalMs {get;private set;}=EditorConnection.SceneCycleMs;
    internal bool LiveBlending=>blending;
    // Tread belts while smoothing: the belt frame is chosen every headset frame from the
    // blended belt travel (see Johnny5.VrBridge.VrTreads), not once per scene. Every frame
    // mesh has its own static slot; only the chosen one gets a visible surface.
    private sealed class TreadDraw{internal bool Left;internal int[] Slots=[];internal int CurrentSlot,Shown;internal Surface Surface;}
    private readonly List<TreadDraw> treadDraws=[];
    private double treadStep=1,treadLeft0,treadLeft1,treadRight0,treadRight1;
    private bool treadMoving;
    internal int TreadShownSlot(int layer)=>layer<treadDraws.Count?treadDraws[layer].Shown:-1;
    private const double TreadSnapMetres=.5;
    internal bool Live {get;private set;}
    internal VrPoseState? PoseState {get;private set;}
    private readonly RobotSurfacePicker posePicker=new();
    private float poseFloor,poseHeight=1.8f;
    private Vector4 liveAmbient=new(.38f,.38f,.38f,1),liveKey=new(.55f,.55f,.55f,1),liveDirection=new(-.4f,.85f,.4f,0);
    internal void ClearLive()
    {
        Live=false;PoseState=null;CaptureCamera=null;posePicker.Clear();foreach(var b in liveBatches)b.Buffer.Dispose();liveBatches.Clear();liveDynamic?.Dispose();liveDynamic=null;liveDynamicCount=liveDynamicCapacity=0;liveSlots.Clear();liveStaticSlots.Clear();nextSlot=1;
        nodeMotionCount=0;motionIndex.Clear();blending=false;liveArrival=0;treadDraws.Clear();treadMoving=false;
    }
    private float BlendFraction(long now)=>blending||treadMoving?(float)Math.Clamp(System.Diagnostics.Stopwatch.GetElapsedTime(liveArrival,now).TotalMilliseconds/LiveBlendIntervalMs,0,1):1;
    private static Matrix4x4 Compose(Vector3 s,Quaternion r,Vector3 t)=>Matrix4x4.CreateScale(s)*Matrix4x4.CreateFromQuaternion(r)*Matrix4x4.CreateTranslation(t);
    private static Matrix4x4 Blended(in NodeMotion m,float t)=>Compose(Vector3.Lerp(m.S0,m.S1,t),Quaternion.Slerp(m.R0,m.R1,t),Vector3.Lerp(m.T0,m.T1,t));
    // Called when a scene arrives, after livePalette holds its targets. Each node starts
    // from where it is drawn right now, so a scene arriving mid-glide never jumps.
    // A node that can't be split into scale, rotation and position just snaps.
    private void StartBlend(VrFrame frame)
    {
        long now=System.Diagnostics.Stopwatch.GetTimestamp();
        float shown=BlendFraction(now);
        if(liveArrival!=0)LiveBlendIntervalMs=Math.Clamp(LiveBlendIntervalMs*.8+System.Diagnostics.Stopwatch.GetElapsedTime(liveArrival,now).TotalMilliseconds*.2,15,100);
        if(nodeMotionSpare.Length<frame.Nodes.Length)nodeMotionSpare=new NodeMotion[frame.Nodes.Length+frame.Nodes.Length/4];
        motionIndexSpare.Clear();
        int count=0;bool any=false;
        foreach(var n in frame.Nodes)
        {
            if(motionIndexSpare.ContainsKey(n.Id))continue;
            ref var m=ref nodeMotionSpare[count];m=default;m.Slot=liveSlots[n.Id].Slot;
            if(SmoothLive&&Matrix4x4.Decompose(livePalette[m.Slot],out var s,out var r,out var p))
            {
                m.Valid=true;m.S0=m.S1=s;m.R0=m.R1=r;m.T0=m.T1=p;
                if(motionIndex.TryGetValue(n.Id,out int j)&&nodeMotion[j].Valid)
                {
                    ref var before=ref nodeMotion[j];float t=before.Moving?shown:1;
                    m.S0=Vector3.Lerp(before.S0,before.S1,t);m.R0=Quaternion.Slerp(before.R0,before.R1,t);m.T0=Vector3.Lerp(before.T0,before.T1,t);
                    m.Moving=Vector3.DistanceSquared(m.T0,m.T1)>1e-12f||Vector3.DistanceSquared(m.S0,m.S1)>1e-12f||MathF.Abs(Quaternion.Dot(m.R0,m.R1))<1-1e-7f;
                    if(m.Moving){livePalette[m.Slot]=Compose(m.S0,m.R0,m.T0);any=true;}
                }
            }
            motionIndexSpare[n.Id]=count++;
        }
        (nodeMotion,nodeMotionSpare)=(nodeMotionSpare,nodeMotion);(motionIndex,motionIndexSpare)=(motionIndexSpare,motionIndex);
        nodeMotionCount=count;liveArrival=now;blending=any;
        StartTreads(frame,shown);
    }
    private void StartTreads(VrFrame frame,float shown)
    {
        bool wasActive=treadDraws.Count>0;
        double left=wasActive?treadLeft0+(treadLeft1-treadLeft0)*shown:0,right=wasActive?treadRight0+(treadRight1-treadRight0)*shown:0;
        treadDraws.Clear();treadMoving=false;
        if(!SmoothLive||frame.Treads is not {} treads)return;
        foreach(var layer in treads.Layers)
        {
            if(!liveSlots.TryGetValue(layer.Node,out var current))continue;
            var slots=new int[layer.Frames.Length];bool complete=true;
            for(int i=0;i<slots.Length;i++)if(liveStaticSlots.TryGetValue((layer.Node,layer.Frames[i]),out int slot))slots[i]=slot;else complete=false;
            if(complete)treadDraws.Add(new(){Left=layer.Left,Slots=slots,CurrentSlot=current.Slot,Shown=current.Slot,Surface=surfaceStaging[current.Slot]});
        }
        if(treadDraws.Count==0)return;
        treadStep=treads.PhaseStep;treadLeft1=treads.LeftTravel;treadRight1=treads.RightTravel;
        // A first scene, or a jump such as seeking, starts at the new travel instead of spinning the belt.
        if(!wasActive||Math.Abs(treadLeft1-left)>TreadSnapMetres||Math.Abs(treadRight1-right)>TreadSnapMetres){left=treadLeft1;right=treadRight1;}
        treadLeft0=left;treadRight0=right;
        treadMoving=treadLeft0!=treadLeft1||treadRight0!=treadRight1;
        if(DrawTreads(0))context.UpdateSubresource(surfaceStaging,surfaces!);
    }
    // Shows each belt's frame for the travel at fraction t; true when surfaces changed.
    private bool DrawTreads(float t)
    {
        bool changed=false;
        double left=treadLeft0+(treadLeft1-treadLeft0)*t,right=treadRight0+(treadRight1-treadRight0)*t;
        foreach(var d in treadDraws)
        {
            int slot=d.Slots[VrTreads.FrameIndex(d.Left?left:right,treadStep,d.Slots.Length)];
            livePalette[slot]=livePalette[d.CurrentSlot];
            if(slot!=d.Shown){surfaceStaging[d.Shown]=default;surfaceStaging[slot]=d.Surface;d.Shown=slot;changed=true;}
        }
        return changed;
    }
    // Called every rendered frame; only does work while something is gliding.
    internal void AdvanceLive()
    {
        if(!(blending||treadMoving)||!Live)return;
        float t=BlendFraction(System.Diagnostics.Stopwatch.GetTimestamp());
        if(blending)for(int i=0;i<nodeMotionCount;i++){ref var m=ref nodeMotion[i];if(m.Moving)livePalette[m.Slot]=Blended(m,t);}
        if(DrawTreads(t))context.UpdateSubresource(surfaceStaging,surfaces!);
        UpdatePose(livePalette);
        if(t>=1)blending=treadMoving=false;
    }
    internal string CheckRobotPicking()
    {
        var watch=System.Diagnostics.Stopwatch.StartNew();int hits=0,rays=0;
        foreach(var handle in PoseState?.Handles.Take(20)??Enumerable.Empty<VrPoseHandle>())
        {
            rays++;if(posePicker.Hit(handle.Anchor+Vector3.UnitZ*.4f,-Vector3.UnitZ)!=null)hits++;
        }
        if(hits==0)throw new InvalidOperationException("No robot surfaces were hit in the live editor scene.");
        return $"PASS: {hits}/{rays} model probes hit real robot triangles; {watch.Elapsed.TotalMilliseconds:F2} ms total.";
    }
    private void EnsureSurfaces(int count)
    {
        if(surfaceCapacity>=count)return;
        surfaceCapacity=Math.Max(count,Math.Max(16,surfaceCapacity*2));surfaceView?.Dispose();surfaces?.Dispose();
        surfaces=new(device,new BufferDescription(surfaceCapacity*64,ResourceUsage.Default,BindFlags.ShaderResource,CpuAccessFlags.None,ResourceOptionFlags.BufferStructured,64));surfaceView=new(device,surfaces);
    }
    private void DefaultSurfaces()
    {
        EnsureSurfaces(1);var data=new Surface[surfaceCapacity];data[0]=new(){Diffuse=Vector4.One,Specular=new(.18f,.18f,.18f,1),Parameters=new(-1,40,0,0)};context.UpdateSubresource(data,surfaces!);
        textureSampler??=new(device,new SamplerStateDescription{Filter=Filter.MinMagMipLinear,AddressU=TextureAddressMode.Wrap,AddressV=TextureAddressMode.Wrap,AddressW=TextureAddressMode.Clamp,MaximumLod=float.MaxValue});
    }
    internal void ApplyLive(VrFrame frame,ViewerSettings settings)
    {
        if(nextSlot>12000)ClearLive();
        var meshes=frame.Meshes.ToDictionary(m=>m.Id);var materials=frame.Materials.ToDictionary(m=>m.Id);
        var fresh=new List<VrNode>();
        foreach(var n in frame.Nodes)
        {
            var mesh=meshes[n.Mesh];
            if(!liveSlots.TryGetValue(n.Id,out var slot)||slot.Dynamic!=mesh.Dynamic||!mesh.Dynamic&&slot.Mesh!=n.Mesh)
            {
                if(!mesh.Dynamic&&liveStaticSlots.TryGetValue((n.Id,n.Mesh),out int cached))slot=(n.Mesh,cached,false);
                else
                {
                    slot=(n.Mesh,nextSlot++,mesh.Dynamic);
                    if(!mesh.Dynamic){fresh.Add(n);liveStaticSlots[(n.Id,n.Mesh)]=slot.Slot;}
                }
                liveSlots[n.Id]=slot;
            }
        }
        var treadFresh=new List<(VrMesh Mesh,int Slot)>();
        if(frame.Treads is {} treads)
            foreach(var layer in treads.Layers)
                foreach(long frameMesh in layer.Frames)
                    if(!liveStaticSlots.ContainsKey((layer.Node,frameMesh))&&meshes.TryGetValue(frameMesh,out var data)&&!data.Dynamic)
                    {int slot=nextSlot++;liveStaticSlots[(layer.Node,frameMesh)]=slot;treadFresh.Add((data,slot));}
        if(nextSlot>20000)throw new InvalidOperationException("VR scene has too many accumulated mesh revisions. Return to standalone, then reconnect the editor.");
        var axes=new Matrix4x4(0,0,1,0,1,0,0,0,0,1,0,0,0,0,0,1);
        float scale=frame.Model.Contains("FullBody",StringComparison.OrdinalIgnoreCase)?settings.RobotHeightMetres/Math.Max(.01f,frame.RobotHeight):1;
        var placement=Matrix4x4.CreateScale(settings.Scale)*axes*Matrix4x4.CreateRotationY(settings.Heading*MathF.PI/180)*Matrix4x4.CreateTranslation(settings.Horizontal,0,-settings.Distance);
        var robotPlacement=Matrix4x4.CreateScale(scale)*Matrix4x4.CreateTranslation(0,0,-frame.RobotFloor*scale)*placement;
        poseFloor=Vector3.Transform(new Vector3(0,0,frame.RobotFloor),robotPlacement).Y;
        poseHeight=frame.RobotHeight*scale*settings.Scale;
        CaptureCamera=frame.CaptureCamera is {} camera
            ?new(Vector3.Transform(camera.Eye,robotPlacement),Vector3.Transform(camera.Target,robotPlacement)):null;
        posePicker.Update(frame,robotPlacement);
        var stagePlacement=Matrix4x4.CreateTranslation(0,0,-frame.StageFloor)*placement;
        PoseState=frame.Pose is {} pose?pose with{Handles=pose.Handles.Select(h=>h with{Anchor=Vector3.Transform(h.Anchor,robotPlacement)}).ToArray()}:null;
        // Both arrays must match their GPU buffers' sizes exactly; reuse them while they do.
        if(livePalette.Length!=nextSlot)livePalette=new Matrix4x4[nextSlot];
        Array.Fill(livePalette,Matrix4x4.Identity);
        EnsureSurfaces(nextSlot);
        if(surfaceStaging.Length!=surfaceCapacity)surfaceStaging=new Surface[surfaceCapacity];else Array.Clear(surfaceStaging);
        var surfaceData=surfaceStaging;
        surfaceData[0]=new(){Diffuse=Vector4.One,Emissive=new(.2f,.2f,.2f,0),Specular=Vector4.Zero,Parameters=new(-1,1,0,0)};
        UpdateTextures(frame.Textures);
        foreach(var n in frame.Nodes)
        {
            int index=liveSlots[n.Id].Slot;livePalette[index]=n.World*(n.Robot?robotPlacement:stagePlacement);
            var m=materials[n.Material];surfaceData[index]=new(){Diffuse=m.Diffuse,Emissive=m.Emissive,Specular=m.Specular,Parameters=new(m.Texture!=0?textureSlots[m.Texture]:-1,m.Power,0,0)};
        }
        context.UpdateSubresource(surfaceData,surfaces!);
        Vertex[] Vertices(IEnumerable<VrNode> nodes)
        {
            var list=new List<Vertex>();
            foreach(var n in nodes)
            {
                var m=meshes[n.Mesh];int slot=liveSlots[n.Id].Slot;
                foreach(int i in m.Indices)list.Add(new(new(m.Positions[i*3],m.Positions[i*3+1],m.Positions[i*3+2]),new(m.Normals[i*3],m.Normals[i*3+1],m.Normals[i*3+2]),Vector4.One){Bone=slot,UV=new(m.UV[i*2],m.UV[i*2+1])});
            }
            return list.ToArray();
        }
        if(fresh.Count>0){var v=Vertices(fresh);if(v.Length>0)liveBatches.Add((Buffer.Create(device,BindFlags.VertexBuffer,v),v.Length));}
        if(treadFresh.Count>0)
        {
            var list=new List<Vertex>();
            foreach(var (m,slot) in treadFresh)foreach(int i in m.Indices)list.Add(new(new(m.Positions[i*3],m.Positions[i*3+1],m.Positions[i*3+2]),new(m.Normals[i*3],m.Normals[i*3+1],m.Normals[i*3+2]),Vector4.One){Bone=slot,UV=new(m.UV[i*2],m.UV[i*2+1])});
            if(list.Count>0)liveBatches.Add((Buffer.Create(device,BindFlags.VertexBuffer,list.ToArray()),list.Count));
        }
        // Dynamic geometry is still rebuilt and uploaded for every scene update (no
        // change detection), but into a reused CPU array and a reused GPU buffer that
        // only grows. The GPU write discards the old contents, so a frame never sees
        // a half-written buffer.
        int count=0,allocationsBefore=DynamicBufferAllocations;
        foreach(var n in frame.Nodes){var m=meshes[n.Mesh];if(m.Dynamic)count+=m.Indices.Length;}
        if(dynamicStaging.Length<count)dynamicStaging=new Vertex[count+count/4];
        int written=0;
        foreach(var n in frame.Nodes)
        {
            var m=meshes[n.Mesh];if(!m.Dynamic)continue;int slot=liveSlots[n.Id].Slot;
            foreach(int i in m.Indices)dynamicStaging[written++]=new(new(m.Positions[i*3],m.Positions[i*3+1],m.Positions[i*3+2]),new(m.Normals[i*3],m.Normals[i*3+1],m.Normals[i*3+2]),Vector4.One){Bone=slot,UV=new(m.UV[i*2],m.UV[i*2+1])};
        }
        liveDynamicCount=count;
        if(count>0)
        {
            if(liveDynamic==null||liveDynamicCapacity<count)
            {
                liveDynamic?.Dispose();liveDynamicCapacity=count+count/4;
                liveDynamic=new Buffer(device,new BufferDescription(liveDynamicCapacity*Vertex.Stride,ResourceUsage.Dynamic,BindFlags.VertexBuffer,CpuAccessFlags.Write,ResourceOptionFlags.None,0));
                DynamicBufferAllocations++;
            }
            var box=context.MapSubresource(liveDynamic,0,MapMode.WriteDiscard,SharpDX.Direct3D11.MapFlags.None);
            try{SharpDX.Utilities.Write(box.DataPointer,dynamicStaging,0,count);}finally{context.UnmapSubresource(liveDynamic,0);}
            DynamicGeometryUploads++;
        }
        BridgeTiming.Record(BridgeTiming.Value.DynamicVertices,liveDynamicCount);
        BridgeTiming.Record(BridgeTiming.Value.DynamicBufferAllocations,DynamicBufferAllocations-allocationsBefore);
        if(bones==null||bones.Description.SizeInBytes!=nextSlot*64)
        {
            boneView?.Dispose();bones?.Dispose();bones=new(device,new BufferDescription(nextSlot*64,ResourceUsage.Default,BindFlags.ShaderResource,CpuAccessFlags.None,ResourceOptionFlags.BufferStructured,64));boneView=new(device,bones);
        }
        StartBlend(frame);UpdatePose(livePalette);Apertures=Vector4.Zero;Live=true;
        var ambient=frame.Lights.Where(l=>l.Ambient).Aggregate(Vector4.Zero,(v,l)=>v+l.Color);liveAmbient=Vector4.Min(ambient,new(.8f));
        var key=frame.Lights.FirstOrDefault(l=>!l.Ambient);
        if(key!=null){liveKey=key.Color*.65f;liveDirection=new(Vector3.TransformNormal(-key.Direction,axes),0);}
    }
    private void UpdateTextures(VrTexture[] textures)
    {
        var active=textures.Select(t=>t.Id).ToHashSet();foreach(long id in textureSlots.Keys.Where(k=>!active.Contains(k)).ToArray())textureSlots.Remove(id);
        if(textures.Length>2048)throw new InvalidOperationException("The live scene exceeds the Direct3D texture layer limit.");
        if(textureArray==null||textureCapacity<textures.Length)
        {
            textureCapacity=Math.Min(2048,Math.Max(8,textures.Length+8));textureView?.Dispose();textureArray?.Dispose();textureSlots.Clear();
            textureArray=new(device,new Texture2DDescription{Width=256,Height=256,ArraySize=textureCapacity,MipLevels=1,Format=Format.R8G8B8A8_UNorm,SampleDescription=new(1,0),Usage=ResourceUsage.Default,BindFlags=BindFlags.ShaderResource});
            textureView=new(device,textureArray);
        }
        foreach(var t in textures)
        {
            if(textureSlots.ContainsKey(t.Id))continue;
            int slot=Enumerable.Range(0,textureCapacity).First(i=>!textureSlots.ContainsValue(i));
            using var stream=new MemoryStream(t.Png);using var source=new Bitmap(stream);using var bitmap=new Bitmap(source,256,256);
            var bytes=bitmap.LockBits(new(0,0,256,256),System.Drawing.Imaging.ImageLockMode.ReadOnly,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                var rgba=new byte[256*256*4];for(int y=0;y<256;y++)Marshal.Copy(bytes.Scan0+y*bytes.Stride,rgba,y*256*4,256*4);for(int i=0;i<rgba.Length;i+=4)(rgba[i],rgba[i+2])=(rgba[i+2],rgba[i]);
                var pin=GCHandle.Alloc(rgba,GCHandleType.Pinned);try{context.UpdateSubresource(new SharpDX.DataBox(pin.AddrOfPinnedObject(),256*4,0),textureArray,slot);}finally{pin.Free();}
            }
            finally{bitmap.UnlockBits(bytes);}textureSlots[t.Id]=slot;
        }
    }
    private void DrawLive(ref Frame frame)
    {
        for(int pass=0;pass<2;pass++)
        {
            frame.Flags=new(pass,1,0,0);frame.Ambient=liveAmbient;frame.Key=liveKey;frame.Light=liveDirection;context.UpdateSubresource(ref frame,constants);
            context.OutputMerger.SetDepthStencilState(pass==0?solidDepth:glassDepth);context.OutputMerger.SetBlendState(pass==0?null:blend);
            foreach(var b in liveBatches){context.InputAssembler.SetVertexBuffers(0,new VertexBufferBinding(b.Buffer,Vertex.Stride,0));context.Draw(b.Count,0);}
            if(liveDynamic!=null&&liveDynamicCount>0){context.InputAssembler.SetVertexBuffers(0,new VertexBufferBinding(liveDynamic,Vertex.Stride,0));context.Draw(liveDynamicCount,0);}
        }
    }
    internal void VerifyStaticFrameReuse()
    {
        var mesh=new VrMesh(1,false,[0,0,0,1,0,0,0,1,0],[0,0,1,0,0,1,0,0,1],[0,0,1,0,0,1],[0,1,2]);
        var meshes=new[]{mesh,mesh with{Id=2}};
        for(int i=0;i<40;i++)ApplyLive(new(i,"check","fixture",0,1,0,[new(10,meshes[i%2].Id,20,Matrix4x4.Identity,true)],meshes,[new(20,Vector4.One,Vector4.Zero,Vector4.Zero,1,0)],[],[]),new());
        if(liveStaticSlots.Count!=2||liveBatches.Count!=2)throw new InvalidOperationException("Repeating tread frames rebuilt their cached GPU geometry.");
        var dynamicMesh=mesh with{Id=3,Dynamic=true};int allocations=DynamicBufferAllocations,uploads=DynamicGeometryUploads;
        void DynamicFrame(VrMesh m,int frame)=>ApplyLive(new(frame,"check","fixture",0,1,0,[new(11,m.Id,20,Matrix4x4.CreateTranslation(frame*.001f,0,0),true)],[m],[new(20,Vector4.One,Vector4.Zero,Vector4.Zero,1,0)],[],[]),new());
        for(int i=0;i<40;i++)DynamicFrame(dynamicMesh,i);
        if(DynamicGeometryUploads-uploads!=40)throw new InvalidOperationException("Dynamic geometry was not uploaded for every scene update.");
        if(DynamicBufferAllocations-allocations>1)throw new InvalidOperationException("Dynamic geometry created a new GPU buffer for each scene update.");
        DynamicFrame(dynamicMesh with{Id=4,Positions=[0,0,0,2,0,0,0,1,0]},41);
        if(DynamicGeometryUploads-uploads!=41||DynamicBufferAllocations-allocations>1)throw new InvalidOperationException("A mesh revision did not upload into the existing GPU buffer.");
        var big=dynamicMesh with{Id=5,Indices=Enumerable.Repeat(new[]{0,1,2},400).SelectMany(x=>x).ToArray()};
        DynamicFrame(big,42);
        if(liveDynamicCount!=1200||liveDynamicCapacity<1200)throw new InvalidOperationException("Dynamic geometry did not grow its GPU buffer for a larger mesh.");
        DynamicFrame(dynamicMesh,43);
        if(liveDynamicCount!=3)throw new InvalidOperationException("Dynamic geometry drew a stale vertex count after shrinking.");
        File.WriteAllText(Path.Combine(Diagnostics.Folder,"live-frame-cache-check.txt"),"PASS: static geometry retains its existing cache; dynamic geometry is rebuilt and uploaded for every snapshot into one reused GPU buffer, which grows for larger meshes.");
    }
    internal void VerifyLiveSmoothing()
    {
        ClearLive();
        var mesh=new VrMesh(1,false,[0,0,0,1,0,0,0,1,0],[0,0,1,0,0,1,0,0,1],[0,0,1,0,0,1],[0,1,2]);
        var settings=new ViewerSettings();
        Matrix4x4 Shown()=>livePalette[liveSlots[10].Slot];
        void Scene(int sequence,Matrix4x4 world)=>ApplyLive(new(sequence,"check","fixture",0,1,0,[new(10,1,20,world,true)],[mesh],[new(20,Vector4.One,Vector4.Zero,Vector4.Zero,1,0)],[],[]),settings);
        void Age(double fraction)=>liveArrival-=(long)(LiveBlendIntervalMs*fraction*System.Diagnostics.Stopwatch.Frequency/1000);
        void Expect(bool ok,string what){if(!ok)throw new InvalidOperationException("Follow smoothing: "+what);}
        Vector3 P()=>Shown().Translation;
        SmoothLive=true;
        Scene(1,Matrix4x4.Identity);var p0=P();
        Scene(2,Matrix4x4.CreateTranslation(1,0,0));Vector3 p1;
        Expect(LiveBlending&&(P()-p0).Length()<1e-4f,"a new scene starts from where the robot is drawn.");
        // Find the placed position of the target by letting the glide finish.
        Age(1.01);AdvanceLive();p1=P();
        Expect(!LiveBlending&&(p1-p0).Length()>.01f,"the glide ends at the new scene.");
        Scene(3,Matrix4x4.Identity);Age(.5);AdvanceLive();var half=P();
        Expect((half-(p0+p1)/2).Length()<.02f*(p1-p0).Length()+1e-4f,"halfway through the interval the robot is halfway.");
        Scene(4,Matrix4x4.CreateTranslation(1,0,0));
        Expect((P()-half).Length()<.02f*(p1-p0).Length()+1e-4f,"a scene arriving mid-glide does not jump.");
        Age(1.01);AdvanceLive();
        Scene(5,Matrix4x4.CreateRotationZ(MathF.PI/2));Age(.5);AdvanceLive();
        var forward=Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitX,Shown()));
        var start=Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitX,livePaletteTarget(Matrix4x4.Identity)));
        var end=Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitX,livePaletteTarget(Matrix4x4.CreateRotationZ(MathF.PI/2))));
        float angle=MathF.Acos(Math.Clamp(Vector3.Dot(forward,start),-1,1)),total=MathF.Acos(Math.Clamp(Vector3.Dot(end,start),-1,1));
        Expect(total>1&&MathF.Abs(angle-total/2)<.03f,"turning glides through the middle angle.");
        Age(1.01);AdvanceLive();
        SmoothLive=false;Scene(6,Matrix4x4.CreateTranslation(1,0,0));
        Expect(!LiveBlending&&(P()-p1).Length()<1e-4f,"outside Follow, scenes are drawn as they arrive.");
        // Tread belt: four frame meshes; the player picks the frame from the blended travel.
        ClearLive();
        var frames=new[]{31L,32,33,34}.Select(id=>mesh with{Id=id}).ToArray();
        void Tread(int sequence,double travel,long current,Matrix4x4 world)=>ApplyLive(new(sequence,"check","fixture",0,1,0,[new(30,current,20,world,true)],frames,[new(20,Vector4.One,Vector4.Zero,Vector4.Zero,1,0)],[],[]){Treads=new(.01,travel,travel,[new(30,true,[31,32,33,34])])},settings);
        int FrameSlot(long frame)=>liveStaticSlots[(30,frame)];
        bool OnlyVisible(long frame)=>frames.All(f=>(surfaceStaging[FrameSlot(f.Id)].Diffuse.W>0)==(f.Id==frame));
        SmoothLive=true;
        Tread(10,0,31,Matrix4x4.Identity);
        Expect(OnlyVisible(31),"the first tread scene shows its own belt frame.");
        Tread(11,-.02,33,Matrix4x4.CreateTranslation(1,0,0));
        Expect(OnlyVisible(31),"a new tread scene starts from the belt frame being drawn.");
        Age(.5);AdvanceLive();
        Expect(OnlyVisible(32)&&livePalette[FrameSlot(32)]==livePalette[FrameSlot(33)],"halfway, the belt shows the in-between frame where the tread is drawn.");
        Age(1.01);AdvanceLive();
        Expect(OnlyVisible(33),"the belt ends on the scene's frame.");
        Tread(12,40.015,31,Matrix4x4.Identity);
        Expect(FrameIndexMesh(40.015)!=31&&OnlyVisible(FrameIndexMesh(40.015)),"a large travel jump snaps instead of spinning the belt.");
        SmoothLive=false;Tread(13,-.03,34,Matrix4x4.Identity);
        Expect(OnlyVisible(34),"outside Follow, the belt shows the frame the editor sent.");
        SmoothLive=false;ClearLive();
        File.WriteAllText(Path.Combine(Diagnostics.Folder,"live-smoothing-check.txt"),"PASS: while following, pieces glide from where they are drawn to each new scene over one scene interval (position and turn), never jump when a scene arrives mid-glide, and are drawn as they arrive outside Follow. Tread belts show the frame for the blended belt travel on every headset frame, snap on large jumps, and show the editor's frame outside Follow.");
        static long FrameIndexMesh(double travel)=>31+VrTreads.FrameIndex(travel,.01,4);
        Matrix4x4 livePaletteTarget(Matrix4x4 world)
        {
            var axes=new Matrix4x4(0,0,1,0,1,0,0,0,0,1,0,0,0,0,0,1);
            var placement=Matrix4x4.CreateScale(settings.Scale)*axes*Matrix4x4.CreateRotationY(settings.Heading*MathF.PI/180)*Matrix4x4.CreateTranslation(settings.Horizontal,0,-settings.Distance);
            return world*placement;
        }
    }
    private void DrawPointer(ref Frame frame,Vector3 eye)
    {
        if(rays.Length==0)return;
        var vertices=new List<Vertex>();
        foreach(var segment in rays)
        {
        var direction=segment.End-segment.Start;if(direction.LengthSquared()<1e-8f)continue;
        var view=Vector3.Normalize(eye-segment.End);var side=Vector3.Cross(Vector3.Normalize(direction),view);side=side.LengthSquared()<1e-8f?Vector3.UnitX:Vector3.Normalize(side);
        var cyan=new Vector4(.05f,1,1,1);
        void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d){foreach(var p in new[]{a,b,c,a,c,d})vertices.Add(new(p,view,cyan));}
        Quad(segment.Start-side*.002f,segment.Start+side*.002f,segment.End+side*.002f,segment.End-side*.002f);
        var up=Vector3.Normalize(Vector3.Cross(view,side))*.006f;var right=side*.006f;var end=segment.End+view*.004f;
        Quad(end-right-up,end+right-up,end+right+up,end-right+up);
        }
        if(vertices.Count==0)return;
        using var buffer=Buffer.Create(device,BindFlags.VertexBuffer,vertices.ToArray());
        frame.Flags=new(0,0,1,0);context.UpdateSubresource(ref frame,constants);
        pointerDepth??=new(device,new DepthStencilStateDescription{IsDepthEnabled=false});context.OutputMerger.SetDepthStencilState(pointerDepth);context.OutputMerger.SetBlendState(null);
        context.InputAssembler.SetVertexBuffers(0,new VertexBufferBinding(buffer,Vertex.Stride,0));context.Draw(vertices.Count,0);
    }
}

