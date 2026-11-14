namespace Johnny5VR;

internal sealed partial class ViewerWindow
{
    private SecondaryMotionPath? secondaryPath;
    private AnimationRig? secondaryRig;
    private int secondaryEpoch=-1;
    private TravelState SecondaryMotion(AnimationRig rig)
    {
        if(player.Current==null)return default;
        if(secondaryPath==null||secondaryEpoch!=player.Epoch||secondaryRig!=rig)
        {
            secondaryEpoch=player.Epoch;secondaryRig=rig;
            secondaryPath=new(player.Current,player.Pose(),rig.CasterRadius,rig.CasterTrail);
        }
        return secondaryPath.At(Math.Min(player.Position,player.Current.Duration));
    }
}
