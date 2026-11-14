using System.IO;
using System.Windows.Media.Media3D;
namespace ServoAnimator;
internal static class VrPropBridgeChecks
{
    internal static void Append(string report)
    {
        var rootFolder=Directory.CreateTempSubdirectory("J5PropBridge-").FullName;
        try
        {
            UrdfPropLibrary.EnsureBuiltIns(rootFolder);
            var scene=new UrdfStageSettings{Props=[new(UrdfPropLibrary.RemoteFile,.6,0,.5),new(UrdfPropLibrary.RemoteFile,1.6,0,.75)]};
            var stage=UrdfStagePropModels.Build(scene,rootFolder);
            var root=new Model3DGroup();root.Children.Add(stage);
            var graph=new VrGraphCapture();
            var rest=new Rect3D(0,0,0,1,1,1);
            var frame=graph.Capture(root,null,stage,rest,0,"","props",[]);
            if(frame.Nodes.Length!=6||frame.Nodes.Select(n=>n.Id).Distinct().Count()!=6||frame.Nodes.Any(n=>n.Robot))
                throw new InvalidOperationException("Live VR scene did not carry six distinct static remote visuals.");
            var hidden=graph.Capture(root,null,stage,rest,0,"","props",[],includeBackdrop:false);
            if(hidden.Nodes.Length!=0)throw new InvalidOperationException("Backdrop visibility did not hide its props.");
            File.AppendAllLines(report,["PASS: Live VR bridge carries multiple prop instances with distinct node IDs.","PASS: Prop visibility follows the 3D backdrop."]);
        }
        finally
        {
            if(ConfigurationPaths.IsWithin(Path.GetTempPath(),rootFolder)&&Path.GetFileName(rootFolder).StartsWith("J5PropBridge-",StringComparison.Ordinal))
                Directory.Delete(rootFolder,true);
        }
    }
}
