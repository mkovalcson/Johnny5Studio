using System.Reflection;

namespace ServoAnimator;

internal static class StudioBuildDetails
{
    internal static string Description
    {
        get
        {
            var assembly=typeof(MainWindow).Assembly;
            string Value(string name)=>assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a=>a.Key==name)?.Value??"unknown";
            string id=Value("StudioBuildId");
            return string.Join(Environment.NewLine,
                "Build "+(id.Length>12?id[..12]:id)+" · VR protocol "+Value("StudioSceneProtocol"),
                "Application location: "+assembly.Location);
        }
    }
}
