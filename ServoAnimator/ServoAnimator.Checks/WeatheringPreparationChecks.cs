using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows.Threading;
using System.Windows.Media.Media3D;
using ServoAnimator;

internal static partial class Program
{
    private static void WeatheringPreparationChecks()
    {
        var app=new App();app.InitializeComponent();DisableTestStartup(app);
        Exception failure=null;
        app.Dispatcher.BeginInvoke(async () =>
        {
            var results=new List<object>();
            try
            {
                foreach(var model in new[]{"Johnny5Head","Johnny5FullBody"})
                {
                    var scene=UrdfScene.Load(Path.GetFullPath($"Config/Models/Robots/{model}/johnny5_head.urdf"));
                    var visuals=(Dictionary<string,GeometryModel3D>)typeof(UrdfScene).GetField("_visuals",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(scene);
                    var original=visuals.ToDictionary(p=>p.Key,p=>p.Value.Material);
                    int pulses=0;var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(25)};
                    timer.Tick+=(_,_)=>pulses++;timer.Start();
                    var elapsed=Stopwatch.StartNew();var pending=scene.RequestWeathering(1);double requestMs=elapsed.Elapsed.TotalMilliseconds;
                    Check(ReferenceEquals(pending,scene.RequestWeathering(1)),"Repeating the same selection reuses its pending task");
                    Check(requestMs<500,"Selecting an uncached finish promptly returns control to the UI");
                    await pending;double firstMs=elapsed.Elapsed.TotalMilliseconds;
                    Check(scene.WeatheringLevel==1,"Background preparation publishes the requested level on the UI thread");
                    Check(firstMs<500||pulses>=2,"The UI heartbeat continues during expensive preparation");
                    var firstMaterials=visuals.ToDictionary(p=>p.Key,p=>p.Value.Material);

                    pending=scene.RequestWeathering(2);var clean=scene.RequestWeathering(0);
                    Check(clean.IsCompleted&&scene.WeatheringLevel==0,"Clean applies immediately while another finish is being prepared");
                    await pending;Check(scene.WeatheringLevel==0,"Canceled preparation cannot overwrite Clean");
                    elapsed.Restart();await scene.RequestWeathering(2);double secondMs=elapsed.Elapsed.TotalMilliseconds;
                    pending=scene.RequestWeathering(3);var cached=scene.RequestWeathering(1);
                    Check(cached.IsCompleted&&scene.WeatheringLevel==1,"A cached finish immediately replaces a pending selection");
                    await pending;Check(scene.WeatheringLevel==1,"Stale background completion cannot replace the latest selection");
                    Check(visuals.All(p=>ReferenceEquals(p.Value.Material,firstMaterials[p.Key])),"Returning to a prepared level reuses its exact material objects");
                    elapsed.Restart();await scene.RequestWeathering(3);double thirdMs=elapsed.Elapsed.TotalMilliseconds;
                    var warm=new List<double>();
                    foreach(int level in new[]{1,2,3,2,1})
                    {
                        elapsed.Restart();var request=scene.RequestWeathering(level);await request;warm.Add(elapsed.Elapsed.TotalMilliseconds);
                        Check(request.IsCompleted&&warm[^1]<500,"Prepared levels switch without regenerating textures");
                    }
                    await scene.RequestWeathering(0,true);Check(scene.WeatheringLevel==0,"Gold applies without waiting for weathering");
                    await scene.RequestWeathering(0);
                    Check(visuals.All(p=>ReferenceEquals(p.Value.Material,original[p.Key])),"Clean restores all original materials after asynchronous changes");
                    timer.Stop();
                    Console.WriteLine($"{model}: request {requestMs:F1} ms; first {firstMs:F0} ms; level 2 {secondMs:F0} ms; level 3 {thirdMs:F0} ms; cached {warm.Min():F1}–{warm.Max():F1} ms; {pulses} UI heartbeats.");
                    results.Add(new{model,requestMs,firstMs,secondMs,thirdMs,cachedMs=warm,uiHeartbeats=pulses});
                }
                PreviewDirectory("weathering-previews");
                File.WriteAllText(Path.Combine(PreviewDirectory("weathering-previews"),"preparation-performance.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
            }
            catch(Exception error){failure=error;}
            finally{app.Shutdown();}
        });
        app.Run();if(failure!=null)throw failure;
    }
}
