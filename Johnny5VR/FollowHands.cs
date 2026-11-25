using System.Numerics;
using ServoAnimator;

namespace Johnny5VR;

internal sealed class FollowHands
{
    private enum Preset { Default, Curl, Point, ReleasedPoint, Fist }
    private const float PresetThreshold=.75f;
    private const float FullStickCurlPerSecond=1.5f;
    private static readonly FollowNeutralControl[][] Controls =
    [
        FollowNeutralCatalog.Controls.Where(c=>c.Section=="Left hand").ToArray(),
        FollowNeutralCatalog.Controls.Where(c=>c.Section=="Right hand").ToArray()
    ];

    private sealed class Hand
    {
        internal Preset Pose;
        internal float Curl, UpperCurl;
        internal readonly Dictionary<string,float> StartingTargets=new();

        internal void Update(Vector2 stick,float seconds)
        {
            if(Pose is Preset.Point or Preset.Fist)
            {
                if(stick.Y < -PresetThreshold)Pose=Pose==Preset.Point?Preset.ReleasedPoint:Preset.Curl;
                return;
            }
            if(Math.Abs(stick.X)>PresetThreshold)
            {
                StartingTargets.Clear();
                Pose=stick.X<0?Preset.Point:Preset.Fist;
                Curl=1;
                UpperCurl=Pose==Preset.Point?0:1;
                return;
            }
            float speed=Locomotion.DeadZone(stick.Y);
            if(speed==0)return;
            StartingTargets.Clear();
            Pose=Preset.Curl;
            float delta=speed*FullStickCurlPerSecond*Math.Clamp(seconds,0,.05f);
            Curl=Math.Clamp(Curl-delta,0,1);
            UpperCurl=Math.Clamp(UpperCurl-delta,0,1);
        }
    }

    private readonly Hand[] hands=[new(),new()];
    internal bool Tracking {get;private set;}=true;
    internal bool Manual {get;private set;}
    internal bool OverrideFingers=>Manual||!Tracking;

    internal void Reset()
    {
        Tracking=true;
        Manual=false;
        ResetPoses();
    }

    internal void ToggleTracking()
    {
        Tracking=!Tracking;
        if(!Tracking)ResetPoses();
    }

    internal void ToggleMode(IReadOnlyDictionary<string,float>? current=null,bool mirrored=false)
    {
        Manual=!Manual;
        if(!Manual||!Tracking||current==null)return;
        ResetPoses();
        for(int i=0;i<hands.Length;i++)
        {
            var hand=hands[i];
            foreach(var control in Controls[mirrored?1-i:i])
            {
                if(!current.TryGetValue(control.Key,out float value))continue;
                string suffix=control.Key.EndsWith("Left",StringComparison.Ordinal)?"Left":"Right";
                string joint=control.Key[..^suffix.Length];
                hand.StartingTargets[joint]=value;
                if(joint=="B_FingerCurlLower")hand.Curl=control.ToSlider(value);
                if(joint=="B_FingerCurlUpper")hand.UpperCurl=control.ToSlider(value);
            }
        }
    }

    private void ResetPoses()
    {
        foreach(var hand in hands){hand.Pose=Preset.Default;hand.Curl=hand.UpperCurl=0;hand.StartingTargets.Clear();}
    }

    internal void Apply(ControllerReading input,bool moveHands,bool mirrored,float seconds,
        Dictionary<string,float> targets,Func<FollowNeutralControl,float> neutral)
    {
        if(!OverrideFingers)return;
        if(Manual&&moveHands)
        {
            hands[0].Update(input.Move,seconds);
            hands[1].Update(input.Right,seconds);
        }
        for(int i=0;i<hands.Length;i++)
        {
            var hand=hands[i];
            foreach(var control in Controls[mirrored?1-i:i])
            {
                bool spread=control.Key.StartsWith("B_FingerSpread",StringComparison.Ordinal);
                bool upper=control.Key.Contains("Upper",StringComparison.Ordinal);
                bool straight=upper&&(hand.Pose is Preset.Point or Preset.ReleasedPoint);
                float curl=upper?hand.UpperCurl:hand.Curl;
                int suffixLength=control.Key.EndsWith("Left",StringComparison.Ordinal)?4:5;
                targets[control.Key]=hand.Pose==Preset.Default?hand.StartingTargets.GetValueOrDefault(control.Key[..^suffixLength],neutral(control))
                    :straight?0
                    :spread?.6f-.8f*curl
                    :control.Minimum+(control.Maximum-control.Minimum)*curl;
            }
        }
    }
}
