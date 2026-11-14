using System.IO;
using System.Reflection;
using System.Windows.Media.Media3D;
using ServoAnimator;

internal static partial class Program
{
    private static void UrdfRuntimePackageChecks(string fixture, string sourceRoot)
    {
        string destination = Path.Combine(fixture, "RealModelConfig"); Directory.CreateDirectory(destination);
        string body = Path.GetFullPath("ServoAnimator/FullBodyModel/johnny5_head.urdf");
        string backdrop = Path.GetFullPath("ServoAnimator/Models/MatrixConstruct_backdrop.urdf");
        string ExportImport(string source, string label)
        {
            string zip = Path.Combine(fixture, label + ".zip");
            int count = ProjectPackage.Export(sourceRoot, source, zip, ProjectPackageKind.UrdfModel);
            using var plan = ProjectPackageImport.Prepare(zip, destination, ProjectPackageKind.UrdfModel);
            plan.Commit();
            Check(plan.Files.Count == count && plan.Files.All(f => f.FileName.EndsWith(".urdf", StringComparison.OrdinalIgnoreCase) || f.FileName.EndsWith(".stl", StringComparison.OrdinalIgnoreCase) || f.FileName.EndsWith(".obj", StringComparison.OrdinalIgnoreCase) ||
                f.FileName.EndsWith("tread_animation.json") || f.FileName.EndsWith("fall_apart.json") || f.FileName.EndsWith(".backfaces.bin") || f.FileName.EndsWith("ExteriorOptimized/manifest.json")), label + " imports only declared runtime assets");
            Console.WriteLine($"{label}: {count:N0} runtime files, {plan.Files.Sum(f => f.File.Size) / 1048576.0:N1} MB uncompressed.");
            return plan.EntryFile;
        }
        string importedBody = ExportImport(body, "Full-body model"), importedBackdrop = ExportImport(backdrop, "TV scenery model");
        Check(ProjectPackage.ModelIdentity(body) == ProjectPackage.ModelIdentity(importedBody), "Actual full-body URDF identity survives the dependency-only transfer");
        RuntimeModelFeatureChecks(importedBody, importedBackdrop);
    }
    private static void RuntimeModelFeatureChecks(string importedBody, string importedBackdrop)
    {
        var scene = UrdfScene.Load(importedBody);
        var clip = scene.FallApartClip;
        Check(clip?.FrameCount == 105 && clip.Links.Count > 650, "Transferred full-body model retains its complete baked disassembly/reassembly clip");
        var rest = new Dictionary<string, Point3D>();
        foreach (string link in clip.Links) if (scene.TryTransformLinkPoint(link, new Point3D(), out var point)) rest.Add(link, point);
        scene.SetFallApartFrame(104);
        Check(scene.FallApartActive && rest.Any(p => scene.TryTransformLinkPoint(p.Key, new Point3D(), out var point) && (point - p.Value).Length > .2), "Transferred full-body model actually disassembles");
        scene.SetFallApartFrame(0); scene.SetFallApartFrame(null);
        Check(!scene.FallApartActive && rest.All(p => scene.TryTransformLinkPoint(p.Key, new Point3D(), out var point) && (point - p.Value).Length < 1e-6), "Transferred full-body model reassembles to its original joint pose");
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var visuals = (Dictionary<string, GeometryModel3D>)typeof(UrdfScene).GetField("_visuals", flags).GetValue(scene);
        var tread = visuals["body_tread_plates_left_visual"]; var phaseZero = tread.Geometry;
        scene.SetTreadTravel(.01, .01);
        Check(!ReferenceEquals(phaseZero, tread.Geometry), "Transferred full-body model loads and swaps its moving tread frame meshes");
        scene.SetTreadTravel(0, 0);
        Check(ReferenceEquals(phaseZero, tread.Geometry), "Transferred tread animation returns to phase zero");
        scene.SetToolboxShown(false); scene.SetJoint("LaserLower", BodyControlCatalog.LaserLowerMaximum);
        Check(scene.HasLaser && scene.LaserReadyToFire && scene.TryLaserMuzzle(out _, out var aim) && aim.X > .99 && Math.Abs(aim.Z) < 1e-6, "Transferred laser and hidden toolbox retain their geometry, hinge and firing origin");
        var view = new RobotHeadView(deferModelLoad: true); view.InstallModel(importedBody, scene, new UrdfFraming());
        view.SetTimelineBackground(new UrdfPresentationSettings { BackgroundMode = UrdfBackgroundMode.Urdf3D, Stage = new UrdfStageSettings { ModelFile = importedBackdrop } });
        Check(view.HasTvScreen(TvFrontCommand.DefaultScreen), "Transferred scenery retains the TV screen used by video playback");
        view.SetLaserShots(FireLaserTimeline.ShotsAt(new[] { new ServoCommand { Servo = ServoNames.FireLaser, TextValue = new FireLaserCommand { Sound = FireLaserCommand.NoSound }.ToJson() } }, .05));
        Check(view.LaserShotModels > 0, "Transferred full-body model supports generated laser bolt and muzzle flash visuals");
    }
}
