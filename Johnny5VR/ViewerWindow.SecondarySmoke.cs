namespace Johnny5VR;

internal sealed partial class ViewerWindow
{
    private void ApplySecondarySmoke()
    {
        if(smokeFrames==70)
        {
            player.Unload();
            scene=new UrdfReader().Load(new ViewerSettings{ConfigFolder=settings.ConfigFolder,Robot="Models/Robots/Johnny5FullBody/johnny5_head.urdf",Backdrop="",FloorGrid=false,Scale=settings.Scale,Distance=settings.Distance,Horizontal=settings.Horizontal,Heading=settings.Heading,RobotHeightMetres=settings.RobotHeightMetres});
            renderer!.Upload(scene);ResetCamera();
        }
        var rig=scene!.Rig!;int phase=Math.Clamp((smokeFrames-70)/5,0,11);
        if(phase is 4 or 5){yaw=2.6f;pitch=.2f;}else{yaw=.28f;pitch=.16f;}
        if(phase>=6)
        {
            target=(rig.RestPoint("left_eye_pop_link",new(.02495f,0,0))+rig.RestPoint("right_eye_pop_link",new(.02495f,0,0)))*.5f;
            orbitDistance=.65f*settings.Scale;
            if(phase>=8){yaw=1.3f;pitch=.3f;}
        }
        if(phase>=10)
        {
            target=rig.RestPoint("body_caster_wheel_l_link",System.Numerics.Vector3.Zero);
            orbitDistance=.55f*settings.Scale;yaw=2.6f;pitch=.18f;
        }
        var travel=new TravelState(0,0,phase==2?.012:phase==3?-.012:0,phase==5?90:0,0,0,phase==11?.035:0,phase==11?.026:0);
        Johnny5VR.Lighting.RgbRingFrame? glow=null;
        if(phase is 7 or 9)glow=new Johnny5VR.Lighting.ArduinoRgbTimelineSimulator().PreviewCommand("SetRGBColor,20,80,255,255,"+(phase==7?"Eyes":"Vents")+",LR");
        rig.Apply(new(),travel);renderer!.UpdatePose(rig.Palette);renderer.Apertures=rig.Apertures;renderer.UpdateSecondary(rig,phase==1?1:0,glow);
    }
    private void CaptureSecondarySmoke()
    {
        string? file=smokeFrames switch{74=>"mouth-off.png",79=>"mouth-on.png",84=>"treads-forward.png",89=>"treads-reverse.png",94=>"caster-straight.png",99=>"caster-turned.png",104=>"eyes-off.png",109=>"eyes-glowing.png",114=>"vents-off.png",119=>"vents-glowing.png",124=>"caster-roll-start.png",129=>"caster-roll-end.png",_=>null};
        if(file==null)return;renderer!.Screenshot(Path.Combine(Diagnostics.Folder,file));
        if(smokeFrames!=129)return;
        using(var resting=new Bitmap(Path.Combine(Diagnostics.Folder,"mouth-off.png")))
        {
            var floor=resting.GetPixel(10,resting.Height-10);
            if(floor.B<=floor.R||floor.B<=floor.G)throw new InvalidOperationException("Non-LED surfaces were incorrectly colored by the mouth shader.");
        }
        int Difference(string first,string second)
        {
            using var a=new Bitmap(Path.Combine(Diagnostics.Folder,first));using var b=new Bitmap(Path.Combine(Diagnostics.Folder,second));int changed=0;
            for(int y=0;y<a.Height;y++)for(int x=0;x<a.Width;x++)if(a.GetPixel(x,y)!=b.GetPixel(x,y))changed++;
            return changed;
        }
        int mouth=Difference("mouth-off.png","mouth-on.png"),treads=Difference("treads-forward.png","treads-reverse.png"),caster=Difference("caster-straight.png","caster-turned.png");
        int eyes=Difference("eyes-off.png","eyes-glowing.png"),vents=Difference("vents-off.png","vents-glowing.png");
        int rolling=Difference("caster-roll-start.png","caster-roll-end.png");
        if(rolling<20)throw new InvalidOperationException($"Caster wheel rolling did not change enough pixels: {rolling}.");
        if(eyes<5||vents<5)throw new InvalidOperationException($"RGB glow did not change enough pixels: eyes {eyes}, vents {vents}.");
        if(mouth<3||treads<20||caster<10)throw new InvalidOperationException($"Secondary GPU animation did not change enough pixels: mouth {mouth}, treads {treads}, caster {caster}.");
        File.WriteAllText(Path.Combine(Diagnostics.Folder,"secondary-render-check.txt"),$"PASS: isolated D3D11 comparisons changed {mouth} mouth pixels, {treads} tread/roller pixels, {caster} caster swivel pixels, {rolling} caster rolling pixels, {eyes} eye-light pixels and {vents} vent-light pixels.");
    }
}
