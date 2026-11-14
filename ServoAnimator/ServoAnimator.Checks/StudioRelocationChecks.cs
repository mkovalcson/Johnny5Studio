using System.IO;
using ServoAnimator;

internal static partial class Program
{
    private static void StudioRelocationChecks()
    {
        var studio = new DirectoryInfo(AppContext.BaseDirectory);
        while (studio != null && !File.Exists(Path.Combine(studio.FullName,"Johnny5VR","Johnny5VR.csproj"))) studio=studio.Parent;
        Check(studio!=null,"Locate Studio independently of the current working directory");
        string config=Path.Combine(studio.FullName,"Config");
        Check(Directory.Exists(config),"Shared Studio Config exists");
        foreach(string folder in new[]{"ServoAnimator/ServoAnimator/bin/Release/net10.0-windows","ServoAnimator/ServoAnimator/bin/Debug/net10.0-windows","Johnny5VREditor/bin/Release/net10.0-windows"})
        {
            string application=Path.GetFullPath(Path.Combine(studio.FullName,folder));
            Check(FolderSettings.ResolveConfigFolder("",application)==config,"Development discovery selects shared Config: "+folder);
            Check(FolderSettings.ResolveConfigFolder(Path.GetRelativePath(application,config),application)==config,"Relative configured path resolves: "+folder);
            Check(FolderSettings.ResolveConfigFolder("X:/Johnny5/Software/ServoAnimator/Config",application)==config,"Stale former location falls back to shared Config: "+folder);
        }
        string fixture=Path.Combine(Path.GetTempPath(),"J5StudioPaths-"+Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(Path.Combine(fixture,"Config"));
            Check(FolderSettings.ResolveConfigFolder("",fixture)==Path.Combine(fixture,"Config"),"Portable executable-local Config retains precedence");
        }
        finally {Directory.Delete(fixture,true);}
        Check(File.Exists(Path.Combine(config,"Models/Robots/Johnny5FullBody/johnny5_head.urdf")),"Full robot remains in runtime Config");
        Check(File.Exists(Path.Combine(studio.FullName,"CAD/Robots/Johnny5/Head/SourceCAD/SimplifiedHead2.step")),"Head CAD is separate from runtime assets");
    }
}
