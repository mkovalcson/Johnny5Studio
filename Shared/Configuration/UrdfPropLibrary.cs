using System.IO;
using System.Xml.Linq;

namespace ServoAnimator;

public sealed record UrdfPropPlacement(string ModelFile, double X = 0, double Y = 0, double Height = 0,
    double Yaw = 0, double Pitch = 0, double Roll = 0, double Scale = 1)
{
    public string Resolve(string root)
    {
        if (!ConfigurationPaths.TryResolve(root, ModelFile, out string file) ||
            !Path.GetExtension(file).Equals(".urdf", StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("Prop URDF is missing from Config.", ModelFile);
        if (!File.Exists(file) && file.Equals(Path.GetFullPath(Path.Combine(root, UrdfPropLibrary.RemoteFile)), StringComparison.OrdinalIgnoreCase))
            UrdfPropLibrary.EnsureBuiltIns(root);
        if (!File.Exists(file)) throw new FileNotFoundException("Prop URDF is missing from Config.", ModelFile);
        if (new[] { X, Y, Height, Yaw, Pitch, Roll, Scale }.Any(n => !double.IsFinite(n)) ||
            Math.Abs(X)>1000 || Math.Abs(Y)>1000 || Math.Abs(Height)>1000 ||
            Scale < .001 || Scale > 100)
            throw new InvalidDataException("Use finite prop coordinates within ±1000 m and scale .001–100.");
        return file;
    }
}

public sealed record UrdfPropEntry(string Name, string ModelFile)
{
    public override string ToString()=>Name;
}

public static class UrdfPropLibrary
{
    public const string RemoteFile="LibraryProps/OneButtonRemote/model.urdf";
    public static string Folder(string root)=>Path.Combine(root,"LibraryProps");
    public static void EnsureBuiltIns(string root)
    {
        var file=Path.Combine(root,RemoteFile);
        if(File.Exists(file))return;
        Directory.CreateDirectory(Path.GetDirectoryName(file));
        try {using var stream=new FileStream(file,FileMode.CreateNew);using var writer=new StreamWriter(stream);writer.WriteLine(RemoteUrdf);}
        catch(IOException) when(File.Exists(file)) {}
    }
    public static IReadOnlyList<UrdfPropEntry> List(string root)
    {
        EnsureBuiltIns(root);
        var result=new List<UrdfPropEntry>();
        foreach(var file in Directory.EnumerateFiles(Folder(root),"*.urdf",new EnumerationOptions {RecurseSubdirectories=true,AttributesToSkip=FileAttributes.ReparsePoint}))
        {
            try {result.Add(Entry(root,file,XDocument.Load(file)));}
            catch(System.Xml.XmlException){} // An incomplete manual copy need not hide other props.
        }
        return result.OrderBy(p=>p.Name,StringComparer.CurrentCultureIgnoreCase).ThenBy(p=>p.ModelFile).ToArray();
    }
    private static UrdfPropEntry Entry(string root,string file,XDocument xml)
        =>new((string)xml.Root?.Attribute("name")??Path.GetFileNameWithoutExtension(file),
            ConfigurationPaths.ToRelative(root,file).Replace('\\','/'));

    public static UrdfPropEntry Import(string root,string source)
    {
        source=Path.GetFullPath(source);
        if(!Path.GetExtension(source).Equals(".urdf",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Choose a .urdf model.");
        var xml=XDocument.Load(source);
        if(xml.Root?.Name.LocalName!="robot"||!xml.Root.Elements("link").Elements("visual").Any())
            throw new InvalidDataException("The URDF needs a robot root and visible geometry.");
        string name=Path.GetFileNameWithoutExtension(source);
        if(string.IsNullOrWhiteSpace((string)xml.Root.Attribute("name")))xml.Root.SetAttributeValue("name",name);
        string library=Folder(root);
        var assets=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var attr in xml.Descendants().Where(e=>e.Name.LocalName is "mesh" or "texture").Attributes("filename"))
        {
            string reference=attr.Value;
            if(reference.Contains("://",StringComparison.Ordinal)&&!reference.StartsWith("file://",StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Use local mesh and texture files. Unresolved package or web references cannot be imported: "+reference);
            string asset=ConfigurationPaths.Resolve(Path.GetDirectoryName(source),reference);
            if(!File.Exists(asset))throw new FileNotFoundException("Referenced prop asset is missing.",asset);
            if(attr.Parent.Name.LocalName=="mesh"&&!new[]{".obj",".stl"}.Contains(Path.GetExtension(asset),StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("Prop meshes must be OBJ or STL: "+reference);
            if(!assets.TryGetValue(asset,out var stored))assets[asset]=stored=$"Assets/{assets.Count:D3}-{Path.GetFileName(asset)}";
            attr.Value=stored;
        }
        string leaf="Prop-"+(name.Length>55?name[..55]:name).TrimEnd(' ','.')+"-"+Guid.NewGuid().ToString("N")[..8];
        string staging=Path.Combine(library,".pending-"+Guid.NewGuid().ToString("N")),destination=Path.Combine(library,leaf);
        var written=new List<string>();
        try
        {
            Directory.CreateDirectory(Path.Combine(staging,"Assets"));
            foreach(var pair in assets)
            {
                string file=Path.Combine(staging,pair.Value);written.Add(file);File.Copy(pair.Key,file);
            }
            string model=Path.Combine(staging,"model.urdf");written.Add(model);xml.Save(model);
            Directory.Move(staging,destination);
            return Entry(root,Path.Combine(destination,"model.urdf"),xml);
        }
        finally
        {
            if(Directory.Exists(staging))
            {
                foreach(var file in written)File.Delete(file);
                if(Directory.Exists(Path.Combine(staging,"Assets")))Directory.Delete(Path.Combine(staging,"Assets"));
                Directory.Delete(staging);
            }
        }
    }
    private const string RemoteUrdf = """
<?xml version="1.0"?>
<robot name="One-button remote">
  <link name="remote">
    <visual name="housing">
      <origin xyz="0 0 0.009"/>
      <geometry><box size="0.14 0.055 0.018"/></geometry>
      <material name="charcoal"><color rgba="0.075 0.09 0.12 1"/></material>
    </visual>
    <visual name="button_bezel">
      <origin xyz="0.028 0 0.019"/>
      <geometry><cylinder radius="0.014" length="0.002"/></geometry>
      <material name="bezel"><color rgba="0.5 0.54 0.6 1"/></material>
    </visual>
    <visual name="single_button">
      <origin xyz="0.028 0 0.023"/>
      <geometry><cylinder radius="0.011" length="0.007"/></geometry>
      <material name="red"><color rgba="0.84 0.07 0.06 1"/></material>
    </visual>
  </link>
</robot>
""";
}
