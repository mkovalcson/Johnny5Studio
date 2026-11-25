#nullable enable
using System.Numerics;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Johnny5.VrBridge;

// Shared transport only: neither application references the other's UI/runtime.
public sealed record VrMesh(long Id,bool Dynamic,float[] Positions,float[] Normals,float[] UV,int[] Indices);
public sealed record VrTexture(long Id,byte[] Png);
public sealed record VrMaterial(long Id,Vector4 Diffuse,Vector4 Emissive,Vector4 Specular,float Power,long Texture);
public sealed record VrNode(long Id,long Mesh,long Material,Matrix4x4 World,bool Robot);
public sealed record VrLight(Vector4 Color,Vector3 Direction,bool Ambient);
public sealed record VrFrame(long Sequence,string Status,string Model,float RobotFloor,float RobotHeight,float StageFloor,VrNode[] Nodes,VrMesh[] Meshes,VrMaterial[] Materials,VrTexture[] Textures,VrLight[] Lights)
{
    public VrPoseState? Pose {get;init;}
    public long BaseSequence {get;init;}
    public long[] RemovedNodes {get;init;}=[];
    public VrEditorMetrics Metrics {get;init;}=new();
    public VrTreads? Treads {get;init;}
    public VrCaptureCamera? CaptureCamera {get;init;}
}
// Tread belts are a fixed set of mesh frames that the editor swaps as the belt travels.
// Sending the frame list and each belt's travel lets the player pick the frame on every
// headset frame instead of once per scene (at full speed a scene advances about 10 of the
// 12 frames, which otherwise looks like the belt crawling backwards).
public sealed record VrTreadLayer(long Node,bool Left,long[] Frames);
public sealed record VrTreads(double PhaseStep,double LeftTravel,double RightTravel,VrTreadLayer[] Layers)
{
    // Same mapping as the editor's UrdfScene.SetTreadTravel: increasing manifest phases drive backwards.
    public static int FrameIndex(double travel,double phaseStep,int frames)=>(int)((Math.Floor(-travel/phaseStep)%frames+frames)%frames);
}
public sealed record VrCaptureCamera(Vector3 Eye,Vector3 Target);
public sealed record VrEditorMetrics(double UiWaitMs=0,double MotionMs=0,double CaptureMs=0,double PoseMs=0,int CachedBranches=0,int VisitedBranches=0,int MetadataBuilds=0,int FingerprintBuilds=0);


