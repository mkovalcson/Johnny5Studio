using System.IO;
namespace ServoAnimator;
public partial class MainWindow
{
    private SequenceInsertion PlanLibraryInsertion(string path,List<ServoCommand> commands,double at)
    {
        double duration=commands.Select(c=>c.OffsetSeconds).DefaultIfEmpty(0).Max();
        using(var json=AnimationJson.ReadFile(path))duration=Math.Max(duration,Math.Max(0,AnimationJson.Number(json.RootElement,"durationSeconds")));
        foreach(var c in commands.Where(c=>c.Servo==ServoNames.Play))
        {
            string audio=ConfigPathService.TryResolve(ConfigRoot,c.TextValue,out var stored)&&File.Exists(stored)?stored:Path.Combine(LibraryAudioFolder(),Path.GetFileName(c.TextValue??""));
            try{using var reader=new NAudio.Wave.AudioFileReader(audio);var trim=c.AudioTrim??new AudioTrimSettings();duration=Math.Max(duration,c.OffsetSeconds+trim.End(reader.TotalTime.TotalSeconds)-trim.Start);}catch{ /* Existing missing-audio handling reports this after review. */ }
        }
        var source=VrInsertionView;
        return new(_doc.Commands,commands,at,duration,c=>source.ExpandSequenceInsertionControl(c));
    }
    private RobotHeadView VrInsertionView=>(_urdfUndocked?_head?.HeadView:EmbeddedHeadView)??EmbeddedHeadView;
}
public sealed partial class RobotHeadView
{
    internal IEnumerable<ServoCommand> ExpandSequenceInsertionControl(ServoCommand command)
    {
        IEnumerable<(ServoNames Servo,RobotControls? Child,double Value)> parts;
        if(PairedBodyControls.Find(command.Servo) is {} pair)
            parts=PairedBodyControls.Resolve(pair,_urdfConfiguration,command.NumericValue).Select(p=>(p.Servo,(RobotControls?)null,p.Value));
        else if(BodyControlCatalog.IsHandGang(command.Servo))parts=HandOpeningValues(command.Servo,command.NumericValue).Select(p=>(p.Servo,(RobotControls?)null,p.Value));
        else if(BodyControlCatalog.IsLegacyShoulder(command.Servo))
        {
            var a=BodyControlCatalog.Find(command.Servo);var b=BodyControlCatalog.Find(BodyControlCatalog.Canonical(command.Servo));
            var from=_urdfConfiguration.BodySetting(a)??BodyMotionSetting.Default(a);var to=_urdfConfiguration.BodySetting(b)??BodyMotionSetting.Default(b);
            parts=[(b.Servo,null,to.CommandForAngle(from.Map(command.NumericValue,a),b))];
        }
        else if(command.Control.HasValue||ServoConfiguration.ControlsFor(command.Servo).Length<=1){yield return command.Clone();yield break;}
        else parts=ServoConfiguration.ControlsFor(command.Servo).Select(child=>(command.Servo==ServoNames.BothEyePop?(child==RobotControls.LeftEyePop?ServoNames.LeftEyePop:ServoNames.RightEyePop):command.Servo,command.Servo==ServoNames.BothEyePop?(RobotControls?)null:child,(double)command.NumericValue));
        foreach(var part in parts){var copy=command.Clone();copy.Servo=part.Servo;copy.Control=part.Child;copy.NumericValue=(int)Math.Round(part.Value);yield return copy;}
    }
}
