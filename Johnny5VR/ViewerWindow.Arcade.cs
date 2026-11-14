using System.Numerics;
namespace Johnny5VR;
internal sealed partial class ViewerWindow
{
    private ArcadeLandscape? arcadeLandscape;
    private ArcadeGame? arcade;private ArcadeGeometry? arcadeGeometry;private ArcadeHud? arcadeHud;
    private readonly ArcadeAudio arcadeAudio=new();
    private ArcadeEventAudio? arcadeEvents;
    private PlaybackPlayer? arcadeIntro;
    private SecondaryMotionPath? arcadeIntroTravel;
    private Dictionary<string,(double Value,double Stamp,string Servo)>? arcadeLastIntroPose;
    private bool arcadeIntroStarted,arcadeSkipReady,arcadeGroundPending,arcadeFirstPadPending,arcadePreparing;
    private int arcadeStartVersion;
    private Matrix4x4? arcadeSavedView;

    private const string ArcadeHelp="Left stick: drive/turn. Right stick Y: laser aim. Trigger: fire / skip instructions. TAP A: pause/menu. HOLD A: point the right controller's arc at a marked lookout; release A to teleport. Only green landings are accepted. Three hits cost a life; three lives. Reassemble in the safe bay with a shield. Game setup selects instruction sequence and audio.";
    private void BuildArcadeMenu()
    {
        if(vr?.Menu is not {} menu){status.Text="Start SteamVR, then open Laser Range from the VR menu.";return;}
        menu.GoBack=arcade==null?BuildVrHome:()=>menu.Navigation.Close();
        menu.Set("Johnny 5 · Laser Range",arcade==null?
            [new("Start game",StartArcade),new("Game setup · Sequence and audio",BuildArcadeSettingsMenu)]:
            [new("Resume game",()=>menu.Navigation.Close()),new("Restart · Three waves",StartArcade),new("Game setup · Sequence and audio",BuildArcadeSettingsMenu),new("Exit game",()=>{EndArcade();BuildVrHome();})],
            (arcade==null?"":arcade.Status+"\n")+ArcadeHelp);
    }
    private void StartArcade()=>_=PrepareArcade();
    private async Task PrepareArcade()
    {
        if(arcadePreparing||vr==null||scene?.Rig is not {} rig||renderer==null)return;
        if(loading||loadingShow||preparingScene){vr.Menu?.Detail("Wait for scene loading to finish.");return;}
        if(!rig.HasGameLaser){vr.Menu?.Detail("Choose the full-body Johnny 5 model with its shoulder laser.");return;}
        var session=vr;int version=++arcadeStartVersion;arcadePreparing=true;
        var options=ArcadeSettings.Load(settings.ConfigFolder);string folder=settings.ConfigFolder;
        session.Menu?.Set("Preparing game resources…",[new("Cancel",()=>{arcadeStartVersion++;arcadePreparing=false;BuildArcadeMenu();})],"Preloading instruction movements, voice audio and event sounds. The game will start when ready.");
        try
        {
            var sounds=ArcadeEventAudio.Load(options,folder);
            PlaybackShow? introduction=null;
            if(options.Introduction.Length>0)
            {
                try{introduction=await Task.Run(()=>PlaybackLibrary.Load(folder,new(options.Introduction,"Game instructions",false,"")));introduction=introduction with{Sequences=introduction.Sequences.Select(q=>q with{Loop=false}).ToArray()};}
                catch(Exception ex){Diagnostics.Log(ex);status.Text="Instruction sequence unavailable; starting without it. "+ex.Message;}
            }
            var clips=await sounds;if(closing||vr!=session||scene?.Rig!=rig||version!=arcadeStartVersion)return;
            EndArcade();session.BodyMotion.Stop("Laser Range started",byUser:true);if(editorMode)UseStandalone();player.Unload();previewEffects=null;effectAudio.Dispose();
            var start=rig.GameBase;start.Y=0;
            var probe=new ArcadeGame(start,Vector3.UnitZ);rig.ApplyGame(probe);var ray=rig.GameLaserRay();
            arcadeLandscape=new(start,ray.Aim);var gameScene=scene! with{Opaque=scene!.Opaque.Where(v=>v.Bone>0).ToArray(),Glass=scene.Glass.Where(v=>v.Bone>0).ToArray()};renderer!.Upload(gameScene);
            arcadeEvents=new(clips);arcade=new(start,ray.Aim,arcadeLandscape.Obstacles,rig.GameScale){Sound=arcadeAudio.Play,CombatEnabled=true,AnimationSeconds=rig.FallApart?.Duration??4,Event=arcadeEvents.Play,Deployed=()=>arcadeFirstPadPending=true};
            arcadeGeometry=new();arcadeSavedView=session.ArcadeViewOrigin;
            arcadeIntro=introduction==null?null:new PlaybackPlayer();if(introduction!=null){arcadeIntro!.Load(introduction);arcadeIntroTravel=new(arcadeIntro.Current!,arcadeIntro.Pose(),rig.CasterRadius,rig.CasterTrail);}
            arcade.BeginPresentation(introduction!=null);arcadeGroundPending=introduction!=null;arcadeFirstPadPending=introduction==null;arcadeIntroStarted=arcadeSkipReady=false;arcadeLastIntroPose=null;
            session.ArcadeTeleport=new(arcadeLandscape.Pads,arcadeLandscape.Obstacles){CanTeleport=introduction==null};
            rig.ApplyGame(arcade);session.ArcadeActive=true;session.ArcadeFrame=ArcadeFrame;session.RobotPose.Enabled=false;
            try{arcadeHud=new(session.Adapter,start+arcade.Forward*3+Vector3.UnitY*3.2f);}catch(Exception ex){Diagnostics.Log(ex);}
            BuildArcadeMenu();session.Menu?.Navigation.Close();status.Text=rig.FallApart==null?"Combat ready; this robot has no baked disassembly clip. Use the full robot with its FallApart assets for that effect.":"Combat range ready · Three lives · Hold A for lookout teleport · Game setup selects audio";
        }
        catch(Exception ex){Diagnostics.Log(ex);session.Menu?.Detail("Could not start the game: "+ex.Message);status.Text=ex.Message;}
        finally{if(version==arcadeStartVersion)arcadePreparing=false;}
    }
    private void ArcadeFrame(ControllerReading input,bool allowed,float seconds,Matrix4x4 head,Matrix4x4 origin)
    {
        if(arcade==null||scene?.Rig is not {} rig||renderer==null)return;
        if(vr==null)return;
        if(arcadeGroundPending)
        {vr.TeleportArcade(head,arcade.Origin-arcade.Forward*5,arcade.Position);arcadeGroundPending=false;}
        bool intro=arcade.Phase==ArcadePhase.Introduction;
        if(intro&&arcadeIntro!=null)UpdateArcadeIntroduction(input,allowed,rig);
        arcade.Step(input,allowed,seconds,rig.CasterRadius,rig.CasterTrail,game=>{if(game.Phase!=ArcadePhase.Introduction)rig.ApplyGame(game,game.Phase==ArcadePhase.Deploying?arcadeLastIntroPose:null);return rig.GameLaserRay();});
        if(arcadeFirstPadPending&&allowed)
        {vr.TeleportArcade(head,arcadeLandscape!.Pads[0].Centre,arcade.Position);arcadeFirstPadPending=false;}
        origin=vr.ArcadeViewOrigin;vr.ArcadeLookAt=arcade.Position;var teleport=vr.ArcadeTeleport!;teleport.CanTeleport=arcade.Phase==ArcadePhase.Playing;
        renderer.UpdatePose(rig.Palette);renderer.Apertures=rig.Apertures;renderer.UpdateSecondary(rig,intro?arcadeIntro?.MouthAmplitude??0:arcadeEvents?.MouthAmplitude??0,intro?arcadeIntro?.RgbFrame:null);
        renderer.UpdateEffects((intro?rig.Bolts.AsEnumerable():arcade.Bolts()).Concat(teleport.Beams()).ToArray());
        var particles=ArcadeGeometry.Particles(arcade,rig.GameLaserRay()).Concat(ArcadeCombatGeometry.Build(arcade,teleport)).ToArray();renderer.UpdateArcade(ArcadeTargets(),particles);
        try{arcadeHud?.Draw(arcade,head,origin,vr.Menu?.Navigation.Visible!=true);}catch(Exception ex){Diagnostics.Log(ex);arcadeHud?.Dispose();arcadeHud=null;}
        playbackStatus.Text=arcade.Status;
    }
    private void UpdateArcadeIntroduction(ControllerReading input,bool allowed,AnimationRig rig)
    {
        if(arcadeIntro==null||arcade==null)return;
        if(!input.Select)arcadeSkipReady=true;
        if(!allowed){if(arcadeIntro.Running)arcadeIntro.Toggle();return;}
        if(input.Select&&arcadeSkipReady){FinishArcadeIntroduction();return;}
        if(!arcadeIntroStarted||!arcadeIntro.Running&&arcadeIntro.Position<(arcadeIntro.Current?.Duration??0)){arcadeIntro.Toggle();arcadeIntroStarted=true;}
        arcadeIntro.Tick();var pose=arcadeIntro.Pose();
        double Value(string name)=>pose.TryGetValue("servo:"+name,out var p)?p.Value:0;
        pose["servo:URDFStageX"]=(Value("URDFStageX")+200/rig.GameScale,0,"URDFStageX");
        var travel=arcadeIntroTravel?.At(arcadeIntro.Position)??default;
        rig.GameLaserElevation=null;rig.SetEffects(arcadeIntro.Effects,arcadeIntro.Position);rig.Apply(pose,travel);
        effectAudio.Step(arcadeIntro.Effects,arcadeIntro.Position,arcadeIntro.Running,rig.LaserVisible,settings.ConfigFolder);
        arcadeLastIntroPose=new(pose);arcadeLastIntroPose["servo:URDFStageX"]=(Value("URDFStageX")+travel.X*100,0,"URDFStageX");arcadeLastIntroPose["servo:URDFStageY"]=(Value("URDFStageY")+travel.Y*100,0,"URDFStageY");
        if(arcadeIntroStarted&&arcadeIntro.Position>=(arcadeIntro.Current?.Duration??0))FinishArcadeIntroduction();
    }
    private void FinishArcadeIntroduction()
    {
        if(arcade==null||arcadeIntro==null)return;
        double Value(string name)=>arcadeLastIntroPose?.GetValueOrDefault("servo:"+name).Value??0;
        arcadeIntro.Unload();effectAudio.Dispose();arcade.FinishIntroduction((float)Value("URDFStageX")/100,(float)Value("URDFStageY")/100,(float)Value("URDFStageHeading"));
    }