public static class SceneWire
{
    public const int Version=12,MaxPacket=512*1024*1024;
    public static byte[] Encode(VrFrame frame,ISet<long> knownMeshes,ISet<long> knownTextures)
    {
        using var memory=new MemoryStream();
        using(var zip=new GZipStream(memory,CompressionLevel.Fastest,true))
        using(var w=new BinaryWriter(zip,Encoding.UTF8,true))
        {
            w.Write(Version);w.Write(frame.Sequence);w.Write(frame.Status);w.Write(frame.Model);w.Write(frame.RobotFloor);w.Write(frame.RobotHeight);w.Write(frame.StageFloor);
            w.Write(frame.BaseSequence);w.Write(frame.RemovedNodes.Length);foreach(long id in frame.RemovedNodes)w.Write(id);
            w.Write(frame.Nodes.Length);foreach(var n in frame.Nodes){w.Write(n.Id);w.Write(n.Mesh);w.Write(n.Material);Matrix(w,n.World);w.Write(n.Robot);}
            var meshes=frame.Meshes.Where(m=>!knownMeshes.Contains(m.Id)).ToArray();
            w.Write(meshes.Length);foreach(var m in meshes){w.Write(m.Id);w.Write(m.Dynamic);Floats(w,m.Positions);Floats(w,m.Normals);Floats(w,m.UV);w.Write(m.Indices.Length);foreach(int i in m.Indices)w.Write(i);}
            w.Write(frame.Materials.Length);foreach(var m in frame.Materials){w.Write(m.Id);Vector(w,m.Diffuse);Vector(w,m.Emissive);Vector(w,m.Specular);w.Write(m.Power);w.Write(m.Texture);}
            var textures=frame.Textures.Where(t=>!knownTextures.Contains(t.Id)).ToArray();
            w.Write(textures.Length);foreach(var t in textures){w.Write(t.Id);w.Write(t.Png.Length);w.Write(t.Png);}
            w.Write(frame.Lights.Length);foreach(var l in frame.Lights){Vector(w,l.Color);w.Write(l.Direction.X);w.Write(l.Direction.Y);w.Write(l.Direction.Z);w.Write(l.Ambient);}
            PoseWire.Write(w,frame.Pose);
            var stats=frame.Metrics;w.Write(stats.UiWaitMs);w.Write(stats.MotionMs);w.Write(stats.CaptureMs);w.Write(stats.PoseMs);
            w.Write(stats.CachedBranches);w.Write(stats.VisitedBranches);w.Write(stats.MetadataBuilds);w.Write(stats.FingerprintBuilds);
            w.Write(frame.CaptureCamera!=null);
            if(frame.CaptureCamera is {} camera)
            {
                Vector(w,new Vector4(camera.Eye,0));
                Vector(w,new Vector4(camera.Target,0));
            }
            w.Write(frame.Treads!=null);
            if(frame.Treads is {} treads)
            {
                w.Write(treads.PhaseStep);w.Write(treads.LeftTravel);w.Write(treads.RightTravel);w.Write(treads.Layers.Length);
                foreach(var layer in treads.Layers){w.Write(layer.Node);w.Write(layer.Left);w.Write(layer.Frames.Length);foreach(long mesh in layer.Frames)w.Write(mesh);}
            }
        }
        if(memory.Length>MaxPacket)throw new InvalidDataException("VR scene packet is too large.");
        return memory.ToArray();
    }
    public static VrFrame Decode(byte[] data)
    {
        if(data.Length>MaxPacket)throw new InvalidDataException("VR packet is too large.");
        using var memory=new MemoryStream(data);using var zip=new GZipStream(memory,CompressionMode.Decompress);using var r=new BinaryReader(zip,Encoding.UTF8);
        if(r.ReadInt32()!=Version)throw new InvalidDataException("Editor and VR viewer protocol versions differ.");
        long sequence=r.ReadInt64();string status=r.ReadString(),model=r.ReadString();float floor=r.ReadSingle(),height=r.ReadSingle(),stage=r.ReadSingle();
        long baseSequence=r.ReadInt64();var removed=new long[Count(r,20000)];for(int i=0;i<removed.Length;i++)removed[i]=r.ReadInt64();
        var nodes=new VrNode[Count(r,20000)];for(int i=0;i<nodes.Length;i++)nodes[i]=new(r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),Matrix(r),r.ReadBoolean());
        var meshes=new VrMesh[Count(r,20000)];for(int i=0;i<meshes.Length;i++){long id=r.ReadInt64();bool dynamic=r.ReadBoolean();var p=Floats(r);var n=Floats(r);var uv=Floats(r);var indices=new int[Count(r,32_000_000)];for(int j=0;j<indices.Length;j++)indices[j]=r.ReadInt32();if(p.Length%3!=0||n.Length!=p.Length||uv.Length!=p.Length/3*2||indices.Any(v=>v<0||v>=p.Length/3))throw new InvalidDataException("Invalid VR mesh.");meshes[i]=new(id,dynamic,p,n,uv,indices);}
        var materials=new VrMaterial[Count(r,20000)];for(int i=0;i<materials.Length;i++)materials[i]=new(r.ReadInt64(),Vector(r),Vector(r),Vector(r),r.ReadSingle(),r.ReadInt64());
        var textures=new VrTexture[Count(r,2048)];for(int i=0;i<textures.Length;i++){long id=r.ReadInt64();int count=Count(r,16*1024*1024);byte[] png=r.ReadBytes(count);if(png.Length!=count)throw new EndOfStreamException();textures[i]=new(id,png);}
        var lights=new VrLight[Count(r,128)];for(int i=0;i<lights.Length;i++)lights[i]=new(Vector(r),new(r.ReadSingle(),r.ReadSingle(),r.ReadSingle()),r.ReadBoolean());
        var pose=PoseWire.Read(r);
        var metrics=new VrEditorMetrics(r.ReadDouble(),r.ReadDouble(),r.ReadDouble(),r.ReadDouble(),Count(r,200000),Count(r,200000),Count(r,20000),Count(r,20000));
        if(new[]{metrics.UiWaitMs,metrics.MotionMs,metrics.CaptureMs,metrics.PoseMs}.Any(v=>!double.IsFinite(v)||v<0))throw new InvalidDataException("Invalid editor timings.");
        VrCaptureCamera? camera=null;
        if(r.ReadBoolean())
        {
            var eye=Vector(r);var target=Vector(r);
            if(!float.IsFinite(eye.X)||!float.IsFinite(eye.Y)||!float.IsFinite(eye.Z)||
                !float.IsFinite(target.X)||!float.IsFinite(target.Y)||!float.IsFinite(target.Z))
                throw new InvalidDataException("Invalid capture camera.");
            camera=new(new(eye.X,eye.Y,eye.Z),new(target.X,target.Y,target.Z));
        }
        VrTreads? treadState=null;
        if(r.ReadBoolean())
        {
            double step=r.ReadDouble(),left=r.ReadDouble(),right=r.ReadDouble();
            if(!double.IsFinite(step)||step<=0||!double.IsFinite(left)||!double.IsFinite(right))throw new InvalidDataException("Invalid tread state.");
            var layers=new VrTreadLayer[Count(r,64)];
            for(int i=0;i<layers.Length;i++){long node=r.ReadInt64();bool isLeft=r.ReadBoolean();var frames=new long[Count(r,240)];for(int j=0;j<frames.Length;j++)frames[j]=r.ReadInt64();if(frames.Length<2)throw new InvalidDataException("Invalid tread frames.");layers[i]=new(node,isLeft,frames);}
            treadState=new(step,left,right,layers);
        }
        return new(sequence,status,model,floor,height,stage,nodes,meshes,materials,textures,lights){Pose=pose,BaseSequence=baseSequence,RemovedNodes=removed,Metrics=metrics,Treads=treadState,CaptureCamera=camera};
    }
    public static int Count(BinaryReader r,int maximum){int count=r.ReadInt32();if(count<0||count>maximum)throw new InvalidDataException("Invalid VR array size.");return count;}
    private static void Floats(BinaryWriter w,float[] values){w.Write(values.Length);foreach(float v in values)w.Write(v);}
    private static float[] Floats(BinaryReader r){var result=new float[Count(r,32_000_000)];for(int i=0;i<result.Length;i++){result[i]=r.ReadSingle();if(!float.IsFinite(result[i]))throw new InvalidDataException("Non-finite VR geometry.");}return result;}
    private static void Vector(BinaryWriter w,Vector4 v){w.Write(v.X);w.Write(v.Y);w.Write(v.Z);w.Write(v.W);}
    private static Vector4 Vector(BinaryReader r)=>new(r.ReadSingle(),r.ReadSingle(),r.ReadSingle(),r.ReadSingle());
    private static void Matrix(BinaryWriter w,Matrix4x4 m){foreach(float f in new[]{m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44})w.Write(f);}
    private static Matrix4x4 Matrix(BinaryReader r)=>new(r.ReadSingle(),r.ReadSingle(),r.ReadSingle(),r.ReadSingle(),r.ReadSingle(),r.ReadSingle(),r.ReadSingle(),r.ReadSingle(),r.ReadSingle(),r.ReadSingle(),r.ReadSingle(),r.ReadSingle(),r.ReadSingle(),r.ReadSingle(),r.ReadSingle(),r.ReadSingle());
}

