using Johnny5.VrBridge;
namespace Johnny5VR;

internal sealed partial class ViewerWindow
{
    private EffectCue[]? previewEffects;
    private readonly System.Diagnostics.Stopwatch effectClock=new();
    private readonly EffectAudioPlayer effectAudio=new();
    private void BuildEffectsMenu()
    {
        if(vr?.Menu is not {} menu)return;
        menu.GoBack=BuildPlaybackMenu;
        menu.Set("Robot effects",[
            new("Preview laser burst (show laser)",()=>PreviewEffect(VrEditorCommand.PreviewFireLaser)),
            new("Preview disassemble",()=>PreviewEffect(VrEditorCommand.PreviewDisassemble)),
            new("Preview reassemble",()=>PreviewEffect(VrEditorCommand.PreviewReassemble)),
            new("Clear effect preview",()=>PreviewEffect(VrEditorCommand.ClearEffects)),
            new("Back",BuildVrHome)],"Pause playback first. These previews do not change your timeline. Existing Fire Laser, Hide Laser, Toolbox Swap and FallApart commands also play from movies and sequences. Disassembly requires the full robot's baked animation clip.");
    }
    private void PreviewEffect(VrEditorCommand command)
    {
        if(editorMode){editor?.Command(command);vr?.Menu?.Navigation.Close();return;}
        if(player.Running){vr?.Menu?.Detail("Pause playback before previewing an effect.");return;}
        if(command==VrEditorCommand.ClearEffects){previewEffects=null;effectClock.Reset();}
        else
        {
            if(command!=VrEditorCommand.PreviewFireLaser&&scene?.Rig?.FallApart==null){vr?.Menu?.Detail("This robot has no baked disassembly clip. Choose the full robot model.");return;}
            previewEffects=command==VrEditorCommand.PreviewFireLaser?
                [new("HideLaser",0,"{\"hide\":false}"),new("ToolboxSwap",0,"{\"swap\":false}"),new("FireLaser",0,"{}")]:
                [new("FallApart",0,command==VrEditorCommand.PreviewReassemble?"{\"reverse\":true}":"{}")];
            effectClock.Restart();
        }
        vr?.Menu?.Navigation.Close();
    }
    private void UpdatePlaybackEffects(AnimationRig rig)
    {
        if(player.Running)previewEffects=null;
        if(previewEffects!=null)
        {
            if(previewEffects.Any(c=>c.Servo=="FireLaser")&&effectClock.Elapsed.TotalSeconds>LaserSettings.Read("{}").Duration)previewEffects=null;
        }
        rig.SetEffects(previewEffects??player.Effects,previewEffects!=null?effectClock.Elapsed.TotalSeconds:Math.Min(player.Position,player.Current?.Duration??0));
    }
    private void UpdateEffectAudio(AnimationRig rig)=>effectAudio.Step(previewEffects??player.Effects,previewEffects!=null?effectClock.Elapsed.TotalSeconds:player.Position,previewEffects!=null||player.Running,rig.LaserVisible,settings.ConfigFolder);
}
