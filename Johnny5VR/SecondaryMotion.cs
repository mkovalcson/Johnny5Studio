using System.Numerics;
using System.Text.Json;

namespace Johnny5VR;

internal sealed record TreadLayerDefinition(int Side,string Link,string Visual,string[] Frames);
internal sealed record TreadRotorDefinition(int Side,string[] Links,Vector3 Axis,Vector3 Centre,double DegreesPerPhase);
internal sealed class TreadDefinition
{
    internal int Phases;
    internal double Step;
    internal readonly Dictionary<string,TreadLayerDefinition> Layers=[];
    internal readonly List<TreadRotorDefinition> Rotors=[];
    internal static TreadDefinition? Load(string model)
    {
        string folder=Path.GetDirectoryName(model)!,manifest=Path.Combine(folder,"Meshes","Body","Treads","tread_animation.json");
        if(!File.Exists(manifest))return null;
        using var document=JsonDocument.Parse(File.ReadAllText(manifest));var root=document.RootElement;
        var result=new TreadDefinition{Phases=root.GetProperty("phases").GetInt32(),Step=root.GetProperty("phaseStep_m").GetDouble()};
        if(result.Phases<2||result.Phases>240||!double.IsFinite(result.Step)||result.Step<=0)throw new InvalidDataException("Invalid tread animation manifest.");
        for(int side=0;side<2;side++)
        {
            var definition=root.GetProperty("sides").GetProperty(side==0?"left":"right");
            foreach(var layer in definition.GetProperty("layers").EnumerateArray())
            {
                var frames=layer.GetProperty("frames").EnumerateArray().Select(p=>LibraryJson.Resolve(folder,p.GetString()!)).ToArray();
                if(frames.Length!=result.Phases||frames.Any(f=>!File.Exists(f)))throw new InvalidDataException("Tread animation files are missing. Copy the model's complete Meshes/Body/Treads folder.");
                string name=layer.GetProperty("urdfVisual").GetString()!;
                result.Layers.Add(name,new(side,layer.GetProperty("urdfLink").GetString()!,name,frames));
            }
            foreach(var rotor in definition.Get("rotators").Items())
            {
                Vector3 Vector(string key){var a=rotor.GetProperty(key).EnumerateArray().Select(v=>v.GetSingle()).ToArray();if(a.Length!=3)throw new InvalidDataException("Invalid tread rotor vector.");return new(a[0],a[1],a[2]);}
                var axis=Vector("axisWorld");if(axis.LengthSquared()<1e-12)throw new InvalidDataException("Invalid tread rotor axis.");
                result.Rotors.Add(new(side,rotor.GetProperty("urdfLinks").EnumerateArray().Select(v=>v.GetString()!).ToArray(),Vector3.Normalize(axis),Vector("centreWorld"),rotor.GetProperty("degreesPerPhase").GetDouble()));
            }
        }
        return result;
    }
    internal int Phase(double forward)=>((int)(Math.Floor(-forward/Step)%Phases)+Phases)%Phases;
}

internal readonly record struct TravelState(double X,double Y,double Forward,double Caster,double CasterTarget,double Heading,double CasterLeft=0,double CasterRight=0);

// Same 20 ms midpoint integration and exact command boundaries as the editor.
// Cached 100 ms checkpoints make backward seeks deterministic and cheap.
internal sealed class SecondaryMotionPath
{
    private readonly ServoAnimator.GroundTravelPath path;
    internal SecondaryMotionPath(PlaybackSequence sequence,Dictionary<string,(double Value,double Stamp,string Servo)> pose,double radius,double trail,double halfWidth=.034875)
    {
        var baseline=pose.Where(p=>p.Key.StartsWith("servo:")).ToDictionary(p=>p.Key[6..],p=>p.Value.Value);
        var tracks=sequence.Tracks.Where(t=>t.Control=="").GroupBy(t=>t.Servo).ToDictionary(g=>g.Key,g=>g.First());
        double Value(ServoAnimator.ServoNames servo,double time) => tracks.GetValueOrDefault(servo.ToString())?.At(time)?.Value
            ?? (servo==ServoAnimator.ServoNames.TrackForward?0:baseline.GetValueOrDefault(servo.ToString()));
        var events=sequence.Tracks.Where(t=>t.Servo is "TrackForward" or "URDFStageHeading" or "URDFSpin" or "URDFRotate").SelectMany(t=>t.Points.Select(p=>p.Time));
        path=new(Value,events,0,Value(ServoAnimator.ServoNames.URDFStageHeading,0)+Value(ServoAnimator.ServoNames.URDFSpin,0),radius,trail,halfWidth,boundaryTolerance:1e-9);
    }
    internal TravelState At(double seconds)
    {
        var p=path.At(seconds);
        return new(p.X,p.Y,p.Forward,p.Caster,p.CasterTarget,p.LastHeading,p.WheelLeft,p.WheelRight);
    }
}
