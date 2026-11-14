using System.IO;
using System.Windows;
using System.Windows.Threading;
namespace ServoAnimator;

public partial class MainWindow
{
    internal void BeginVrEffectChecks(string report)
    {
        var start=Environment.TickCount64;var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(200)};
        timer.Tick+=(_,_)=>
        {
            if(Environment.TickCount64-start<60000&&EmbeddedHeadView.FallApartClip==null)return;
            timer.Stop();var results=new List<string>();
            try
            {
                void Check(bool good,string message){if(!good)throw new InvalidOperationException(message);results.Add("PASS: "+message);}
                var source=EmbeddedHeadView;int commands=_doc.Commands.Count;
                Check(source.FallApartClip!=null,"VR editor loads its baked disassembly asset.");
                source.SetLaserHidden(false);source.SetToolboxShown(false);source.SetLaserShots(Array.Empty<LaserShot>());
                var before=source.CaptureVrScene("before");var ids=before.Nodes.Select(n=>n.Id).ToHashSet();
                var fire=new[]{new ServoCommand{Servo=ServoNames.FireLaser,TextValue=new FireLaserCommand().ToJson()}};
                source.SetLaserShots(FireLaserTimeline.ShotsAt(fire,.02));var a=source.CaptureVrScene("fire-a");
                var boltIds=a.Nodes.Where(n=>!ids.Contains(n.Id)).Select(n=>n.Id).ToHashSet();
                Check(boltIds.Count>=3,"Bolts and muzzle flash reach the actual VR scene graph.");
                source.SetLaserShots(FireLaserTimeline.ShotsAt(fire,.03));var b=source.CaptureVrScene("fire-b");
                Check(b.Nodes.Count(n=>boltIds.Contains(n.Id))==boltIds.Count,"Laser core/glow/flash reuse stable scene nodes across frames.");
                source.SetLaserShots(Array.Empty<LaserShot>());var cleared=source.CaptureVrScene("clear");
                Check(!cleared.Nodes.Any(n=>boltIds.Contains(n.Id)),"Finished laser bolts leave the streamed scene.");
                var original=cleared.Nodes.ToDictionary(n=>n.Id,n=>n.World);
                source.SetTimelineFallApart(source.FallApartClip.FrameCount-1);var apart=source.CaptureVrScene("apart");
                Check(apart.Nodes.Count(n=>original.TryGetValue(n.Id,out var matrix)&&matrix!=n.World)>20,"Disassembly changes real streamed part transforms.");
                source.SetTimelineFallApart(null);var restored=source.CaptureVrScene("restored");
                Check(restored.Nodes.All(n=>!original.TryGetValue(n.Id,out var matrix)||matrix==n.World),"Reassembly restores the original live editor pose.");
                Check(_doc.Commands.Count==commands,"Effect previews leave the user's timeline untouched.");
                File.WriteAllLines(report,results);
            }
            catch(Exception ex){File.WriteAllText(report,ex.ToString());Environment.ExitCode=1;}
            finally{Application.Current.Shutdown();}
        };
        timer.Start();
    }
}
