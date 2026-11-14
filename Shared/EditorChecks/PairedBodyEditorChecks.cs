using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
namespace ServoAnimator;

internal static class PairedBodyEditorChecks
{
    internal static void Run(Action<bool,string> check,string model)
    {
        SequenceInsertionChecks.Run(check);
        var config=UrdfConfiguration.CreateDefault(model);var servoConfig=ServoConfiguration.CreateDefault();
        var view=new RobotHeadView();view.InstallModel(model,UrdfScene.Load(model),new UrdfFraming());view.SetUrdfConfiguration(config);view.SetServoConfiguration(servoConfig);
        foreach(var pair in PairedBodyControls.All)
        {
            check(BodyControlCatalog.IsBody(pair.Servo)&&BodyControlCatalog.IsPairedGang(pair.Servo),pair.Name+" is a shared first-class body gang.");
            check(CommandVM.ServoPickOptions.Any(i=>i.Servo==pair.Servo),pair.Name+" appears in command picker.");
            check(ControllerTargets.TryServo(pair.Target,out var target,out var child)&&target==pair.Servo&&child==null,pair.Name+" is a valid controller mapping target.");
            check(ServoIconProvider.For(pair.Servo)!=null,pair.Name+" has an editor icon.");
            view.ApplyBodyControl(pair.Servo,45);
            var expected=PairedBodyControls.Resolve(pair,config,45);
            check(expected.All(m=>Math.Abs(view.BodyPosition(m.Servo)-m.Value)<.01),pair.Name+" previews both members.");
            var rows=new[]{new ServoStateRow(pair.Servo){Value=60,Offset=1},new ServoStateRow(pair.Left){Value=-20,Offset=2},new ServoStateRow(pair.Right){Value=0}};
            view.ApplyBodyPose(rows);
            check(Math.Abs(view.BodyPosition(pair.Left)+20)<.01,pair.Name+" allows later individual refinement.");
            check(Math.Abs(view.BodyPosition(pair.Right)-PairedBodyControls.Resolve(pair,config,60)[1].Value)<.01,pair.Name+" leaves the other member under gang control.");
            var idle=new ServoStateRow(pair.Servo){Value=0};view.ApplyBodyControl(pair.Left,25);view.ApplyBodyPose([idle]);
            check(Math.Abs(view.BodyPosition(pair.Left)-25)<.01,"Untouched gang does not reset an individual pose.");
        }
        string root=Path.Combine(Path.GetTempPath(),"J5 paired "+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            foreach(var kind in Enum.GetValues<ControllerKind>())
            {
                var map=new ControllerMappingWindow(ControllerProfile.Defaults(kind),()=>new ControllerSample(),_=>{},configRoot:root);
                var target=(ComboBox)typeof(ControllerMappingWindow).GetField("_target",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(map);
                foreach(var pair in PairedBodyControls.All)
                    check(target.Items.Cast<object>().Any(i=>i.GetType().GetProperty("Id")?.GetValue(i)?.ToString()==pair.Target || i.GetType().GetProperty("Target")?.GetValue(i)?.ToString()==pair.Target),pair.Name+" appears in "+kind+" mapping picker.");
                map.Close();
            }
            var document=new AnimationDocument{Commands=PairedBodyControls.All.Select(p=>new ServoCommand{Servo=p.Servo,NumericValue=37,OffsetSeconds=1.25}).ToList()};
            string file=Path.Combine(root,"paired.json");document.Save(file);var loaded=AnimationDocument.Load(file);
            check(loaded.Commands.Select(c=>c.Servo).SequenceEqual(document.Commands.Select(c=>c.Servo)),"Paired command names survive save/load.");
            var uninitialized=(MainWindow)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
            typeof(MainWindow).GetField("_urdfConfig",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(uninitialized,config);
            var expanded=(List<ServoCommand>)typeof(MainWindow).GetMethod("ExpandGangedCommands",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(uninitialized,[document.Commands]);
            check(expanded.Count==10&&expanded.All(c=>!BodyControlCatalog.IsPairedGang(c.Servo)),"Individual export expands each paired command to its two calibrated members.");
            var window=new UrdfConfigWindow(config,servoConfig,(s,v)=>view.ApplyBodyControl(s,v),(s,c,v)=>{},()=>{},root);
            var panel=(StackPanel)window.FindName("BodyControlsPanel");
            string AllText(DependencyObject node)
            {string text=node is TextBlock t?t.Text:"";foreach(var child in System.Windows.LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())text+=" "+AllText(child);return text;}
            string content=AllText(panel);
            foreach(var pair in PairedBodyControls.All)check(content.Contains(pair.Servo.ToString()),pair.Name+" appears in URDF body mapping.");
            window.Close();
        }
        finally{Directory.Delete(root,true);}
    }
}
