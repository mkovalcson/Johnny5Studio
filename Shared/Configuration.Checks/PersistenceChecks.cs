using System.IO;
using System.Text.Json;
using ServoAnimator;

internal static class PersistenceChecks
{
    internal static void Run()
    {
        int count = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); count++; }
        string parent = Directory.CreateTempSubdirectory("J5-SharedConfiguration-").FullName;
        string a = Path.Combine(parent, "ConfigA"), b = Path.Combine(parent, "ConfigB"), moved = Path.Combine(parent, "MovedConfig");
        string previous = FolderSettings.SessionConfigFolder;
        try
        {
            string MakeConfig(string root, int minimum, int maximum)
            {
                string model = Path.Combine(root, "Models/Robots/Johnny5FullBody/johnny5_head.urdf");
                string stage = Path.Combine(root, "Models/Scenery/MatrixConstruct/MatrixConstruct_backdrop.urdf");
                Directory.CreateDirectory(Path.GetDirectoryName(model)!); Directory.CreateDirectory(Path.GetDirectoryName(stage)!);
                File.WriteAllText(model, $"<robot name='fixture'><link name='base'/><servo_animator_calibration><motion servo='NeckTurn' control='NeckTurn' minExtent='{minimum}' maxExtent='{maximum}' zeroExtent='2' /></servo_animator_calibration></robot>");
                File.WriteAllText(stage, "<robot name='stage'><link name='floor'/></robot>");
                File.WriteAllText(Path.Combine(root, "URDFconfig.json"), "{\"version\":10,\"motions\":[]}");
                new UrdfModelSettings { ModelFile = model, Framing = new() { [model] = new UrdfFraming { Distance = 2.2 } } }.Save(root);
                return model;
            }
            string modelA = MakeConfig(a, -41, 43), modelB = MakeConfig(b, -11, 13);
            FolderSettings.SessionConfigFolder = b;
            Check(FolderSettings.Load().ConfigFolderOrDefault == b, "The VR session Config override is retained.");
            File.WriteAllText(Path.Combine(b, "URDFconfig.json"), """{/* legacy comment */ "VERSION":10,"MOTIONS":[],"AUDIOLedGAIN":1.75,}""");
            Check(ConfigurationStore.LoadUrdf(b).AudioLedGain == 1.75, "Common URDF reading retains the Player's case-insensitive/comment/trailing-comma compatibility.");
            File.WriteAllText(Path.Combine(b, "ServoConfig.json"), """{"SERVOS":[{"CONTROL":"NeckTurn","DEFAULTPWM":1000}],}""");
            Check(ConfigurationStore.LoadServos(b).Get(RobotControls.NeckTurn).DefaultPwm == 1000, "Common servo reading retains legacy property casing.");
            File.WriteAllText(Path.Combine(b, "UrdfModelSettings.json"), """{/* legacy model settings */ "modelFile":"Models/Robots/Johnny5FullBody/johnny5_head.urdf","framing":{},}""");
            Check(UrdfModelSettings.Load(b).SelectedFile == Path.GetFullPath(modelB), "Common model settings retain the Player's JSON read compatibility.");
            var cfgA = ConfigurationStore.LoadUrdf(a);
            var cfgB = ConfigurationStore.LoadUrdf(b);
            Check(cfgA.Get(ServoNames.NeckTurn, RobotControls.NeckTurn).MinExtent == -41, "Config A uses its own model's embedded calibration.");
            Check(cfgB.Get(ServoNames.NeckTurn, RobotControls.NeckTurn).MinExtent == -11, "Config B uses its own model's embedded calibration.");
            Check(cfgA.Presentation.Stage.ResolveModelFile(a).StartsWith(a), "Missing stage settings use the Config being read.");
            Check(UrdfConfiguration.Load(Path.Combine(a, "URDFconfig.json")).Get(ServoNames.NeckTurn, RobotControls.NeckTurn).MaxExtent == 43, "Direct file loading also uses that file's Config.");
            Check(FolderSettings.Load().ConfigFolderOrDefault == b, "An explicit load does not change the host's session Config.");
            Parallel.For(0, 8, i => {
                string root = i % 2 == 0 ? a : b;
                double expected = i % 2 == 0 ? -41 : -11;
                if (ConfigurationStore.LoadUrdf(root).Get(ServoNames.NeckTurn, RobotControls.NeckTurn).MinExtent != expected)
                    throw new Exception("Concurrent Config loads interfered.");
            });
            Check(true, "Concurrent Config loads remain independent.");
            string servoFile = Path.Combine(a, "ServoConfig.json");
            File.WriteAllText(servoFile, "{ /* legacy file */ \"servos\":[{\"control\":\"NeckTurn\",\"defaultPwm\":999,\"minPwm\":1,\"maxPwm\":9999,\"maestroPort\":25}],\"gangDirections\":[],}");
            var servos = ConfigurationStore.LoadServos(a);
            Check(servos.Servos.Count == ServoConfiguration.CreateDefault().Servos.Count, "Missing physical channels are backfilled.");
            Check(servos.Get(RobotControls.NeckTurn).MaestroPort == 23 && servos.Get(RobotControls.NeckTurn).MinPwm == 500 && servos.Get(RobotControls.NeckTurn).MaxPwm == 2400, "Legacy channel/PWM limits remain enforced.");
            servos.SetGangReversed(ServoNames.FlapsOpen, RobotControls.BrowLeftTopOpen, false);
            servos.LeftTicSerialNumber = "fixture-left"; servos.Save(servoFile);
            var savedServos = ConfigurationStore.LoadServos(a);
            Check(savedServos.Get(RobotControls.NeckTurn).DefaultPwm == 999 && savedServos.LeftTicSerialNumber == "fixture-left", "Custom channel defaults and Tic assignment survive save/load.");
            Check(!savedServos.GangReversed(ServoNames.FlapsOpen, RobotControls.BrowLeftTopOpen), "Custom gang direction survives save/load.");
            Check(ConfigurationStore.LoadServos(b).Get(RobotControls.NeckTurn).DefaultPwm != 999, "One Config's defaults do not change another Config.");
            cfgA.Rendering.RobotFinish = UrdfRobotFinish.GoldJohnny;
            cfgA.Rendering.DefaultAccessory = UrdfAccessory.Toolbox;
            cfgA.Rendering.SelectedRenderer = UrdfRenderer.Direct3D11;
            cfgA.Rendering.Wpf.Brightness = .8; cfgA.Rendering.Direct3D11.Brightness = 1.2;
            cfgA.EyeLightIntensity = 3; cfgA.VentLightIntensity = 4; cfgA.AudioLedGain = 1.5;
            cfgA.BodyMotions["ShoulderPitchLeft"] = new() { MinDegrees = -25, MaxDegrees = 80, Reverse = true };
            cfgA.Save(Path.Combine(a, "URDFconfig.json"));
            var saved = ConfigurationStore.LoadUrdf(a);
            Check(saved.Rendering.RobotFinish == UrdfRobotFinish.GoldJohnny && saved.Rendering.DefaultAccessory == UrdfAccessory.Toolbox, "Finish and accessory defaults survive save/load.");
            Check(saved.Rendering.Wpf.Brightness == .8 && saved.Rendering.Direct3D11.Brightness == 1.2, "Per-renderer profiles remain independent.");
            Check(saved.EyeLightIntensity == 3 && saved.VentLightIntensity == 4 && saved.AudioLedGain == 1.5, "Lighting and voice gain survive save/load.");
            var shoulder = saved.BodySetting(BodyControlCatalog.Find(ServoNames.B_ShoulderRotateLeft));
            Check(shoulder.MinDegrees == -25 && shoulder.MaxDegrees == 80 && shoulder.Reverse, "Saved shoulder aliases retain calibration through migration.");
            var item = new BackgroundLibraryItem { Name = "Fixture scene", RobotModelFile = modelA, Settings = new()
            {
                Framing = new() { Distance = 2.2 }, Background = new()
                {
                    BackgroundMode = UrdfBackgroundMode.Urdf3D,
                    Stage = new() { ModelFile = ModelLocations.DefaultScenery(a) }
                }
            }};
            BackgroundLibrary.Save(a, item, scenes: true);
            var editorScene = SavedSceneCommand.Read(a, item.FileName);
            var playerScene = ConfigurationStore.ReadScene(a, item.FileName);
            Check(ConfigurationPaths.Reference(a, editorScene.RobotModelFile) == playerScene.RobotModelFile, "Editor and Player readers use the same saved scene model.");
            Check(playerScene.Settings.Framing.Distance == 2.2, "Scene framing survives the common reader.");
            foreach (var file in Directory.GetFiles(a, "*.json", SearchOption.AllDirectories))
                Check(!File.ReadAllText(file).Contains(JsonSerializer.Serialize(a)[1..^1]), "Persisted fixture paths are Config-relative: " + file);
            Directory.Move(a, moved);
            var movedModels = UrdfModelSettings.Load(moved);
            Check(movedModels.SelectedFile.StartsWith(moved) && movedModels.Framing.ContainsKey(movedModels.SelectedFile), "Model selection and framing keys resolve after relocating Config.");
            var movedScene = SavedSceneCommand.Read(moved, item.FileName);
            Check(movedScene.RobotModelFile.StartsWith(moved) && movedScene.Settings.Background.Stage.ResolveModelFile(moved).StartsWith(moved), "Scene robot and stage paths resolve after relocating Config.");
            Check(ConfigurationStore.LoadServos(moved).Get(RobotControls.NeckTurn).DefaultPwm == 999, "Custom servo calibration survives relocation.");
            Check(ConfigurationPaths.ResolveAsset(moved + Path.DirectorySeparatorChar, item.FileName) == Path.Combine(moved, item.FileName), "A trailing separator does not change Config asset resolution.");
            bool rejected = false;
            try { ConfigurationPaths.ResolveAsset(moved, "../outside.json"); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Asset traversal outside Config remains rejected.");
            Check(ConfigurationPaths.Reference(moved, new Uri(movedScene.RobotModelFile).AbsoluteUri) == playerScene.RobotModelFile, "Legacy file URIs retain the same Config-relative reference.");
            Console.WriteLine($"PASS: {count} shared configuration save/load, session isolation, concurrency, scene and relocation assertions.");
        }
        finally
        {
            FolderSettings.SessionConfigFolder = previous;
            string full = Path.GetFullPath(parent);
            if (full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) && Path.GetFileName(full).StartsWith("J5-SharedConfiguration-"))
                Directory.Delete(full, recursive: true);
        }
    }
}
