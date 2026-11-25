using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Johnny5.VrBridge;

internal sealed record StudioBuildIdentity(string Version,string BuildId,string Protocol)
{
    internal static StudioBuildIdentity Read(string assemblyFile)
    {
        using var stream=File.OpenRead(assemblyFile);
        using var pe=new PEReader(stream);
        var metadata=pe.GetMetadataReader();
        var assembly=metadata.GetAssemblyDefinition();
        var values=new Dictionary<string,string>();
        foreach(var handle in assembly.GetCustomAttributes())
        {
            var attribute=metadata.GetCustomAttribute(handle);
            if(attribute.Constructor.Kind!=HandleKind.MemberReference)continue;
            var member=metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
            if(member.Parent.Kind!=HandleKind.TypeReference)continue;
            var type=metadata.GetTypeReference((TypeReferenceHandle)member.Parent);
            if(metadata.GetString(type.Namespace)!="System.Reflection"||metadata.GetString(type.Name)!="AssemblyMetadataAttribute")continue;
            var data=metadata.GetBlobReader(attribute.Value);
            if(data.ReadUInt16()!=1)continue;
            var key=data.ReadSerializedString();
            var value=data.ReadSerializedString();
            if(key!=null&&value!=null)values[key]=value;
        }
        return new(assembly.Version.ToString(3),values.GetValueOrDefault("StudioBuildId",""),
            values.GetValueOrDefault("StudioSceneProtocol",""));
    }

    internal string Description=>$"{Version} · build {(BuildId.Length>=12?BuildId[..12]:"unknown")} · protocol {Protocol}";

    internal static string? Mismatch(StudioBuildIdentity player,StudioBuildIdentity editor)
    {
        if(player.Version==editor.Version&&player.BuildId.Length==64&&player.BuildId==editor.BuildId&&
            player.Protocol.Length>0&&player.Protocol==editor.Protocol)return null;
        return string.Join(Environment.NewLine,
            "The VR Player and VR Editor must come from the same build.",
            $"Player: {player.Description}",
            $"Editor: {editor.Description}",
            "Build the J5 Studio solution again, or install both apps from the same VR Full package.");
    }

    internal static string EditorPath(string home,string appFolder,string configuration)
    {
        if(File.Exists(Path.Combine(appFolder,"Johnny5VR.portable")))
            return Path.Combine(appFolder,"Editor","AnimationEditorPlayer.exe");
        string? root=Directory.GetParent(home)?.FullName;
        if(root!=null&&File.Exists(Path.Combine(root,"Johnny5Studio.slnx")))
            return Path.Combine(root,"Johnny5VREditor","bin",configuration,"net10.0-windows","AnimationEditorPlayer.exe");
        return Path.Combine(appFolder,"Editor","AnimationEditorPlayer.exe");
    }
}
