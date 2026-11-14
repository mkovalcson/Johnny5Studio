using System.Runtime.InteropServices;
using Valve.VR;
namespace Johnny5VR;
internal sealed class TrackedFingerInput
{
    private ulong left,right;
    private bool ready;
    private long retry;
    internal (float[]? Left,float[]? Right,string Source,bool? Trigger) Read(CVRInput? input)
    {
        if(input==null)return(null,null,"Finger input waiting for shared controller actions",null);
        try
        {
            if(!ready)
            {
                if(Environment.TickCount64<retry)return(null,null,"Finger data unavailable",null);
                retry=Environment.TickCount64+3000;
                Check(input.GetActionHandle("/actions/navigation/in/hand_left",ref left));Check(input.GetActionHandle("/actions/navigation/in/hand_right",ref right));ready=true;
            }
            float[]? Hand(ulong action)
            {
                var state=new InputSkeletalActionData_t();
                if(input.GetSkeletalActionData(action,ref state,(uint)Marshal.SizeOf<InputSkeletalActionData_t>())!=EVRInputError.None||!state.bActive)return null;
                var level=EVRSkeletalTrackingLevel.VRSkeletalTracking_Estimated;
                if(input.GetSkeletalTrackingLevel(action,ref level)==EVRInputError.None&&level==EVRSkeletalTrackingLevel.VRSkeletalTracking_Estimated)return null;
                var summary=new VRSkeletalSummaryData_t();
                if(input.GetSkeletalSummaryData(action,EVRSummaryType.FromDevice,ref summary)!=EVRInputError.None)return null;
                float[] values=[summary.flFingerCurl0,summary.flFingerCurl1,summary.flFingerCurl2,summary.flFingerCurl3,summary.flFingerCurl4];
                return values.All(float.IsFinite)?values.Select(v=>Math.Clamp(v,0,1)).ToArray():null;
            }
            var l=Hand(left);var r=Hand(right);
            return(l,r,l!=null&&r!=null?"SteamVR skeletal finger curls":"Finger data unavailable — arm tracking remains active",null);
        }
        catch{ready=false;return(null,null,"Finger data unavailable — finger following disabled",null);}
    }
    private static void Check(EVRInputError error){if(error!=EVRInputError.None)throw new InvalidOperationException(error.ToString());}
}
