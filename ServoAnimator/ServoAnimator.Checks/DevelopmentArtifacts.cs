using System.IO;

internal static partial class Program
{
    // Anchor outputs to the solution, independent of where checks are launched.
    private static string PreviewDirectory(string category) => ArtifactDirectory("Previews", category);
    private static string CheckDirectory(string category) => ArtifactDirectory("Checks", category);
    private static string ArtifactDirectory(string section, string category)
    {
        if (Path.GetFileName(category) != category || category is "." or "..")
            throw new ArgumentException("Expected a preview category folder", nameof(category));
        string solution = AppContext.BaseDirectory;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName,"ServoAnimator.Checks","ServoAnimator.Checks.csproj")))
            { solution = dir.FullName; break; }
        }
        string path = Path.Combine(solution,"DevelopmentArtifacts",section,category);
        Directory.CreateDirectory(path);
        return path;
    }
    private static string ChangeArchivePath(string relative)
    {
        for(var dir=new DirectoryInfo(AppContext.BaseDirectory);dir!=null;dir=dir.Parent)
            if(File.Exists(Path.Combine(dir.FullName,"Johnny5VR","Johnny5VR.csproj")))
                return Path.GetFullPath(Path.Combine(dir.FullName,"Archive","DesktopChanges",relative));
        throw new DirectoryNotFoundException("Studio workspace not found");
    }
}
