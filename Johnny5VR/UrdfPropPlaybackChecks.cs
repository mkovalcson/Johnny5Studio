using ServoAnimator;

namespace Johnny5VR;
internal static class UrdfPropPlaybackChecks
{
    internal static void Run()
    {
        string scratch=Directory.CreateTempSubdirectory("J5VrPropCheck-").FullName;
        try
        {
            UrdfPropLibrary.EnsureBuiltIns(scratch);
            File.WriteAllText(Path.Combine(scratch,"floor.urdf"),"<robot name='Stage'><link name='test_floor'><visual name='slab'><origin xyz='0 0 -0.2'/><geometry><box size='3 3 0.2'/></geometry></visual></link></robot>");
            var settings=new ViewerSettings{ConfigFolder=scratch,Robot=UrdfPropLibrary.RemoteFile,Backdrop="floor.urdf",Distance=2};
            var placement=new UrdfPropPlacement(UrdfPropLibrary.RemoteFile,.5,.6,.75,90);
            var stage=new UrdfStageSettings{ModelFile="floor.urdf",Props=[placement]};
            var background=new BackdropCommand{Background=new(){BackgroundMode=UrdfBackgroundMode.Urdf3D,Stage=stage}};
            File.WriteAllText(Path.Combine(scratch,"scene.json"),System.Text.Json.JsonSerializer.Serialize(new BackgroundLibraryItem{Name="Prop test",Settings=background}));
            var scene=PlaybackLibrary.Scene(scratch,"scene.json",settings.Robot,settings.Backdrop);
            if(scene.Stage?.Props?.Single()!=placement)throw new InvalidOperationException("Saved scene did not carry its prop placement to the player.");
            var empty=new UrdfReader().Load(settings);
            settings.PlaybackStage=scene.Stage;
            var full=new UrdfReader().Load(settings);
            var prop=full.Opaque.Skip(empty.Opaque.Length).ToArray();
            if(prop.Length==0||Math.Abs(prop.Min(p=>p.Position.Y)-.75f)>.001f||Math.Abs(prop.Max(p=>p.Position.X)-.67f)>.001f)
                throw new InvalidOperationException("Standalone VR did not render the prop at its saved floor-relative position and yaw.");
            var stageFloor=full.Opaque.Skip(empty.Opaque.Length-36).Take(36).Max(p=>p.Position.Y);
            if(Math.Abs(stageFloor)>.001f)throw new InvalidOperationException("The detected floor did not align with the VR standing origin.");
            Console.WriteLine("PASS: 3 standalone VR checks for saved prop placement, rendered geometry and detected floor alignment.");
        }
        finally
        {
            if(ConfigurationPaths.IsWithin(Path.GetTempPath(),scratch)&&Path.GetFileName(scratch).StartsWith("J5VrPropCheck-",StringComparison.Ordinal))
                Directory.Delete(scratch,true);
        }
    }
}
