// ---------------------------------------------------------------------------
// Program.cs
//
// Explicit application entry point. Normally WPF auto-generates Main from
// App.xaml (when its build action is "ApplicationDefinition"), but that
// detection can fail depending on how the project is imported - producing
// "Program does not contain a static 'Main' method suitable for an entry
// point". Defining Main explicitly (and pointing <StartupObject> at it in
// the .csproj) makes the entry point unambiguous in every configuration.
//
// [STAThread] is required: WPF must run on a single-threaded-apartment
// thread.
// ---------------------------------------------------------------------------

namespace ServoAnimator
{
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--vr-travel-check")
            {
                try { MainWindow.RunTravelExtentChecks(args[1]); }
                catch (Exception ex) { System.IO.File.WriteAllText(args[1],ex.ToString()); Environment.ExitCode=1; }
                return;
            }
            if (args.Length == 2 && args[0] == "--urdf-prop-check")
            {
                try { UrdfPropChecks.Run(args[1]); VrPropBridgeChecks.Append(args[1]); }
                catch (Exception ex) { System.IO.File.WriteAllText(args[1],ex.ToString()); Environment.ExitCode=1; }
                return;
            }
            if (args.Length == 2 && args[0] == "--workspace-check")
            {
                try { MainWindow.RunWorkspaceChecks(args[1]); }
                catch (Exception ex) { System.IO.File.WriteAllText(args[1], ex.ToString()); Environment.ExitCode = 1; }
                return;
            }
            if(args.Length==3&&(args[0]=="--vr-pose-check"||args[0]=="--vr-effects-check"||args[0]=="--vr-rehearsal-check"))
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(args[2])));
                VrEditorMode.Configure(new[]{"--vr-editor","--vr-pipe",Guid.NewGuid().ToString(),"--vr-config",args[1]});
                var checks=new App();checks.InitializeComponent();
                checks.Startup+=(_,_)=>checks.Dispatcher.BeginInvoke(new Action(()=>{if(args[0]=="--vr-pose-check"||args[0]=="--vr-rehearsal-check")((MainWindow)checks.MainWindow).BeginVrPoseChecks(args[2],args[0]=="--vr-rehearsal-check");else ((MainWindow)checks.MainWindow).BeginVrEffectChecks(args[2]);}));
                checks.Run();return;
            }
            if(args.Length==2&&args[0]=="--vr-popup-check")
            {
                try{VrPopupChecks.Run(args[1]);}
                catch(Exception ex){System.IO.File.WriteAllText(args[1],ex.ToString());Environment.ExitCode=1;}
                return;
            }
            if(args.Length==2&&args[0]=="--vr-motion-check")
            {
                try{VrMotionChecks.Run(args[1]);}
                catch(Exception ex){System.IO.File.WriteAllText(args[1],ex.ToString());Environment.ExitCode=1;}
                return;
            }
            if(args.Length==3&&args[0]=="--vr-media-check")
            {
                try
                {
                    using var reader=new VideoFrames.Reader(args[1]);
                    var times=new[]{0.0,.1,.2,2.0,.1};
                    var frames=times.Select(reader.Read).ToArray();
                    if(frames.Any(f=>f.Width<1||f.Height<1||f.Pixels.Length!=f.Width*f.Height*4))throw new InvalidOperationException("Invalid decoded video dimensions.");
                    if(frames[3].Seconds<1.8||Math.Abs(frames[1].Seconds-frames[4].Seconds)>.1)throw new InvalidOperationException("Video seeking did not return the requested pictures.");
                    if(frames.Select(f=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(f.Pixels))).Distinct().Count()<2)throw new InvalidOperationException("Video pictures did not advance.");
                    System.IO.File.WriteAllText(args[2],"PASS: persistent VR video decoding, forward playback, large jump and backward seek.");
                }
                catch(Exception ex){System.IO.File.WriteAllText(args[2],ex.ToString());Environment.ExitCode=1;}
                return;
            }
            VrEditorMode.Configure(args);
            if (args.Length > 0 && args[0] == "--editor-api")
            {
                Environment.ExitCode = EditorApiClient.Run(args).GetAwaiter().GetResult();
                return;
            }
            if(!VrEditorMode.Active)
            {
                System.Windows.MessageBox.Show("Start Johnny5VR.exe and choose Open / resume VR editor. This separate editor needs the viewer's selected Config folder and VR session.","J5 VR Animation Editor");
                return;
            }
            var app = new App();
            app.InitializeComponent();   // loads App.xaml (StartupUri -> MainWindow)
            app.Run();
        }
    }
}
