using System.Numerics;
using System.Runtime.InteropServices;
using NAudio.Wave;

namespace Johnny5VR;

internal static class SecondaryChecks
{
    internal static void Run(ViewerSettings settings,Action<bool,string> check)
    {
        check(Marshal.SizeOf<Vertex>()==Vertex.Stride,"CPU/GPU vertex layout includes the mouth LED identifier.");
        string fixture=Path.Combine(Diagnostics.Folder,"voice-envelope.wav");
        using(var output=new WaveFileWriter(fixture,new WaveFormat(16000,16,2)))
            for(int i=0;i<8000;i++)
            {
                double t=i/16000.0,amplitude=t<.1?0:t<.2?.2:t<.3?.8:0;
                foreach(double scale in new[]{1.0,.25}){short sample=(short)(amplitude*scale*32767*Math.Sin(2*Math.PI*440*t));output.WriteByte((byte)sample);output.WriteByte((byte)(sample>>8));}
            }
        AudioEnvelope envelope;using(var reader=new AudioFileReader(fixture))envelope=AudioEnvelope.Read(reader);
        check(envelope.At(.05)==0&&Math.Abs(envelope.At(.15)-.2)<.01&&Math.Abs(envelope.At(.25)-.8)<.01,"Voice envelope follows quiet/loud speech and uses the louder stereo channel.");
        double now=0;using(var player=new PlaybackPlayer(()=>now))
        {
            var cue=new AudioCue(fixture,.5,.2,.1){Envelope=envelope};
            player.Load(new("voice",[new("","",2,[],[cue],false,"",[])],[]));player.Toggle();
            now=.1;check(player.MouthAmplitude==0,"Mouth remains off before a voice clip starts.");
            now=.525;check(player.MouthAmplitude>.75,"Trimmed voice uses its source position for mouth amplitude.");
            player.Toggle();check(player.MouthAmplitude==0,"Pausing playback extinguishes mouth LEDs.");
            player.Toggle();now=.8;check(player.MouthAmplitude==0,"Mouth remains off in audio gaps and after a trimmed clip ends.");
            player.Stop();check(player.MouthAmplitude==0,"Stopping playback extinguishes mouth LEDs.");
        }
        check(UrdfReader.MouthId("lip_led_06_dynamic")==7&&UrdfReader.MouthId("lip_led_07_dynamic")==8,"Front mouth lights identify the centre LED pair.");
        check(UrdfReader.MouthId("mouth_side_red_s11_c3_dynamic")==43&&UrdfReader.MouthId("mouth_side_green_s00_c0_dynamic")==64,"Side mouth lights retain red/green identity and station order.");
        MotionTrack Track(string servo,double start,double end,bool spline=false)=>new(servo,"",[new(0,start,false),new(1,end,false)],spline);
        PlaybackSequence Sequence(params MotionTrack[] tracks)=>new("","",2,tracks,[],false,"",[]);
        var forward=new SecondaryMotionPath(Sequence(Track("TrackForward",20,0)),new(),.5,.08);
        var travel=forward.At(1);check(Math.Abs(travel.Forward-.2)<1e-9&&Math.Abs(travel.X-.2)<1e-9&&Math.Abs(travel.Y)<1e-9,"Track Forward uses editor centimetres/second and integrates metres of travel.");
        var backwards=forward.At(.25);check(Math.Abs(backwards.Forward-.05)<1e-9,"Backward seek restores deterministic tread travel.");
        var reverse=new SecondaryMotionPath(Sequence(Track("TrackForward",-20,0)),new(),.5,.08).At(1);
        check(reverse.Forward<-.19&&Math.Abs(reverse.Caster)>45,"Reverse travel reverses belts and swings a trailing caster around.");
        check(forward.At(2)==forward.At(1),"Stationary treads and trailing caster retain their final positions.");
        var rig=new AnimationRig(settings.Resolve("Models/Robots/Johnny5FullBody/johnny5_head.urdf"),settings.ConfigFolder);
        check(rig.Treads is {Phases:12}&&rig.Treads.Layers.Count==4,"Full robot loads both metal and rubber tread layers and every frame.");
        check(rig.Treads!.Phase(rig.Treads.Step*2.5)==9&&rig.Treads.Phase(-rig.Treads.Step*2.5)==2,"Forward and reverse use opposite signed tread phases with correct wraparound.");
        check(rig.CasterTrail>.005&&rig.CasterRadius>.01&&rig.TreadGauge>.1,"Caster and track geometry come from the actual model.");
        var spinPath=new SecondaryMotionPath(Sequence(Track("URDFSpin",0,90,true)),new(),rig.CasterRadius,rig.CasterTrail);
        var spin=spinPath.At(1);rig.Apply(new(){["servo:URDFSpin"]=(90,1,"URDFSpin")},spin);
        check(rig.TreadDistances[0]<0&&rig.TreadDistances[1]>0,"Spinning reverses the inside belt and advances the outside belt.");
        check(Math.Abs(spin.Caster)>1,"Rear caster swivels during an in-place turn.");
        var caster=rig.Palette[rig.Bone("body_caster_yoke_link")];
        check(caster!=Matrix4x4.CreateRotationZ(MathF.PI/2),"Caster visual has its own steering transform in addition to the robot's heading.");
        var mirror=new SecondaryMotionPath(Sequence(Track("URDFSpin",0,-90,true)),new(),rig.CasterRadius,rig.CasterTrail).At(1);
        check(Math.Sign(mirror.Caster)==-Math.Sign(spin.Caster),"Caster steering reverses with the turn direction.");
        var scene=new UrdfReader().Load(new ViewerSettings{ConfigFolder=settings.ConfigFolder,Backdrop=""});
        check(scene.Treads.Length==4&&scene.Treads.All(t=>t.Frames.Length==12&&t.Frames.All(f=>f.Length>0)),"Renderer receives complete selectable belt meshes for both sides.");
        check(scene.Opaque.Any(v=>v.Mouth is 7 or 8)&&scene.Opaque.Any(v=>v.Mouth is >=32 and <44)&&scene.Opaque.Any(v=>v.Mouth>=64),"Actual front and side mouth meshes carry amplitude-driven LED identifiers.");
    }
}
