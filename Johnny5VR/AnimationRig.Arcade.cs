using System.Numerics;
namespace Johnny5VR;
internal sealed partial class AnimationRig
{
    internal float? GameLaserElevation;
    internal bool HasGameLaser=>indices.ContainsKey("body_laser_head_link")&&supportedJoints.Contains("LaserLower");
    internal (Vector3 Muzzle,Vector3 Aim) GameLaserRay()
    {
        if(!lastWorlds.TryGetValue("body_laser_head_link",out var laser))throw new InvalidOperationException("Choose the full-body Johnny 5 robot with the shoulder laser.");
        var transform=laser*lastScene*placement;
        return(Vector3.Transform(new(.3355f,0,.0545f),transform),Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitX,transform)));
    }
    internal float GameScale=>Vector3.TransformNormal(Vector3.UnitX,placement).Length();
    internal Vector3 GameBase=>Vector3.Transform(Vector3.Zero,placement);
    internal void ApplyGame(ArcadeGame game,Dictionary<string,(double Value,double Stamp,string Servo)>? transition=null)
    {
        GameLaserElevation=game.Elevation;
        var effects=new List<EffectCue>{new("HideLaser",0,"{\"hide\":false}"),new("ToolboxSwap",0,"{\"swap\":false}")};
        if(game.Phase is ArcadePhase.Disassembling or ArcadePhase.GameOver or ArcadePhase.Reassembling)effects.Add(new("FallApart",0,game.Phase==ArcadePhase.Reassembling?"{\"reverse\":true}":"{}"));
        SetEffects(effects.ToArray(),game.FallTime);
        var pose=transition==null?new Dictionary<string,(double Value,double Stamp,string Servo)>():transition.ToDictionary(p=>p.Key,p=>(p.Value.Value*(1-Math.Clamp(game.PhaseAge/5,0,1)),p.Value.Stamp,p.Value.Servo));
        pose["servo:URDFStageX"]=(game.X*100,0,"URDFStageX");pose["servo:URDFStageY"]=(game.Y*100,0,"URDFStageY");pose["servo:URDFStageHeading"]=(game.Heading,0,"URDFStageHeading");Apply(pose,game.Travel);
    }
}
