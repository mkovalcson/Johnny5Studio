namespace Johnny5VR;

internal sealed partial class ViewerWindow
{
    private readonly bool effectsSmoke;
    private bool effectsSmokeReady;
    private int effectsSmokeFrame;
    private void ApplyEffectsSmoke()
    {
        if(!effectsSmokeReady)
        {
            scene=new UrdfReader().Load(new ViewerSettings{ConfigFolder=settings.ConfigFolder,Robot="Models/Robots/Johnny5FullBody/johnny5_head.urdf",Backdrop="",Distance=3.5f});
            renderer!.Upload(scene);ResetCamera();effectsSmokeReady=true;
        }
        var rig=scene!.Rig!;int phase=effectsSmokeFrame/5;
        var show=new[]{new EffectCue("HideLaser",0,"{\"hide\":false}"),new EffectCue("ToolboxSwap",0,"{\"swap\":false}")};
        EffectCue[] effects=phase switch{1=>show.Concat(new[]{new EffectCue("FireLaser",0,"{\"shots\":1,\"sound\":\"none\"}")}).ToArray(),2=>show.Concat(new[]{new EffectCue("FallApart",0,"{}")}).ToArray(),_=>show};
        double time=phase==1?.03:phase==2?rig.FallApart!.Duration:0;
        rig.SetEffects(effects,time);rig.Apply(new(){["servo:B_LaserLower"]=(100,0,"B_LaserLower")});
        renderer!.UpdatePose(rig.Palette);renderer.UpdateSecondary(rig,0);renderer.UpdateEffects(rig.Bolts);renderer.Apertures=rig.Apertures;
        yaw=.65f;pitch=.25f;orbitDistance=3.2f;
    }
    private void CaptureEffectsSmoke()
    {
        if(effectsSmokeFrame%5==4)
        {
            string name=new[]{"effects-whole.png","effects-fire.png","effects-apart.png","effects-restored.png"}[effectsSmokeFrame/5];
            renderer!.Screenshot(Path.Combine(Diagnostics.Folder,name));
        }
        effectsSmokeFrame++;if(effectsSmokeFrame<20)return;
        int Difference(string path)
        {
            using var a=new Bitmap(Path.Combine(Diagnostics.Folder,"effects-whole.png"));using var b=new Bitmap(Path.Combine(Diagnostics.Folder,path));int changed=0;
            for(int y=0;y<a.Height;y++)for(int x=0;x<a.Width;x++)if(a.GetPixel(x,y)!=b.GetPixel(x,y))changed++;return changed;
        }
        int fire=Difference("effects-fire.png"),apart=Difference("effects-apart.png"),restored=Difference("effects-restored.png");
        // CPU checks require exact restored transforms. GPU edge coverage has
        // a small rasterization tolerance after the intervening frames.
        if(fire<3||apart<500||restored>100)throw new InvalidOperationException($"Effect render comparison failed: fire {fire}, disassembly {apart}, restored {restored}.");
        File.WriteAllText(Path.Combine(Diagnostics.Folder,"effects-render-check.txt"),$"PASS: actual D3D11 laser changed {fire} pixels; disassembly changed {apart}; restoration changed {restored} pixels (within antialiasing tolerance).");Close();
    }
}
