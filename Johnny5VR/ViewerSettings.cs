using System.Text.Json;

namespace Johnny5VR;

internal sealed class ViewerSettings
{
    public string ConfigFolder { get; set; } = "";
    public string Robot { get; set; } = "Models/Robots/Johnny5FullBody/johnny5_head.urdf";
    public string Backdrop { get; set; } = "Models/Scenery/MatrixConstruct/MatrixConstruct_backdrop.urdf";
    [System.Text.Json.Serialization.JsonIgnore]
    internal ServoAnimator.UrdfStageSettings? PlaybackStage { get; set; }
    public float Scale { get; set; } = 1;
    public float RobotHeightMetres { get; set; } = 1.8288f;
    public bool ControllerLocomotion { get; set; } = true;
    public bool UseSteamVrBindings { get; set; }
    public bool UseSavedPlaybackScene { get; set; } = true;
    public FeedbackConfirmation ControllerConfirmation {get;set;}=FeedbackConfirmation.Sounds;
    public float ControllerFeedbackVolume {get;set;}=.4f;
    public bool UseEditorFollowBackdrop {get;set;}=true;
    public bool ShowFollowStatus {get;set;}
    // Off by default: Follow leaves B_WaistSwivel to the timeline, so it can still be animated by hand.
    public bool TrackWaistSwivel {get;set;}
    public bool ShowTransport {get;set;}=true;
    public bool CleanOutputEnabled {get;set;}
    public string CleanOutputDisplay {get;set;}="";
    public bool CleanOutputFullscreen {get;set;}=true;
    public bool CleanOutputOverlays {get;set;}
    public int CleanOutputWidth {get;set;}=1920;
    public int CleanOutputHeight {get;set;}=1080;
    public int MenuWidth { get; set; } = 380;
    public float MoveSpeed { get; set; } = 1.2f;
    public float SnapTurnDegrees { get; set; } = 30;
    public float Distance { get; set; } = 2.5f;
    public float Horizontal { get; set; }
    public float Heading { get; set; }
    public bool FloorGrid { get; set; }
    internal static string Home => FindHome();
    internal static string SettingsFile => Path.Combine(Home,"Settings","viewer.json");

    private static string FindHome()
    {
        // A portable bundle must remain independent even when extracted under
        // a development checkout containing a parent Johnny5VR.csproj.
        if(File.Exists(Path.Combine(AppContext.BaseDirectory,"Johnny5VR.portable")))return AppContext.BaseDirectory;
        for(var d=new DirectoryInfo(AppContext.BaseDirectory);d!=null;d=d.Parent)
            if(File.Exists(Path.Combine(d.FullName,"Johnny5VR.csproj")))return d.FullName;
        return AppContext.BaseDirectory;
    }
    internal static ViewerSettings Load()
    {
        var settings=File.Exists(SettingsFile)
            ? JsonSerializer.Deserialize<ViewerSettings>(File.ReadAllText(SettingsFile))??new() : new();
        if(string.IsNullOrWhiteSpace(settings.ConfigFolder))settings.ConfigFolder=ServoAnimator.FolderSettings.ResolveConfigFolder("", Home);
        if(!Path.IsPathRooted(settings.ConfigFolder))settings.ConfigFolder=Path.GetFullPath(Path.Combine(Home,settings.ConfigFolder));
        if(!Enum.IsDefined(settings.ControllerConfirmation))settings.ControllerConfirmation=FeedbackConfirmation.Sounds;
        settings.ControllerFeedbackVolume=Math.Clamp(float.IsFinite(settings.ControllerFeedbackVolume)?settings.ControllerFeedbackVolume:.4f,0,1);
        settings.Scale=Math.Clamp(float.IsFinite(settings.Scale)?settings.Scale:1,.1f,3);
        settings.CleanOutputWidth=Math.Clamp(settings.CleanOutputWidth,320,7680);settings.CleanOutputHeight=Math.Clamp(settings.CleanOutputHeight,180,4320);
        settings.MenuWidth=Math.Clamp(settings.MenuWidth,280,800);
        settings.RobotHeightMetres=Math.Clamp(float.IsFinite(settings.RobotHeightMetres)?settings.RobotHeightMetres:1.8288f,.9144f,2.7432f);
        settings.MoveSpeed=Math.Clamp(float.IsFinite(settings.MoveSpeed)?settings.MoveSpeed:1.2f,.2f,3);
        settings.SnapTurnDegrees=Math.Clamp(float.IsFinite(settings.SnapTurnDegrees)?settings.SnapTurnDegrees:30,15,90);
        settings.Distance=Math.Clamp(float.IsFinite(settings.Distance)?settings.Distance:2.5f,.5f,15);
        settings.Horizontal=Math.Clamp(float.IsFinite(settings.Horizontal)?settings.Horizontal:0,-15,15);
        settings.Heading=float.IsFinite(settings.Heading)?settings.Heading:0;
        return settings;
    }
    internal string Resolve(string relative) => ServoAnimator.ConfigurationPaths.Resolve(ConfigFolder,relative);
    internal void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
        var path=ConfigFolder;
        try
        {
            ConfigFolder=Path.GetRelativePath(Home,path);
            var temp=SettingsFile+".tmp";
            File.WriteAllText(temp,JsonSerializer.Serialize(this,new JsonSerializerOptions{WriteIndented=true}));
            File.Move(temp,SettingsFile,true);
        }
        finally{ConfigFolder=path;}
    }
}
