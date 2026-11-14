namespace Johnny5VR;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if(args.Contains("--arcade-preview")){ApplicationConfiguration.Initialize();ArcadeChecks.PreviewRobot();return 0;}
            if(args.Contains("--arcade-combat-check")){ApplicationConfiguration.Initialize();ArcadeCombatChecks.Run();return 0;}
            if(args.Contains("--arcade-check")){ApplicationConfiguration.Initialize();ArcadeChecks.Run();return 0;}
            if(args.Contains("--urdf-prop-check")){UrdfPropPlaybackChecks.Run();return 0;}
            if(args.Contains("--vr-rehearsal-check"))
            {
                ApplicationConfiguration.Initialize();
                int count=0;VrTaskMenuChecks.Run((ok,message)=>{if(!ok)throw new InvalidOperationException(message);count++;});
                Console.WriteLine($"PASS: {count} focused VR menu and rehearsal panel checks.");return 0;
            }
            if(args.Contains("--follow-neutral-check")){ApplicationConfiguration.Initialize();FollowNeutralChecks.Run();return 0;}
            if(args.Contains("--follow-speed-check")){ApplicationConfiguration.Initialize();FollowSpeedChecks.Run();return 0;}
            if(args.Contains("--clean-output-check")){ApplicationConfiguration.Initialize();CleanOutputChecks.Run();return 0;}
            if(args.Contains("--follow-controls-check")){ApplicationConfiguration.Initialize();FollowControlsChecks.Run();return 0;}
            if(args.Contains("--feedback-check")){ApplicationConfiguration.Initialize();ViewerWindow.CheckFeedbackUI();return 0;}
            if(args.Contains("--layout-check"))
            {
                ApplicationConfiguration.Initialize();ViewerWindow.CheckDisplayScaling();return 0;
            }
            if(args.Contains("--glove-preview")){ApplicationConfiguration.Initialize();GloveChecks.Preview();return 0;}
            var settings=ViewerSettings.Load();
            int configIndex=Array.IndexOf(args,"--config");if(configIndex>=0&&configIndex+1<args.Length)settings.ConfigFolder=Path.GetFullPath(args[configIndex+1]);
            if(args.Contains("--check")){Diagnostics.Check(settings);return 0;}
            int replayIndex=Array.IndexOf(args,"--replay-tracking");
            if(replayIndex>=0)
            {
                // --replay-tracking <recording.bin> [--calibration <profile.json>] [--out <file.csv>]
                if(replayIndex+1>=args.Length)throw new ArgumentException("--replay-tracking needs the recording file.");
                string recording=args[replayIndex+1];
                int calibrationIndex=Array.IndexOf(args,"--calibration"),outIndex=Array.IndexOf(args,"--out");
                string calibration=calibrationIndex>=0&&calibrationIndex+1<args.Length?args[calibrationIndex+1]:Path.Combine(settings.ConfigFolder,"VR","BodyTrackingCalibration.json");
                string output=outIndex>=0&&outIndex+1<args.Length?args[outIndex+1]:Path.ChangeExtension(recording,".csv");
                Console.WriteLine(TrackingRecorder.Replay(recording,calibration,output));return 0;
            }
            ApplicationConfiguration.Initialize();
            if(args.Contains("--pose-preview")){PoseLayoutPreview.Write(settings);return 0;}
            Application.ThreadException+=(_,e)=>{Diagnostics.Log(e.Exception);MessageBox.Show(e.Exception.Message,"J5 Studio VR Player");};
            Application.Run(new ViewerWindow(settings,args.Contains("--smoke"),args.Contains("--editor-smoke"),args.Contains("--effects-smoke"),args.Contains("--open-editor")));
            return Environment.ExitCode;
        }
        catch(Exception ex)
        {
            Diagnostics.Log(ex);
            if(!args.Contains("--urdf-prop-check")&&!args.Contains("--vr-rehearsal-check")&&!args.Contains("--follow-neutral-check")&&!args.Contains("--arcade-combat-check")&&!args.Contains("--arcade-preview")&&!args.Contains("--arcade-check")&&!args.Contains("--follow-speed-check")&&!args.Contains("--clean-output-check")&&!args.Contains("--follow-controls-check")&&!args.Contains("--pose-preview")&&!args.Contains("--layout-check")&&!args.Contains("--check")&&!args.Contains("--smoke")&&!args.Contains("--editor-smoke")&&!args.Contains("--effects-smoke"))MessageBox.Show(ex.Message,"J5 Studio VR Player");
            Console.Error.WriteLine(ex);return 1;
        }
    }
}
