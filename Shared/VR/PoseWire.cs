#nullable enable
using System.Numerics;
using System.IO;
namespace Johnny5.VrBridge;

public sealed record VrPoseHandle(string Key,string Name,Vector3 Anchor,float Minimum,float Maximum,float Value,bool Ganged);
public sealed record VrTravelExtent(string Key,double Minimum,double Maximum,double SliderMinimum,double SliderMaximum,string Unit);
internal sealed record VrTravelExtentEdit(string Key,double OriginalMinimum,double OriginalMaximum,double Minimum,double Maximum);
public sealed record VrPoseState(string Token,double Cursor,bool Editable,int Changed,string Message,string LastRequest,VrPoseHandle[] Handles)
{ public VrTravelExtent[] TravelExtents {get;init;}=[]; public string TravelExtentReceipt {get;init;}=""; public string TravelExtentMessage {get;init;}=""; public int Appearance {get;init;} public int Accessory {get;init;} public bool AppearanceBusy {get;init;} public bool Recording {get;init;} public bool Replaying {get;init;} public bool HasTake {get;init;} public string RecordingMessage {get;init;}=""; public double RecordedSeconds {get;init;} }
internal enum VrPoseAction : byte { Set=1,Insert=2,Discard=3,Appearance=4,Accessory=5,ReadTravelExtents=6,SaveTravelExtent=7 }
internal sealed record VrPoseRequest(string Id,string Token,VrPoseAction Action,string Key="",float Value=0);
internal sealed record VrMotionBatch(string Id,string Token,float Seconds,float MaximumDegreesPerSecond,Dictionary<string,float> Targets)
{ internal Dictionary<string,float> Speeds {get;init;}=[]; }
[Flags]
internal enum VrRecordingTracks { LeftArm=1,RightArm=2,LeftHand=4,RightHand=8,Torso=16,Neck=32,Travel=64,Body=LeftArm|RightArm|LeftHand|RightHand|Torso|Travel,All=Body|Neck }
internal enum VrRecordingAction:byte { Record=1,Overdub=2,Stop=3,Keep=4,Discard=5,Replay=6 }
internal sealed record VrRecordingRequest(string Id,string Token,VrRecordingAction Action,VrRecordingTracks Tracks);
internal static class VrRecordingSelection
{
    internal static bool Owns(VrRecordingTracks tracks,string key)
    {
        bool Has(VrRecordingTracks part)=>(tracks&part)!=0;
        if(key is "NeckTurn" or "NeckNodUp" or "NeckTiltRight")return Has(VrRecordingTracks.Neck);
        // Crouching drives the neck lift before the chest and waist, so it records with them.
        if(key is "B_NeckLift" or "B_ChestTilt" or "B_WaistLean" or "B_WaistSwivel")return Has(VrRecordingTracks.Torso);
        if(key is "URDFStageX" or "URDFStageY" or "URDFStageHeading")return Has(VrRecordingTracks.Travel);
        bool left=key.EndsWith("Left",StringComparison.Ordinal),right=key.EndsWith("Right",StringComparison.Ordinal);
        if(!left&&!right)return false;
        if(key.StartsWith("B_Finger",StringComparison.Ordinal)||key.StartsWith("B_Thumb",StringComparison.Ordinal))return Has(left?VrRecordingTracks.LeftHand:VrRecordingTracks.RightHand);
        if(new[]{"B_ShoulderRotate","B_ShoulderHug","B_ArmTwist","B_Elbow","B_WristPitch","B_WristRoll"}.Any(key.StartsWith))return Has(left?VrRecordingTracks.LeftArm:VrRecordingTracks.RightArm);
        return false;
    }
}
public static class PoseWire
{
    public static void Write(BinaryWriter w,VrPoseState? state)
    {
        w.Write(state!=null);if(state==null)return;
        w.Write(state.Token);w.Write(state.Cursor);w.Write(state.Editable);w.Write(state.Changed);w.Write(state.Message);w.Write(state.LastRequest);
        w.Write(state.Handles.Length);foreach(var h in state.Handles)
        {w.Write(h.Key);w.Write(h.Name);w.Write(h.Anchor.X);w.Write(h.Anchor.Y);w.Write(h.Anchor.Z);w.Write(h.Minimum);w.Write(h.Maximum);w.Write(h.Value);w.Write(h.Ganged);}
        w.Write(state.Appearance);w.Write(state.Accessory);w.Write(state.AppearanceBusy);w.Write(state.Recording);w.Write(state.HasTake);w.Write(state.RecordingMessage);w.Write(state.RecordedSeconds);w.Write(state.Replaying);
        w.Write(state.TravelExtentReceipt);w.Write(state.TravelExtentMessage);w.Write(state.TravelExtents.Length);
        foreach(var e in state.TravelExtents){w.Write(e.Key);w.Write(e.Minimum);w.Write(e.Maximum);w.Write(e.SliderMinimum);w.Write(e.SliderMaximum);w.Write(e.Unit);}
    }
    public static VrPoseState? Read(BinaryReader r)
    {
        if(!r.ReadBoolean())return null;
        string token=r.ReadString();double cursor=r.ReadDouble();bool editable=r.ReadBoolean();int changed=SceneWire.Count(r,256);string message=r.ReadString(),last=r.ReadString();
        if(!double.IsFinite(cursor))throw new InvalidDataException("Invalid pose cursor.");
        var handles=new VrPoseHandle[SceneWire.Count(r,256)];
        for(int i=0;i<handles.Length;i++)
        {
            string key=r.ReadString(),name=r.ReadString();var p=new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());float min=r.ReadSingle(),max=r.ReadSingle(),value=r.ReadSingle();bool gang=r.ReadBoolean();
            if(!float.IsFinite(p.X+p.Y+p.Z+min+max+value)||min>=max||value<min||value>max)throw new InvalidDataException("Invalid pose handle.");
            handles[i]=new(key,name,p,min,max,value,gang);
        }
        int appearance=SceneWire.Count(r,4),accessory=SceneWire.Count(r,2);bool busy=r.ReadBoolean();
        bool recording=r.ReadBoolean(),hasTake=r.ReadBoolean();string recordingMessage=r.ReadString();double recorded=r.ReadDouble();
        if(!double.IsFinite(recorded)||recorded<0)throw new InvalidDataException("Invalid recording duration.");
        bool replaying=r.ReadBoolean();string receipt=r.ReadString(),rangeMessage=r.ReadString();
        var extents=new VrTravelExtent[SceneWire.Count(r,128)];
        for(int i=0;i<extents.Length;i++)
        {
            var e=new VrTravelExtent(r.ReadString(),r.ReadDouble(),r.ReadDouble(),r.ReadDouble(),r.ReadDouble(),r.ReadString());
            if(!double.IsFinite(e.Minimum+e.Maximum+e.SliderMinimum+e.SliderMaximum)||e.Minimum>e.Maximum||e.SliderMinimum>=e.SliderMaximum)
                throw new InvalidDataException("Invalid URDF travel extent.");
            extents[i]=e;
        }
        return new(token,cursor,editable,changed,message,last,handles){Appearance=appearance,Accessory=accessory,AppearanceBusy=busy,Recording=recording,HasTake=hasTake,RecordingMessage=recordingMessage,RecordedSeconds=recorded,Replaying=replaying,TravelExtents=extents,TravelExtentReceipt=receipt,TravelExtentMessage=rangeMessage};
    }
}

internal static class VrMotionWire
{
    internal static void Write(BinaryWriter w,VrMotionBatch batch)
    {
        w.Write(batch.Id);w.Write(batch.Token);w.Write(batch.Seconds);w.Write(batch.MaximumDegreesPerSecond);
        w.Write(batch.Targets.Count);foreach(var item in batch.Targets){w.Write(item.Key);w.Write(item.Value);}
        w.Write(batch.Speeds.Count);foreach(var item in batch.Speeds){w.Write(item.Key);w.Write(item.Value);}
    }
    internal static VrMotionBatch Read(BinaryReader r)
    {
        string id=r.ReadString(),token=r.ReadString();float seconds=r.ReadSingle(),speed=r.ReadSingle();
        Dictionary<string,float> Values(int maximum)
        {
            var values=new Dictionary<string,float>();int count=SceneWire.Count(r,maximum);
            for(int i=0;i<count;i++)if(!values.TryAdd(r.ReadString(),r.ReadSingle()))throw new InvalidDataException("Duplicate motion key.");
            return values;
        }
        var targets=Values(128);var speeds=Values(64);
        return new(id,token,seconds,speed,targets){Speeds=speeds};
    }
}
