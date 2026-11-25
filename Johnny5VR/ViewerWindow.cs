using System.Diagnostics;
using Microsoft.Win32;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Johnny5VR;

internal sealed partial class ViewerWindow:Form
{
    private static string ReleaseVersion => typeof(ViewerWindow).Assembly.GetName().Version!.ToString(3);
    private ViewerSettings settings;
    private readonly Panel viewport=new(){Dock=DockStyle.Fill,BackColor=StudioDesktopTheme.Input,TabStop=true};
    private readonly FlowLayoutPanel sidebar=new(){Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new(16)};
    private readonly Label status=new(){Dock=DockStyle.Bottom,Height=54,Padding=new(12,8,8,4),AutoEllipsis=true};
    private readonly TextBox controllerReadout=new(){Dock=DockStyle.Bottom,Height=65,Multiline=true,ReadOnly=true,BorderStyle=BorderStyle.None,
        BackColor=StudioDesktopTheme.Status,ForeColor=StudioDesktopTheme.Secondary,ScrollBars=ScrollBars.Vertical,
        Text="Controller input values will appear here when VR starts."};
    private readonly ComboBox inputMode=new(){Width=265,DropDownStyle=ComboBoxStyle.DropDownList};
    private readonly bool inputModeAtLaunch;
    private readonly SplitContainer workspace=new(){Dock=DockStyle.Fill,Width=1280,Height=900,Orientation=Orientation.Vertical,
        SplitterWidth=8,FixedPanel=FixedPanel.Panel1,Panel1MinSize=280,Panel2MinSize=300,SplitterDistance=380};
    private bool resizingMenu, menuLayoutReady;
    private readonly TextBox config=new(){Width=265};
    private Button? browseConfig;
    private readonly ComboBox robot=new(){Width=265,DropDownStyle=ComboBoxStyle.DropDownList};
    private readonly ComboBox backdrop=new(){Width=265,DropDownStyle=ComboBoxStyle.DropDownList};
    private readonly NumericUpDown scale=Number(.1m,3,1,.05m),distance=Number(.5m,15,2.5m,.25m),horizontal=Number(-15,15,0,.25m),heading=Number(-180,180,0,5);
    private readonly NumericUpDown robotFeet=Number(3,9,6,.1m),walkSpeed=Number(.2m,3,1.2m,.1m),snapAngle=Number(15,90,30,15);
    private readonly CheckBox locomotionEnabled=new(){Text="Index controller movement",AutoSize=true,Checked=true};
    private readonly CheckBox grid=new(){Text="Show floor grid",AutoSize=true,Checked=true};
    private readonly StudioDesktopButton apply=new(){Text="Apply scene",Width=130,Height=36};
    private readonly StudioDesktopButton save=new(){Text="Save settings",Width=130,Height=36};
    private readonly StudioDesktopButton start=new(){Text="Start SteamVR",Width=265,Height=42,Enabled=false};
    private readonly StudioDesktopButton stop=new(){Text="Stop VR",Width=265,Height=34,Enabled=false};
    private readonly Label performance=new(){AutoSize=true,MaximumSize=new(265,0)};
    private readonly Stopwatch clock=Stopwatch.StartNew();
    private Renderer? renderer;
    private SceneData? scene;
    private VrSession? vr;
    private bool loading,closing,rendering;
    private bool renderEnabled;
    private float yaw=.28f,pitch=.16f,orbitDistance=3.3f;
    private Vector3 target;
    private Point mouse;
    private int frames,smokeFrames;
    private double lastStats;
    private readonly bool smoke;
    private bool smokeReady;
    private readonly PlaybackPlayer player=new();
    private LibraryEntry[] library=[];
    private readonly ComboBox libraryType=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=110};
    private readonly ComboBox libraryPick=new(){DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill};
    private readonly Label playbackStatus=new(){Dock=DockStyle.Fill,AutoEllipsis=true,TextAlign=ContentAlignment.MiddleLeft};
    private bool loadingShow;
    private string playbackWarnings="";
    private long lastPlaybackStatus;
    private Button? playButton;
    private readonly CheckBox useSavedScene=new(){Text="Use editor robot and saved 3D scene for playback",AutoSize=true,Checked=true};
    private bool preparingScene;
    private string preparedSequence="";
    private ViewerSettings? playbackBase;
    private string[] sceneNotices=[];
    internal ViewerWindow(ViewerSettings initial,bool smoke=false,bool editorSmoke=false,bool effectsSmoke=false,bool openEditor=false,bool layoutCheckOnly=false)
    {
        SuspendLayout();
        this.editorSmoke=editorSmoke;
        this.effectsSmoke=effectsSmoke;
        this.smoke=smoke;settings=initial;inputModeAtLaunch=initial.UseSteamVrBindings;
        useSavedScene.Checked=initial.UseSavedPlaybackScene;
        Text=$"J5 Studio VR Player · {ReleaseVersion}";ClientSize=new(1280,900);MinimumSize=new(1000,680);StartPosition=FormStartPosition.CenterScreen;
        Font=new("Segoe UI",10);BackColor=StudioDesktopTheme.Background;ForeColor=StudioDesktopTheme.Text;KeyPreview=true;
        var sideHost=new Panel{Dock=DockStyle.Fill,BackColor=StudioDesktopTheme.Panel};
        var vrControls=new Panel{Dock=DockStyle.Bottom,Height=222,Padding=new(16,6,16,6)};
        var transport=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=7,Margin=Padding.Empty};
        foreach(int height in new[]{44,8,42,8,34,8,62})transport.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
        transport.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        start.Dock=DockStyle.Fill;stop.Dock=DockStyle.Fill;performance.Dock=DockStyle.Fill;performance.AutoSize=false;performance.MaximumSize=Size.Empty;
        start.Margin=stop.Margin=performance.Margin=Padding.Empty;
        transport.Controls.Add(start,0,2);transport.Controls.Add(stop,0,4);transport.Controls.Add(performance,0,6);vrControls.Controls.Add(transport);
        sideHost.Controls.Add(sidebar);sideHost.Controls.Add(vrControls);
        workspace.Panel1.Controls.Add(sideHost);workspace.Panel2.Controls.Add(viewport);workspace.BackColor=StudioDesktopTheme.Divider;
        BuildPlaybackControls();
        workspace.Panel1.BackColor=StudioDesktopTheme.Panel;workspace.Panel2.BackColor=StudioDesktopTheme.Panel;
        workspace.SplitterDistance=initial.MenuWidth;
        var statusHost=new Panel{BackColor=StudioDesktopTheme.Status,Dock=DockStyle.Bottom,Height=136,Padding=new(12,6,12,8)};
        var statusTitle=new Label{Text="Status and controller input",Dock=DockStyle.Top,Height=22,Font=new(Font,FontStyle.Bold)};
        status.Dock=DockStyle.Top;status.Height=38;status.Padding=Padding.Empty;controllerReadout.Dock=DockStyle.Fill;
        statusHost.Controls.Add(controllerReadout);statusHost.Controls.Add(status);statusHost.Controls.Add(statusTitle);
        Controls.Add(workspace);Controls.Add(statusHost);
        var identity=Johnny5.VrBridge.StudioBuildIdentity.Read(typeof(ViewerWindow).Assembly.Location);
        Text+=" · build "+identity.BuildId[..Math.Min(12,identity.BuildId.Length)];
        AddLabel("JOHNNY 5 VR",17,true);AddLabel("Standalone scene viewer",10);
        AddLabel("Shared Config folder",10,true);sidebar.Controls.Add(config);config.Text=settings.ConfigFolder;
        var browse=Button("Choose Config folder…",()=>
        {
            using var dialog=new FolderBrowserDialog{SelectedPath=config.Text,Description="Select the Animation Editor's Config folder"};
            if(dialog.ShowDialog(this)==DialogResult.OK){config.Text=dialog.SelectedPath;ScanAssets();ScanLibrary();}
        });sidebar.Controls.Add(browse);browseConfig=browse;
        sidebar.Controls.Add(Button("Open J5 Studio VR Editor",()=>_=OpenEditor()));
        sidebar.Controls.Add(Button("Standalone playback",UseStandalone));
        sidebar.Controls.Add(Button("Build information",()=>MessageBox.Show(this,
            identity.Description+Environment.NewLine+typeof(ViewerWindow).Assembly.Location,
            "J5 Studio VR Player build")));
        sidebar.Controls.Add(Button("Laser Range setup…",OpenArcadeSettings));
        sidebar.Controls.Add(Button("Laser Range · VR game",()=>{BuildArcadeMenu();vr?.Menu?.Navigation.Open();}));
        AddLabel("Robot",10,true);sidebar.Controls.Add(robot);AddLabel("3D backdrop",10,true);sidebar.Controls.Add(backdrop);
        sidebar.Controls.Add(useSavedScene);
        AddLabel("Placement",12,true);
        Field("Full robot height (feet)",robotFeet);Field("Scene scale",scale);Field("Distance in front (metres)",distance);Field("Left / right (metres)",horizontal);Field("Scene heading (degrees)",heading);
        robotFeet.Value=Math.Clamp((decimal)(settings.RobotHeightMetres/.3048f),3,9);
        scale.Value=(decimal)settings.Scale;distance.Value=(decimal)settings.Distance;horizontal.Value=(decimal)settings.Horizontal;heading.Value=Math.Clamp((decimal)settings.Heading,-180,180);
        grid.Checked=settings.FloorGrid;sidebar.Controls.Add(grid);
        AddLabel("Index controllers",11,true);sidebar.Controls.Add(locomotionEnabled);
        inputMode.Items.AddRange(["Direct Index input","SteamVR action bindings"]);inputMode.SelectedIndex=initial.UseSteamVrBindings?1:0;sidebar.Controls.Add(inputMode);
        inputMode.SelectedIndexChanged+=(_,_)=>{status.Text="Input mode changes require Save settings, then close and reopen the viewer.";};
        Field("Walk speed (metres/sec)",walkSpeed);Field("Snap turn (degrees)",snapAngle);
        walkSpeed.Value=(decimal)settings.MoveSpeed;snapAngle.Value=(decimal)settings.SnapTurnDegrees;locomotionEnabled.Checked=settings.ControllerLocomotion;
        locomotionEnabled.CheckedChanged+=(_,_)=>ConfigureNavigation();walkSpeed.ValueChanged+=(_,_)=>ConfigureNavigation();snapAngle.ValueChanged+=(_,_)=>ConfigureNavigation();
        sidebar.Controls.Add(Button("Reset VR position",()=>{vr?.ResetPosition();status.Text="VR movement reset to the starting position.";}));
        sidebar.Controls.Add(Button("Follow settings…",OpenFollowSettings));
        sidebar.Controls.Add(Button("Controller feedback…",OpenControllerFeedback));
        sidebar.Controls.Add(Button("Clean Output…",OpenCleanOutputSettings));
        InitializeCleanOutput();
        apply.Width=125;save.Width=125;
        var row=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=Padding.Empty};
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,8));row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        apply.Dock=save.Dock=DockStyle.Fill;apply.Margin=save.Margin=Padding.Empty;
        row.Controls.Add(apply,0,0);row.Controls.Add(save,2,0);transport.Controls.Add(row,0,0);
        sidebar.Controls.Add(Button("Reset desktop view",ResetCamera));
        AddLabel("Drag the vertical divider to resize this menu. Save settings remembers its width.",9);
        AddLabel("Desktop controls",11,true);AddLabel("Drag: orbit · Right-drag: pan\nWheel: zoom · WASD: move view\nR: reset view · Esc: stop VR",10);
        AddLabel("Index: left stick walks / strafes in your viewing direction. Right stick snaps left / right. Release the right stick between turns. Physical movement still works. Virtual walls do not block movement.",9);
        AddLabel("Robot and backdrop files are read only. Save settings writes only to Johnny5VR/Settings.",9);
        apply.Accent=start.Accent=true;
        StudioDesktopTheme.Apply(this);
        controllerReadout.BackColor=StudioDesktopTheme.Status;controllerReadout.ForeColor=StudioDesktopTheme.Secondary;
        sidebar.SizeChanged+=(_,_)=>ResizeMenuControls();
        workspace.SplitterMoved+=(_,_)=>ResizeMenuControls();
        apply.Click+=async (_,_)=>await LoadScene();save.Click+=(_,_)=>Save();start.Click+=(_,_)=>StartVr();stop.Click+=(_,_)=>StopVr();
        viewport.Resize+=(_,_)=>{if(renderer!=null&&viewport.Width>0&&viewport.Height>0&&!closing)try{renderer.Resize(viewport.Width,viewport.Height);}catch(Exception ex){Fail(ex);}};
        viewport.MouseDown+=(_,e)=>{mouse=e.Location;viewport.Focus();viewport.Capture=true;};
        viewport.MouseUp+=(_,_)=>viewport.Capture=false;
        viewport.MouseMove+=(_,e)=>
        {
            int dx=e.X-mouse.X,dy=e.Y-mouse.Y;mouse=e.Location;
            if(vr!=null)return;
            if(e.Button==MouseButtons.Left){yaw-=dx*.006f;pitch=Math.Clamp(pitch+dy*.006f,-1.35f,1.35f);}
            if(e.Button==MouseButtons.Right){var right=new Vector3(MathF.Cos(yaw),0,-MathF.Sin(yaw));target-=right*dx*orbitDistance*.0015f;target+=Vector3.UnitY*dy*orbitDistance*.0015f;}
        };
        viewport.MouseWheel+=(_,e)=>{if(vr==null)orbitDistance=Math.Clamp(orbitDistance*MathF.Exp(-e.Delta*.001f),.2f,40);};
        KeyDown+=(_,e)=>
        {
            if(e.KeyCode==Keys.Escape){StopVr();e.Handled=true;return;}
            if(ActiveControl is TextBoxBase or NumericUpDown or ComboBox)return;
            if(e.KeyCode==Keys.R)ResetCamera();
            if(vr!=null)return;
            var forward=new Vector3(-MathF.Sin(yaw),0,-MathF.Cos(yaw));var right=new Vector3(MathF.Cos(yaw),0,-MathF.Sin(yaw));
            switch(e.KeyCode){case Keys.W:target+=forward*.12f;break;case Keys.S:target-=forward*.12f;break;case Keys.A:target-=right*.12f;break;case Keys.D:target+=right*.12f;break;}
        };
        FormClosing+=(_,e)=>
        {
            if(editor!=null&&!editor.Process.HasExited){e.Cancel=true;waitingForEditorClose=true;editor.RequestClose();status.Text="Close the editor and resolve any save dialogs before the viewer exits.";return;}
            editor?.Dispose();
            closing=true;EndArcade();SystemEvents.DisplaySettingsChanged-=CleanDisplaysChanged;DisableCleanOutput(false);cleanSettingsWindow?.Close();controllerFeedback.Dispose();feedbackWindow?.Close();renderEnabled=false;Application.Idle-=RenderWhenIdle;player.Dispose();effectAudio.Dispose();vr?.Dispose();vr=null;renderer?.Dispose();renderer=null;
        };
        Application.Idle+=RenderWhenIdle;
        // All construction dimensions above are logical 96-DPI pixels. The manifest alone
        // does not scale hand-built WinForms controls or their absolute table rows.
        AutoScaleDimensions=new SizeF(96,96);
        AutoScaleMode=AutoScaleMode.Dpi;
        Load+=(_,_)=>{menuLayoutReady=true;ResizeMenuControls();};
        DpiChanged+=(_,_)=>BeginInvoke(new Action(ResizeMenuControls));
        ResumeLayout(true);
        Shown+=async (_,_)=>
        {
            if(layoutCheckOnly)return;
            try
            {
                renderer=new(viewport.Handle,viewport.Width,viewport.Height);ScanAssets();ScanLibrary();await LoadScene();
                if((smoke||editorSmoke)&&scene!=null){OverlayReliabilityChecks.Gpu();renderer.VerifyStaticFrameReuse();renderer.VerifyLiveSmoothing();renderer.Upload(scene);renderer.VerifyControllerPointerDrawing();}
                ApplyCleanOutput();
                if(editorSmoke||openEditor)await OpenEditor();
                if(smoke&&scene!=null)
                {
                    var demo=library.FirstOrDefault(e=>e.Movie)??throw new InvalidOperationException("Smoke test needs a movie in Config.");
                    await LoadShow(demo);
                    if(player.Show==null)throw new InvalidOperationException("Movie UI loading failed during smoke test.");
                    player.Toggle();smokeFrames=0;smokeReady=true;
                }
                if(scene!=null)viewport.Focus();
            }
            catch(Exception ex){Fail(ex);}
        };
    }
    private void BuildPlaybackControls()
    {
        var host=new TableLayoutPanel{Dock=DockStyle.Bottom,Height=125,Padding=new(10,6,10,6),ColumnCount=1,RowCount=3};
        host.ColumnStyles.Add(new(SizeType.Percent,100));
        host.RowStyles.Add(new(SizeType.Absolute,36));host.RowStyles.Add(new(SizeType.Absolute,38));host.RowStyles.Add(new(SizeType.Percent,100));
        var choose=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=Padding.Empty};
        choose.RowStyles.Add(new(SizeType.Percent,100));
        choose.ColumnStyles.Add(new(SizeType.Absolute,115));choose.ColumnStyles.Add(new(SizeType.Percent,100));choose.ColumnStyles.Add(new(SizeType.Absolute,80));
        libraryType.Items.AddRange(["Movies","Sequences"]);libraryType.SelectedIndex=0;libraryType.Dock=DockStyle.Fill;
        libraryType.SelectedIndexChanged+=(_,_)=>FillLibrary();
        var load=Button("Load",()=>{if(libraryPick.SelectedItem is LibraryEntry e)_=LoadShow(e);});load.Dock=DockStyle.Fill;
        choose.Controls.Add(libraryType,0,0);choose.Controls.Add(libraryPick,1,0);choose.Controls.Add(load,2,0);
        var buttons=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=4,RowCount=1,Margin=Padding.Empty};
        buttons.RowStyles.Add(new(SizeType.Percent,100));
        for(int i=0;i<4;i++)buttons.ColumnStyles.Add(new(SizeType.Percent,25));
        playButton=Button("Play / Pause",()=>PlaybackAction(player.Toggle));playButton.Width=120;
        var stopPlayback=Button("Stop",()=>PlaybackAction(()=>{player.Stop();scene?.Rig?.Reset();}));stopPlayback.Width=70;
        var next=Button("Next",()=>PlaybackAction(player.Next));next.Width=70;
        var refresh=Button("Refresh",ScanLibrary);
        int column=0;foreach(var button in new[]{playButton,stopPlayback,next,refresh}){button.Dock=DockStyle.Fill;button.Font=new(Font.FontFamily,9);buttons.Controls.Add(button,column++,0);}
        host.Controls.Add(choose,0,0);host.Controls.Add(buttons,0,1);host.Controls.Add(playbackStatus,0,2);workspace.Panel2.Controls.Add(host);
        playbackStatus.Text="Choose a movie or sequence. In VR, press A for the playback menu.";
    }
    private void ScanLibrary()
    {
        try
        {
            var errors=new List<string>();library=PlaybackLibrary.Scan(config.Text,errors);FillLibrary();
            if(errors.Count>0){Directory.CreateDirectory(Diagnostics.Folder);File.WriteAllLines(Path.Combine(Diagnostics.Folder,"library-scan.txt"),errors);status.Text=$"Library scanned; {errors.Count} unreadable files listed in Diagnostics/library-scan.txt.";}
            BuildVrHome();
        }
        catch(Exception ex){Fail(ex);}
    }
    private void FillLibrary()
    {
        var selected=(libraryPick.SelectedItem as LibraryEntry)?.File;libraryPick.Items.Clear();
        libraryPick.Items.AddRange(library.Where(e=>e.Movie==(libraryType.SelectedIndex==0)).Cast<object>().ToArray());
        libraryPick.SelectedItem=libraryPick.Items.Cast<LibraryEntry>().FirstOrDefault(e=>e.File==selected);
        if(libraryPick.SelectedIndex<0&&libraryPick.Items.Count>0)libraryPick.SelectedIndex=0;
    }
    private async Task LoadShow(LibraryEntry entry)
    {
        EndArcade();
        if(editorMode)UseStandalone();
        if(loadingShow||loading||preparingScene)return;loadingShow=true;player.Stop();playbackWarnings="";playbackStatus.Text="Loading "+entry.Name+"…";
        vr?.Menu?.Set("Loading…",[new("Close menu",()=>vr?.Menu?.Navigation.Close())],entry.Name);
        try
        {
            string folder=Path.GetFullPath(config.Text);
            if(!string.Equals(folder,Path.GetFullPath(settings.ConfigFolder),StringComparison.OrdinalIgnoreCase))
            {
                await LoadScene();
                if(!string.Equals(folder,Path.GetFullPath(settings.ConfigFolder),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Apply the selected Config folder before loading playback.");
            }
            var show=await Task.Run(()=>PlaybackLibrary.Load(folder,entry));if(closing)return;
            playbackBase=ReadControls();preparedSequence="";sceneNotices=[];
            player.Load(show);
            await PreparePlaybackScene();if(closing)return;
            var unsupported=scene?.Rig?.Unsupported(show)??[];
            var warnings=show.Warnings.Concat(sceneNotices).Concat(unsupported.Length>0?["Unsupported motion controls: "+string.Join(", ",unsupported)]:Array.Empty<string>()).ToArray();
            playbackWarnings=warnings.Length==0?"":"Some effects are unavailable; review Details before playing.";
            Directory.CreateDirectory(Diagnostics.Folder);File.WriteAllText(Path.Combine(Diagnostics.Folder,"playback-report.txt"),show.Name+"\n"+string.Join('\n',warnings));
            scene?.Rig?.Reset();playbackStatus.Text=player.Status+(warnings.Length>0?" · "+playbackWarnings:"");
            BuildPlaybackMenu();
            if(warnings.Length>0)
            {
                status.Text=$"Loaded with {warnings.Length} compatibility notices. See playback Details or Diagnostics/playback-report.txt.";
                vr?.Menu?.Set("Playback compatibility",[new("Continue to playback controls",BuildPlaybackMenu),new("Choose something else",()=>BrowseVr(entry.Movie))],string.Join("  ",warnings).Truncate(670));
                if(vr==null&&!smoke)MessageBox.Show(this,string.Join("\n",warnings),"Playback compatibility",MessageBoxButtons.OK,MessageBoxIcon.Information);
            }
        }
        catch(Exception ex){player.Unload();playbackStatus.Text="Playback could not load: "+ex.Message;BuildVrHome();if(vr!=null){status.Text=ex.Message;Diagnostics.Log(ex);vr.Menu?.Detail(ex.Message);}else Fail(ex);}
        finally{loadingShow=false;}
    }
    private async Task PreparePlaybackScene()
    {
        if(preparingScene||player.Current==null)return;
        preparedSequence=player.Current.File;
        if(!useSavedScene.Checked)return;
        bool resume=player.Running;if(resume)player.Toggle();preparingScene=true;apply.Enabled=save.Enabled=false;
        try
        {
            var basis=playbackBase??settings;var selection=PlaybackLibrary.Scene(basis.ConfigFolder,player.Current.SceneReference,basis.Robot,basis.Backdrop);
            sceneNotices=selection.Warnings;
            if(selection.Warnings.Length>0){playbackWarnings=string.Join(" ",selection.Warnings);status.Text=playbackWarnings;}
            string StageKey(ServoAnimator.UrdfStageSettings? stage)=>stage==null?"":System.Text.Json.JsonSerializer.Serialize(new{stage.DetectFloor,stage.FloorHeight,stage.Props});
            if(selection.Robot.Equals(settings.Robot,StringComparison.OrdinalIgnoreCase)&&selection.Backdrop.Equals(settings.Backdrop,StringComparison.OrdinalIgnoreCase)&&StageKey(selection.Stage)==StageKey(settings.PlaybackStage))return;
            var next=System.Text.Json.JsonSerializer.Deserialize<ViewerSettings>(System.Text.Json.JsonSerializer.Serialize(basis))!;
            next.Robot=selection.Robot;next.Backdrop=selection.Backdrop;next.PlaybackStage=selection.Stage;
            next.CleanOutputEnabled=settings.CleanOutputEnabled;next.CleanOutputDisplay=settings.CleanOutputDisplay;next.CleanOutputFullscreen=settings.CleanOutputFullscreen;next.CleanOutputWidth=settings.CleanOutputWidth;next.CleanOutputHeight=settings.CleanOutputHeight;next.CleanOutputOverlays=settings.CleanOutputOverlays;
            status.Text="Preparing saved playback scene…";
            var loaded=await Task.Run(()=>new UrdfReader().Load(next));if(closing)return;
            renderer!.Upload(loaded);scene=loaded;settings=next;ScanAssets();ResetCamera();
        }
        catch(Exception ex){player.Stop();resume=false;status.Text="Saved scene could not load: "+ex.Message;Diagnostics.Log(ex);throw;}
        finally{preparingScene=false;if(!closing){apply.Enabled=save.Enabled=true;if(resume)player.Toggle();}}
    }
    private void PlaybackAction(Action action)
    {
        if(arcade!=null){status.Text="Exit Laser Range before playing a movie or sequence.";return;}
        try{if(loadingShow||preparingScene)return;action();playbackStatus.Text=player.Status;if(playButton!=null)playButton.Text=player.TransportLabel;if(vr?.Menu?.LivePlaybackStatus==true)BuildPlaybackMenu();}
        catch(Exception ex){player.Stop();Fail(ex);}
    }
    private void BuildVrHome()
    {
        if(vr?.Menu is not {} menu)return;
        if(arcade!=null){BuildArcadeMenu();return;}
        menu.GoBack=()=>menu.Navigation.Close();
        menu.Set("J5 Studio · Home",[
            new("Library · Movies and sequences",BuildLibraryMenu),
            new("Playback · Transport and effects",BuildPlaybackMenu),
            new("Pose · Robot controls",()=>BeginInvoke(new Action(()=>_=OpenPoseMenu()))),
            new("Follow / Record · Body movements",()=>BeginInvoke(new Action(()=>_=OpenBodyMotion()))),
            new("Editor · Workspace",BuildVrEditorMenu),
            new("Game · Johnny 5 Laser Range",BuildArcadeMenu),
            new("Settings · Navigation and feedback",BuildVrSettingsMenu)],editorMode?"VR Editor connected. Choose a task. Back and Close are always below the page.":"Standalone player. Choose a task. Pose and Follow open the VR Editor when needed.");
    }
    private void BrowseVr(bool movies)
    {
        if(vr?.Menu is not {} menu)return;
        var rows=library.Where(e=>e.Movie==movies).Select(e=>new MenuRow(e.Name,()=>BeginInvoke(new Action(async()=>{if(editorMode)UseStandalone();await LoadShow(e);})))).ToList();
        menu.GoBack=BuildLibraryMenu;
        menu.Set(movies?"Movies":"Sequences",rows.ToArray(),"Library: "+settings.ConfigFolder+"\nSelect a title to load it, then choose Play. Movement pauses while this menu is open.");
    }
    private static NumericUpDown Number(decimal min,decimal max,decimal value,decimal step)=>new(){Minimum=min,Maximum=max,Value=value,Increment=step,DecimalPlaces=2,Width=108};
    private void Field(string text,Control control)
    {
        var field=new Panel{Width=265,Height=38,Margin=new(0,2,0,4),Tag="field"};
        field.Controls.Add(new Label{Text=text,AutoSize=false,Width=163,Height=30,TextAlign=ContentAlignment.MiddleLeft,Font=new(Font.FontFamily,9)});
        control.Width=100;control.Location=new(163,3);field.Controls.Add(control);sidebar.Controls.Add(field);
    }
    private int MenuDpi => layoutCheckDpi ?? DeviceDpi;
    private int MenuPixels(int logical) => (int)Math.Round(logical * MenuDpi / 96.0);
    private void ResizeMenuControls()
    {
        if(!menuLayoutReady||resizingMenu)return;resizingMenu=true;sidebar.SuspendLayout();
        try
        {
            int width=Math.Max(MenuPixels(230),sidebar.ClientSize.Width-sidebar.Padding.Horizontal-MenuPixels(8));
            foreach(Control control in sidebar.Controls)
            {
                if(control is Label label){label.MaximumSize=new(width,0);continue;}
                if(control is CheckBox check){check.MaximumSize=new(width,0);continue;}
                control.Width=width;
                if(control is Button button)
                    button.Height=Math.Max(MenuPixels(34),button.GetPreferredSize(new Size(width,0)).Height);
                if(control is Panel field&&Equals(field.Tag,"field"))
                {
                    int inputWidth=MenuPixels(108),gap=MenuPixels(10);
                    var caption=(Label)field.Controls[0];var input=field.Controls[1];
                    caption.Width=Math.Max(MenuPixels(80),width-inputWidth-gap);
                    input.Width=inputWidth;input.Left=width-inputWidth;
                    int captionHeight=TextRenderer.MeasureText(caption.Text,caption.Font,new Size(caption.Width,int.MaxValue),TextFormatFlags.WordBreak).Height;
                    field.Height=Math.Max(MenuPixels(38),Math.Max(captionHeight,input.PreferredSize.Height)+MenuPixels(8));
                    caption.Height=field.Height;input.Top=(field.Height-input.Height)/2;
                }
            }
        }
        finally{sidebar.ResumeLayout();resizingMenu=false;}
    }
    private void AddLabel(string text,float size,bool bold=false)=>sidebar.Controls.Add(new Label{Text=text,AutoSize=true,MaximumSize=new(265,0),Margin=new(0,8,0,5),Font=new(Font.FontFamily,size,bold?FontStyle.Bold:FontStyle.Regular)});
    private Button Button(string text,Action action)
    {var button=new StudioDesktopButton{Text=text,Width=265,Height=34};button.Click+=(_,_)=>action();return button;}
    private record Asset(string Label,string Path){public override string ToString()=>Label;}
    private void ScanAssets()
    {
        if(!Directory.Exists(config.Text))throw new DirectoryNotFoundException("Choose the editor's Config folder first.");
        void Fill(ComboBox box,string folder,string selected,bool none)
        {
            box.Items.Clear();if(none)box.Items.Add(new Asset("None — studio floor",""));
            string path=Path.Combine(config.Text,"Models",folder);
            if(Directory.Exists(path))foreach(string file in Directory.EnumerateFiles(path,"*.urdf",SearchOption.AllDirectories))
            {
                var relative=Path.GetRelativePath(config.Text,file).Replace('\\','/');
                string folderName=Path.GetFileName(Path.GetDirectoryName(file))??"";
                string label=folderName switch{"Johnny5FullBody"=>"Johnny 5 — full body","Johnny5Head"=>"Johnny 5 — head and neck","MatrixConstruct"=>"Matrix Construct","Johnny5Backdrop"=>"Johnny 5 studio",_=>folderName+" / "+Path.GetFileNameWithoutExtension(file)};
                box.Items.Add(new Asset(label,relative));
            }
            box.SelectedItem=box.Items.Cast<Asset>().FirstOrDefault(a=>string.Equals(a.Path,selected,StringComparison.OrdinalIgnoreCase));
            if(box.SelectedIndex<0&&box.Items.Count>0)box.SelectedIndex=0;
        }
        Fill(robot,"Robots",settings.Robot,false);Fill(backdrop,"Scenery",settings.Backdrop,true);
    }
    private ViewerSettings ReadControls()=>new(){ConfigFolder=Path.GetFullPath(config.Text),Robot=(robot.SelectedItem as Asset)?.Path??throw new InvalidOperationException("Choose a robot."),
        Backdrop=(backdrop.SelectedItem as Asset)?.Path??"",Scale=(float)scale.Value,RobotHeightMetres=(float)robotFeet.Value*.3048f,
        ControllerLocomotion=locomotionEnabled.Checked,MoveSpeed=(float)walkSpeed.Value,SnapTurnDegrees=(float)snapAngle.Value,
        UseEditorFollowBackdrop=settings.UseEditorFollowBackdrop,ShowFollowStatus=settings.ShowFollowStatus,ShowTransport=settings.ShowTransport,ControllerConfirmation=settings.ControllerConfirmation,ControllerFeedbackVolume=settings.ControllerFeedbackVolume,
        CleanOutputEnabled=settings.CleanOutputEnabled,CleanOutputDisplay=settings.CleanOutputDisplay,CleanOutputFullscreen=settings.CleanOutputFullscreen,CleanOutputWidth=settings.CleanOutputWidth,CleanOutputHeight=settings.CleanOutputHeight,CleanOutputOverlays=settings.CleanOutputOverlays,
        UseSteamVrBindings=inputMode.SelectedIndex==1,
        UseSavedPlaybackScene=useSavedScene.Checked,
        // Persist logical width so moving between monitors/computers keeps the same usable menu.
        MenuWidth=(int)Math.Round(workspace.SplitterDistance * 96.0 / MenuDpi),
        Distance=(float)distance.Value,Horizontal=(float)horizontal.Value,Heading=(float)heading.Value,FloorGrid=grid.Checked};
    private void ConfigureNavigation()
    {
        settings.ControllerLocomotion=locomotionEnabled.Checked;settings.MoveSpeed=(float)walkSpeed.Value;settings.SnapTurnDegrees=(float)snapAngle.Value;vr?.Configure(settings);
    }
    private async Task LoadScene()
    {
        if(editorMode){status.Text="Return to Standalone playback to change the viewer scene. In VR editing, use the editor's Scene Setup.";return;}
        if(loading||preparingScene)return;EndArcade();loading=true;apply.Enabled=save.Enabled=start.Enabled=false;
        try
        {
            var next=ReadControls();
            var progress=new Progress<string>(text=>{if(!closing)status.Text=text;});
            var loaded=await Task.Run(()=>new UrdfReader().Load(next,progress));
            if(closing)return;
            player.Unload();renderer!.Upload(loaded);scene=loaded;settings=next;ResetCamera();
            vr?.Configure(settings);
            renderEnabled=true;lastStats=clock.Elapsed.TotalSeconds;frames=0;
            status.Text=$"Ready · Robot {loaded.RobotHeightMetres:F2} m / {loaded.RobotHeightMetres/.3048f:F1} ft · {loaded.Triangles:N0} triangles · {renderer.AdapterName}"+(loaded.Warnings.Length>0?$" · {loaded.Warnings.Length} material warnings (see Diagnostics)":"");
            Diagnostics.WriteScene(loaded,settings);
        }
        catch(Exception ex){Fail(ex);}
        finally{loading=false;if(!closing){apply.Enabled=save.Enabled=true;start.Enabled=scene!=null&&vr==null;}}
    }
    private void Save()
    {
        try{ReadControls().Save();status.Text="Settings saved in Johnny5VR/Settings. Apply scene to use any pending placement changes.";}
        catch(Exception ex){Fail(ex);}
    }
    private void ResetCamera(){target=new(settings.Horizontal,(scene?.RobotHeightMetres??settings.Scale)*.55f,-settings.Distance);yaw=.28f;pitch=.16f;orbitDistance=3.3f*settings.Scale;}
    private void StartVr()
    {
        if(vr!=null||scene==null)return;
        try
        {
            var session=new VrSession(settings,inputModeAtLaunch);
            session.Configure(settings);
            session.BodyMotion.ToggleFeedback=PlayControllerFeedback;
            session.ToggleFollowSelections=ToggleFollowSelections;
            if(session.Menu!=null)session.Menu.RestoreRegularMenu=BuildVrHome;
            ConfigureTransport(session);
            try
            {
                renderEnabled=false;renderer?.Dispose();renderer=null;
                renderer=new(viewport.Handle,viewport.Width,viewport.Height,session.Adapter);renderer.Upload(scene);vr=session;
                if(editorMode&&editorReady&&editor!=null){vr.AttachEditor(editor.Process.Id,editor.Command,editor.Navigate,()=>editor.Desktop,editor.Pose,editor.Motion,editor.Config,editor.Recording);if(lastEditorFrame!=null)renderer.ApplyLive(lastEditorFrame,settings);}
                ApplyCleanOutput();BuildVrHome();
                start.Enabled=false;stop.Enabled=true;status.Text="VR active · Index left stick: walk / strafe · Right stick: snap turn · Esc stops VR";
                lastStats=clock.Elapsed.TotalSeconds;frames=0;
            }
            catch{session.Dispose();throw;}
            finally{renderEnabled=true;}
        }
        catch(Exception ex){Fail(ex);}
    }
    private void StopVr()
    {
        EndArcade();
        vr?.Dispose();vr=null;start.Enabled=scene!=null;stop.Enabled=false;status.Text="Desktop preview · VR stopped";
    }
    private void Render()
    {
        if(rendering||closing||renderer==null||WindowState==FormWindowState.Minimized&&vr==null&&renderer?.CleanOutputActive!=true)return;
        rendering=true;
        BridgeTiming.BeginFrame(1000.0/(vr?.RefreshRate is >1 and var hz?hz:60));
        try
        {
            if(editorMode){var pumpStart=BridgeTiming.Start();PumpEditor();BridgeTiming.Stop(BridgeTiming.Section.Pump,pumpStart);}
            else
            {
            player.Tick();
            if(!preparingScene&&player.Current!=null&&preparedSequence!=player.Current.File&&!loadingShow)
            {
                _=PreparePlaybackSceneSafely();
            }
            if(arcade==null&&scene?.Rig is {} rig)
            {
                UpdatePlaybackEffects(rig);
                if(smoke&&smokeFrames>=35&&smokeFrames<=70)
                {
                    rig.Apply(new(){["servo:NeckTurn"]=(smokeFrames<55?-65:65,0,"NeckTurn"),["servo:IrisClose"]=(smokeFrames<55?-100:100,0,"IrisClose")});
                    renderer.UpdatePose(rig.Palette);renderer.Apertures=rig.Apertures;
                }
                else if(player.Current!=null){rig.Apply(player.Pose(),SecondaryMotion(rig));renderer.UpdatePose(rig.Palette);renderer.Apertures=rig.Apertures;}
                else{rig.Apply(new());renderer.UpdatePose(rig.Palette);renderer.Apertures=Vector4.Zero;}
                renderer.UpdateSecondary(rig,player.MouthAmplitude,player.RgbFrame);
                renderer.UpdateEffects(rig.Bolts);
                UpdateEffectAudio(rig);
            }
            if(Environment.TickCount64-lastPlaybackStatus>=250)
            {
                lastPlaybackStatus=Environment.TickCount64;
                if(playButton!=null)playButton.Text=player.TransportLabel;
                playbackStatus.Text=arcade?.Status??(loadingShow?"Loading playback…":player.Status+(playbackWarnings!=""?" · compatibility notices":""));
            }
            }
            if(smoke&&smokeFrames>=70)ApplySecondarySmoke();
            if(effectsSmoke)ApplyEffectsSmoke();
            var eye=target+new Vector3(MathF.Sin(yaw)*MathF.Cos(pitch),MathF.Sin(pitch),MathF.Cos(yaw)*MathF.Cos(pitch))*orbitDistance;
            if(!editorMode)
            {
                renderer.CaptureCamera=settings.CleanOutputEnabled&&arcade==null&&scene?.Rig is {} shotRig&&
                    player.Current is {CameraCues.Length:>0} current
                    ?shotRig.CaptureShot(current.CameraCues,player.Position,eye,target):null;
            }
            bool tracked=renderer.Render(vr,Matrix4x4.CreateLookAt(eye,target,Vector3.UnitY),eye);
            if(settings.CleanOutputEnabled&&cleanOutputWindow!=null&&!renderer.CleanOutputActive)
                DisableCleanOutput(true,"Clean Output unavailable — application preview restored. "+renderer.CleanOutputFailure);
            if(settings.CleanOutputEnabled&&settings.CleanOutputOverlays&&renderer.OverlayCaptureFailure.Length>0)status.Text="VR overlay capture unavailable; Clean Output continues with the scene. "+renderer.OverlayCaptureFailure;
            if(effectsSmoke)CaptureEffectsSmoke();
            if(smoke&&smokeFrames>=70)CaptureSecondarySmoke();
            if(editorSmoke)CheckEditorSmoke();
            if(smoke&&smokeFrames is 40 or 65)
            {
                renderer.Screenshot(Path.Combine(Diagnostics.Folder,smokeFrames==40?"animation-left.png":"animation-right.png"));
                if(smokeFrames==65)
                {
                    using var a=new Bitmap(Path.Combine(Diagnostics.Folder,"animation-left.png"));using var b=new Bitmap(Path.Combine(Diagnostics.Folder,"animation-right.png"));
                    int changed=0;for(int y=0;y<a.Height;y+=2)for(int x=0;x<a.Width;x+=2)if(a.GetPixel(x,y)!=b.GetPixel(x,y))changed++;
                    if(changed<200)throw new InvalidOperationException("Animated GPU poses did not change the rendered model.");
                    File.WriteAllText(Path.Combine(Diagnostics.Folder,"animation-render-check.txt"),$"PASS: two calibrated neck/iris poses changed {changed} sampled pixels in actual D3D11 output.");
                }
            }
            frames++;
            double elapsed=clock.Elapsed.TotalSeconds-lastStats;
            if(elapsed>=1){performance.Text=$"{(vr==null?"Preview":"VR submission")}: {frames/elapsed:F1} fps"+(vr!=null?$"\n{vr.Width} × {vr.Height} per eye\n{vr.InputStatus}":"")+(!tracked?"\nWaiting for headset tracking":"")+(BridgeTiming.Summary!=""?"\n"+BridgeTiming.Summary:"");if(vr!=null){controllerReadout.Text=vr.InputDetails+"\n"+vr.TimingStatus;vr.Menu?.PlaybackStatus(player.Status,player.Running,player.TransportLabel);}frames=0;lastStats=clock.Elapsed.TotalSeconds;}
            if(smoke&&scene!=null&&smokeReady)
            {
                smokeFrames++;
                if(smokeFrames==10){workspace.SplitterDistance=500;PerformLayout();ResizeMenuControls();VerifyFixedControls();}
                if(smokeFrames==20){ClientSize=new(1000,680);PerformLayout();VerifyFixedControls();}
                if(smokeFrames==30){ClientSize=new(1280,900);workspace.SplitterDistance=420;PerformLayout();ResizeMenuControls();VerifyFixedControls();}
            }
            if(smoke&&scene!=null&&smokeFrames==130)
            {
                string path=Path.Combine(ViewerSettings.Home,"Diagnostics","desktop-preview.png");renderer.Screenshot(path);
                Diagnostics.VerifyScreenshot(path);
                using(var ui=new Bitmap(Width,Height))
                {
                    DrawToBitmap(ui,new Rectangle(0,0,Width,Height));
                    using var frame=new Bitmap(path);using var graphics=Graphics.FromImage(ui);
                    var position=PointToClient(viewport.PointToScreen(Point.Empty));
                    // DrawToBitmap excludes the native swap chain; compose its actual frame.
                    var border=new Point((Width-ClientSize.Width)/2,Height-ClientSize.Height-(Width-ClientSize.Width)/2);
                    graphics.DrawImage(frame,new Rectangle(position.X+border.X,position.Y+border.Y,viewport.Width,viewport.Height));
                    ui.Save(Path.Combine(ViewerSettings.Home,"Diagnostics","viewer-window.png"));
                }
                File.WriteAllText(Path.Combine(ViewerSettings.Home,"Diagnostics","smoke-result.txt"),"PASS: actual D3D11 rendering and screenshot content verified.");Close();
            }
        }
        catch(Exception ex){if(vr!=null)StopVr();else renderEnabled=false;Fail(ex);}
        finally
        {
            rendering=false;
            try
            {
                BridgeTiming.EndFrame();
                if(editorMode&&editor is {Process.HasExited:false})BridgeTiming.EditorProcess(editor.Process.Id);
                BridgeTiming.FlushIfDue((editorMode?"editor":"standalone")+(vr!=null?"-vr":"-desktop")+(vr?.BodyMotion.Following==true?"-follow":"")+(arcade!=null?"-game":""),vr?.EditorPanel==null?"":vr.EditorPanel.Visible?"shown":"hidden",vr?.RefreshRate??60);
            }
            // Timing must never interrupt rendering, even if Diagnostics cannot be written.
            catch(Exception ex){try{Diagnostics.Log(ex);}catch{}}
        }
    }
    private async Task PreparePlaybackSceneSafely()
    {
        try{await PreparePlaybackScene();}
        catch(Exception ex){player.Unload();playbackWarnings=ex.Message;BuildVrHome();}
    }
    private void VerifyFixedControls()
    {
        Rectangle InWindow(Control c)
        {
            var location=c.Location;
            for(var parent=c.Parent;parent!=null&&parent!=this;parent=parent.Parent)location.Offset(parent.Location);
            return new(location,c.ClientSize);
        }
        foreach(var control in new Control[]{apply,save,start,stop,status,controllerReadout})
        {
            var rect=InWindow(control);
            if(!ClientRectangle.Contains(rect)||rect.Width<80||rect.Height<20)throw new InvalidOperationException("A fixed action/status control was clipped during resizing: "+control.Text+$"; bounds {rect}, client {ClientRectangle}, DPI {MenuDpi}");
        }
        if(InWindow(apply).IntersectsWith(InWindow(save))||InWindow(start).IntersectsWith(InWindow(stop)))throw new InvalidOperationException("Transport buttons overlap after menu resizing.");
        if(config.Width<sidebar.ClientSize.Width-sidebar.Padding.Horizontal-MenuPixels(20))throw new InvalidOperationException($"Menu fields did not expand with the divider: field {config.Width}, sidebar {sidebar.ClientSize.Width}, padding {sidebar.Padding.Horizontal}, DPI {MenuDpi}.");
        Directory.CreateDirectory(Diagnostics.Folder);
        File.AppendAllText(Path.Combine(Diagnostics.Folder,"menu-resize-checks.txt"),$"PASS: window {ClientSize.Width}x{ClientSize.Height}, menu {workspace.SplitterDistance}; actions and status visible.\n");
    }
    // SteamVR's WaitGetPoses paces headset frames. A WinForms timer would cap
    // submission near the Windows timer rate, so use an idle render loop instead.
    private void RenderWhenIdle(object? sender,EventArgs e)
    {
        while(!closing&&!rendering&&renderEnabled&&renderer!=null&&scene!=null&&!PeekMessage(out _,IntPtr.Zero,0,0,0))
        {
            if(WindowState==FormWindowState.Minimized&&vr==null&&renderer?.CleanOutputActive!=true){WaitMessage();return;}
            Render();
        }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage{public IntPtr Window;public uint Message;public UIntPtr WParam;public IntPtr LParam;public uint Time;public Point Position;public uint Private;}
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out NativeMessage message,IntPtr window,uint min,uint max,uint remove);
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool WaitMessage();
    private void Fail(Exception ex)
    {
        Diagnostics.Log(ex);status.Text=ex.Message;
        if(smoke||editorSmoke||effectsSmoke){Environment.ExitCode=1;Close();}
        else MessageBox.Show(this,ex.Message,"J5 Studio VR Player",MessageBoxButtons.OK,MessageBoxIcon.Information);
    }
}
