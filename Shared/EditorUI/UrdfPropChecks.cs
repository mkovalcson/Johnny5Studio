using System.IO;
using System.Text.Json;
using System.Windows.Media.Media3D;
using System.Xml.Linq;

namespace ServoAnimator;
internal static class UrdfPropChecks
{
    internal static void Run(string report)
    {
        var results=new List<string>();
        void Check(bool pass,string description){if(!pass)throw new InvalidOperationException(description);results.Add(description);}
        var scratch=Directory.CreateTempSubdirectory("J5PropCheck-").FullName;
        try
        {
            string config=Path.Combine(scratch,"Config"),source=Path.Combine(scratch,"Source");
            Directory.CreateDirectory(source);
            Check(File.Exists(new UrdfPropPlacement(UrdfPropLibrary.RemoteFile).Resolve(config)),"A saved scene can resolve the built-in remote before the library opens.");
            var remote=UrdfPropLibrary.List(config).Single();
            Check(remote.ModelFile==UrdfPropLibrary.RemoteFile,"Remote is available in a fresh shared library.");
            string remoteFile=Path.Combine(config,remote.ModelFile);
            var xml=XDocument.Load(remoteFile);
            Check(xml.Descendants("visual").Count(v=>(string)v.Attribute("name")=="single_button")==1,"The remote model has exactly one raised button.");
            string mesh=Path.Combine(source,"panel.obj"),model=Path.Combine(source,"panel.urdf");
            File.WriteAllText(mesh,"v 0 0 0\nv 0.15 0 0\nv 0 0.1 0\nf 1 2 3\n");
            File.WriteAllText(model,"<robot name='Panel'><link name='panel'><visual><geometry><mesh filename='panel.obj'/></geometry></visual></link></robot>");
            var entry=UrdfPropLibrary.Import(config,model);
            File.Delete(mesh);File.Delete(model);
            var imported=Path.Combine(config,entry.ModelFile);
            Check(UrdfScene.Load(imported,optimizeExterior:false).RootModel.Bounds.SizeX>.14,"The imported mesh renders after the original source is removed.");
            Check(!Path.IsPathRooted(entry.ModelFile)&&UrdfPropLibrary.List(config).Count==2,"Imported models are reusable by relative Config path.");
            var stage=new UrdfStageSettings{ModelFile=remote.ModelFile,Props=[new(remote.ModelFile,1,2,.7,90),new(remote.ModelFile,-2,0,0,0,0,0,2)]};
            stage.Validate(config);
            var cloned=stage.Clone();cloned.Props.RemoveAt(0);
            Check(stage.Props.Count==2,"Scene clones isolate their placement lists.");
            var backdrop=new BackdropCommand{Background=new(){BackgroundMode=UrdfBackgroundMode.Urdf3D,Stage=stage}};
            Check(BackdropCommand.Read(backdrop.ToJson()).Background.Stage.Props.SequenceEqual(stage.Props),"Prop coordinates and assets survive scene serialization.");
            var models=UrdfStagePropModels.Build(stage,config);
            var bounds=models.Children[0].Bounds;
            Check(Math.Abs(bounds.Z-.7)<.001&&Math.Abs(bounds.SizeY-.14)<.001&&Math.Abs(bounds.X-.9725)<.001,"WPF placement sets scale/rotation and floor-relative position.");
            var first=((Model3DGroup)((Model3DGroup)models.Children[0]).Children[0]).Children[0];
            var second=((Model3DGroup)((Model3DGroup)models.Children[1]).Children[0]).Children[0];
            Check(!ReferenceEquals(first,second)&&Math.Abs(models.Children[1].Bounds.SizeX-.28)<.001,"Two placements render distinct instances at independent scales.");
            bool rejected=false;
            try {new UrdfPropPlacement(Path.GetFullPath(Path.Combine(scratch,"outside.urdf"))).Resolve(config);}
            catch(FileNotFoundException){rejected=true;}
            Check(rejected,"Saved placements cannot resolve outside Config.");
            File.WriteAllLines(report,results.Select(r=>"PASS: "+r));
        }
        finally
        {
            if(ConfigurationPaths.IsWithin(Path.GetTempPath(),scratch)&&Path.GetFileName(scratch).StartsWith("J5PropCheck-",StringComparison.Ordinal))
                Directory.Delete(scratch,true);
        }
    }
}