// Ordered pipe snapshots: the receiver merges every packet before publishing the latest complete frame.
public sealed class VrSceneDeltaWriter
{
    private VrFrame? previous;
    private long sequence;
    public VrFrame Prepare(VrFrame snapshot,long acknowledgedSequence)
    {
        var full=snapshot with{Sequence=++sequence,BaseSequence=0,RemovedNodes=[]};
        var old=previous;previous=full;
        bool reset=old==null||old.Sequence!=acknowledgedSequence||old.Model!=full.Model||old.RobotFloor!=full.RobotFloor||old.RobotHeight!=full.RobotHeight||sequence%120==0;
        if(reset)return full;
        var nodes=old!.Nodes.ToDictionary(n=>n.Id);var materials=old.Materials.ToDictionary(m=>m.Id);var active=full.Nodes.Select(n=>n.Id).ToHashSet();
        return full with{BaseSequence=old.Sequence,Nodes=full.Nodes.Where(n=>!nodes.TryGetValue(n.Id,out var before)||before!=n).ToArray(),
            RemovedNodes=old.Nodes.Where(n=>!active.Contains(n.Id)).Select(n=>n.Id).ToArray(),
            Materials=full.Materials.Where(m=>!materials.TryGetValue(m.Id,out var before)||before!=m).ToArray()};
    }
}
public sealed class VrSceneAccumulator
{
    private readonly Dictionary<long,VrNode> nodes=[];
    private readonly Dictionary<long,VrMaterial> materials=[];
    public long Sequence {get;private set;}
    public VrFrame Apply(VrFrame packet)
    {
        if(packet.BaseSequence==0){nodes.Clear();materials.Clear();}
        else if(packet.BaseSequence!=Sequence)throw new InvalidDataException("VR scene delta is missing its baseline; reconnect the editor.");
        foreach(long id in packet.RemovedNodes)nodes.Remove(id);
        foreach(var node in packet.Nodes)nodes[node.Id]=node;
        foreach(var material in packet.Materials)materials[material.Id]=material;
        var used=nodes.Values.Select(n=>n.Material).ToHashSet();
        foreach(long id in materials.Keys.Where(id=>!used.Contains(id)).ToArray())materials.Remove(id);
        if(nodes.Values.Any(n=>!materials.ContainsKey(n.Material)))throw new InvalidDataException("VR scene material baseline is incomplete.");
        Sequence=packet.Sequence;
        return packet with{Nodes=nodes.Values.ToArray(),Materials=materials.Values.ToArray(),BaseSequence=0,RemovedNodes=[]};
    }
}
