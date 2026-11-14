using System.IO;
using System.Text.Json;
using Johnny5.VrBridge;

namespace ServoAnimator;

public partial class MainWindow
{
    private VrTravelExtent[] _vrTravelExtents=[];
    private string _vrTravelReceipt="",_vrTravelMessage="";
    private static VrTravelExtent ReadTravelExtent(UrdfConfiguration config,ServoNames servo)
    {
        string key=servo.ToString();
        if(BodyControlCatalog.Find(servo) is {} body)
        {
            var setting=config.BodySetting(body);
            return new(key,setting.MinDegrees,setting.MaxDegrees,Math.Min(-360,setting.MinDegrees),Math.Max(360,setting.MaxDegrees),"°");
        }
        var motion=config.Motions.FirstOrDefault(m=>m.Servo==servo);
        if(motion==null)return null;
        var (min,max)=UrdfConfiguration.ExtentEditorRange(servo);
        string unit=UrdfConfiguration.UnitFor(servo);
        return new(key,motion.MinExtent,motion.MaxExtent,min,max,unit=="deg"?"°":unit);
    }
    private static void WriteTravelExtent(UrdfConfiguration config,ServoNames servo,double min,double max)
    {
        if(BodyControlCatalog.Find(servo) is {} body)
        {
            var setting=config.BodySetting(body);setting.MinDegrees=min;setting.MaxDegrees=max;
        }
        else foreach(var motion in config.Motions.Where(m=>m.Servo==servo))
        {
            motion.MinExtent=min;motion.MaxExtent=max;
            motion.ZeroExtent=Math.Clamp(motion.ZeroExtent,min,max);
        }
    }
    private static void SaveTravelExtentFile(string path,string model,string root,ServoNames servo,VrTravelExtent active,double min,double max)
    {
        string original=File.Exists(path)?File.ReadAllText(path):null;
        var persisted=original!=null?UrdfConfiguration.Load(path,model,root):UrdfConfiguration.CreateDefault(model);
        var previous=ReadTravelExtent(persisted,servo);
        if(previous==null||previous.Minimum!=active.Minimum||previous.Maximum!=active.Maximum)
            throw new InvalidOperationException("The saved range differs from the active one. Save or reload it in URDF Configuration first.");
        WriteTravelExtent(persisted,servo,min,max);
        Directory.CreateDirectory(root);
        string temporary=Path.Combine(root,".urdf-travel-"+Guid.NewGuid().ToString("N")+".tmp");
        try
        {
            persisted.Save(temporary);
            if((File.Exists(path)?File.ReadAllText(path):null)!=original)
                throw new InvalidOperationException("URDF Configuration changed on disk. Reopen Travel Extents and retry.");
            File.Move(temporary,path,true);
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    internal static void RunTravelExtentChecks(string report)
    {
        var dir=Directory.CreateTempSubdirectory("J5TravelExtent-").FullName;
        string file=Path.Combine(dir,"URDFconfig.json");
        try
        {
            var config=UrdfConfiguration.CreateDefault();
            const ServoNames arm=ServoNames.B_ElbowLeft,head=ServoNames.NeckNodUp;
            config.BodySetting(BodyControlCatalog.Find(arm)).Reverse=true;
            config.AudioLedGain=1.4;
            config.Save(file);
            var current=UrdfConfiguration.Load(file);
            var extent=ReadTravelExtent(current,arm);
            SaveTravelExtentFile(file,null,dir,arm,extent,-30,52);
            var saved=UrdfConfiguration.Load(file);
            var updated=ReadTravelExtent(saved,arm);
            if(updated.Minimum!=-30||updated.Maximum!=52||!saved.BodySetting(BodyControlCatalog.Find(arm)).Reverse||saved.AudioLedGain!=1.4)
                throw new InvalidOperationException("Body limits or unrelated settings did not round trip.");
            bool stale=false;
            try{SaveTravelExtentFile(file,null,dir,arm,extent,-20,40);}catch(InvalidOperationException){stale=true;}
            if(!stale||File.ReadAllText(file).Length==0)throw new InvalidOperationException("A stale range was accepted.");
            var headRange=ReadTravelExtent(saved,head);
            double lo=(headRange.Minimum+headRange.Maximum)/2,hi=headRange.Maximum;
            SaveTravelExtentFile(file,null,dir,head,headRange,lo,hi);
            var result=UrdfConfiguration.Load(file);
            var children=result.Motions.Where(m=>m.Servo==head).ToArray();
            if(children.Length<2||children.Any(m=>m.MinExtent!=lo||m.MaxExtent!=hi||m.ZeroExtent<lo||m.ZeroExtent>hi))
                throw new InvalidOperationException("Shared head range or child zero did not round trip.");
            File.WriteAllLines(report,["PASS: Body limits persist with reverse and unrelated settings preserved.","PASS: Stale ranges are rejected.","PASS: Head ranges are shared across children and zeroes stay within the range."]);
        }
        finally
        {
            if(File.Exists(file))File.Delete(file);
            Directory.Delete(dir);
        }
    }
    private void ApplyVrTravelRequest(VrPoseRequest request)
    {
        if(request.Id==_vrTravelReceipt)return;
        _vrTravelReceipt=request.Id;
        try
        {
            if(!VrPoseAllowed||_vrRecording||_urdfConfigWindow!=null||request.Token!=VrPoseToken())
                throw new InvalidOperationException("Pause recording/playback and close URDF Configuration before changing ranges.");
            var view=VrEditorMode.Source??EmbeddedHeadView;
            if(request.Action==VrPoseAction.SaveTravelExtent)
            {
                if(request.Key.Length>1024)throw new InvalidDataException("Invalid range edit.");
                var e=JsonSerializer.Deserialize<VrTravelExtentEdit>(request.Key);
                if(e==null||!FollowNeutralCatalog.Controls.Any(c=>c.Key==e.Key)||!Enum.TryParse<ServoNames>(e.Key,out var servo))
                    throw new InvalidDataException("Unknown Follow control.");
                var active=ReadTravelExtent(_urdfConfig,servo)??throw new InvalidDataException("No URDF range for this control.");
                if(e.OriginalMinimum!=active.Minimum||e.OriginalMaximum!=active.Maximum)
                    throw new InvalidOperationException("The URDF range changed. Reopen Travel Extents before saving.");
                if(!double.IsFinite(e.Minimum)||!double.IsFinite(e.Maximum)||e.Minimum>=e.Maximum||
                    e.Minimum<active.SliderMinimum||e.Maximum>active.SliderMaximum)
                    throw new InvalidDataException("Minimum must be below maximum and within the slider limits.");
                string path=Path.Combine(ConfigRoot,"URDFconfig.json");
                if(ConfigurationHasUnsavedChanges())
                    throw new InvalidOperationException("Resolve the unsaved URDF or servo configuration changes before saving travel extents.");
                SaveTravelExtentFile(path,view.LoadedModelFile,ConfigRoot,servo,active,e.Minimum,e.Maximum);
                WriteTravelExtent(_urdfConfig,servo,e.Minimum,e.Maximum);
                ApplyUrdfConfigurationToViews();
                MarkConfigurationSaved();
                _vrTravelMessage="Saved to URDF Configuration.";
            }
            else _vrTravelMessage="Set both endpoints, then Save to URDF Configuration.";
            _vrTravelExtents=FollowNeutralCatalog.Controls.Select(c=>ReadTravelExtent(_urdfConfig,Enum.Parse<ServoNames>(c.Key))).Where(e=>e!=null).ToArray();
        }
        catch(Exception ex){_vrTravelMessage=ex.Message;}
    }
}