    private Vertex[]? arcadeCombined,arcadePrevious;
    private Vertex[] ArcadeTargets()
    {
        var targets=arcadeGeometry!.Targets(arcade!);if(!ReferenceEquals(targets,arcadePrevious)){arcadePrevious=targets;arcadeCombined=arcadeLandscape!.Vertices.Concat(targets).ToArray();}return arcadeCombined!;
    }
    private void EndArcade()
    {
        arcadeStartVersion++;arcadePreparing=false;
        if(arcade==null)return;
        arcadeIntro?.Dispose();arcadeIntro=null;arcadeIntroTravel=null;effectAudio.Dispose();arcadeEvents?.Dispose();arcadeEvents=null;
        if(vr!=null&&arcadeSavedView.HasValue)vr.RestoreArcadeView(arcadeSavedView.Value);arcadeSavedView=null;
        arcade=null;arcadeGeometry=null;arcadeLandscape=null;arcadeCombined=arcadePrevious=null;arcadeHud?.Dispose();arcadeHud=null;arcadeAudio.Dispose();
        if(vr!=null){vr.ArcadeActive=false;vr.ArcadeFrame=null;vr.ArcadeTeleport=null;}
        if(scene?.Rig is {} rig){rig.GameLaserElevation=null;rig.Reset();rig.Apply(new());renderer?.UpdatePose(rig.Palette);}
        if(scene!=null)renderer?.Upload(scene);
        renderer?.UpdateArcade([],[]);renderer?.UpdateEffects([]);playbackStatus.Text="Laser Range ended. Movies, sequences and Follow are available.";
    }
}
