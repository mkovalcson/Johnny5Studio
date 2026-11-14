using Johnny5.VrBridge;
namespace ServoAnimator;

public partial class MainWindow
{
    private VrEditorCommand? _vrEffect;
    private readonly System.Diagnostics.Stopwatch _vrEffectClock=new();
    private readonly ServoCommand _vrFirePreview=new(){Servo=ServoNames.FireLaser,TextValue=new FireLaserCommand().ToJson()};
    private void PreviewVrEffect(VrEditorCommand command)
    {
        if(IsRunning&&!(command==VrEditorCommand.PreviewFireLaser&&_vrFollowing)){ShowStatus("Pause playback before previewing an effect.");return;}
        if(command==VrEditorCommand.PreviewFireLaser&&_vrFollowing)
        {
            var source=VrEditorMode.Source??EmbeddedHeadView;var range=ServoCommand.RangeFor(ServoNames.B_LaserLower);
            if(source.VrAccessory!=1||source.BodyPosition(ServoNames.B_LaserLower)<range.Max-Math.Max(.1,(range.Max-range.Min)*.02))return;
        }
        if(command==VrEditorCommand.ClearEffects){ClearVrEffect();return;}
        _vrEffect=command;_vrEffectClock.Restart();ApplyVrEffectPreview();
    }
    private void ClearVrEffect()
    {
        if(_vrEffect==null)return;_vrEffect=null;_vrEffectClock.Reset();
        ForEachHeadView(v=>v.SetLaserShots(Array.Empty<LaserShot>()));SyncFireSound(null,0,false);
        ApplyLaserTimeline();ApplyFallApartTimeline();
    }
    private void ApplyVrEffectPreview()
    {
        if(_vrEffect==null)return;if(IsRunning&&!(_vrEffect==VrEditorCommand.PreviewFireLaser&&_vrFollowing)){ClearVrEffect();return;}
        double time=_vrEffectClock.Elapsed.TotalSeconds;
        if(_vrEffect==VrEditorCommand.PreviewFireLaser)
        {
            var shots=FireLaserTimeline.ShotsAt(new[]{_vrFirePreview},time);
            ForEachHeadView(v=>{v.SetToolboxShown(false);v.SetLaserHidden(false);v.SetLaserShots(shots);});
            SyncFireSound(shots.OrderBy(s=>s.Start).Cast<LaserShot?>().LastOrDefault(),time,true);
            if(time>FireLaserCommand.Read(_vrFirePreview.TextValue).Duration)ClearVrEffect();
        }
        else
        {
            ForEachHeadView(v=>
            {
                if(v.FallApartClip is not {} clip)return;
                double frame=Math.Clamp(time*clip.Fps,0,clip.FrameCount-1);
                if(_vrEffect==VrEditorCommand.PreviewReassemble)frame=clip.FrameCount-1-frame;
                v.SetTimelineFallApart(frame<=0&&_vrEffect==VrEditorCommand.PreviewReassemble?null:frame);
            });
        }
    }
}
