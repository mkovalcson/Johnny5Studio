using System.Numerics;
using System.Text.Json;

namespace Johnny5VR;

internal sealed partial class AnimationRig
{
    internal TreadDefinition? Treads {get;private set;}
    internal double TreadGauge {get;private set;}=.42;
    internal double CasterRadius {get;private set;}=.5;
    internal double CasterTrail {get;private set;}
    internal double MouthGain {get;private set;}=1;
    internal double EyeIntensity {get;private set;}=1;
    internal double VentIntensity {get;private set;}=1;
    internal int[] TreadPhases {get;}=new int[2];
    internal double[] TreadDistances {get;}=new double[2];
    private Vector3 casterPivot;
    private string[] casterLinks=[];
    private readonly Dictionary<string,(Vector3 Centre,double Radius)> casterWheels=[];
    private void InitializeSecondary(string file,string config)
    {
        Treads=TreadDefinition.Load(file);
        if(rest.TryGetValue("body_track_front_roller_left_link",out var l)&&rest.TryGetValue("body_track_front_roller_right_link",out var r))TreadGauge=Vector3.Distance(l.Translation,r.Translation);
        if(rest.TryGetValue("body_caster_yoke_link",out var yoke))
        {
            casterPivot=yoke.Translation;CasterRadius=Math.Max(.01,Math.Abs(.123658047-casterPivot.X));
            if(rest.TryGetValue("body_caster_axle_link",out var axle))
            {
                double trail=Vector2.Distance(new(casterPivot.X,casterPivot.Y),new(axle.M41,axle.M42));CasterTrail=trail>=.005?trail:0;
            }
            casterLinks=links.Where(l=>l.StartsWith("body_caster_")&&new[]{"yoke","fork","axle","wheel","hub"}.Any(part=>l[12..].StartsWith(part))).ToArray();
            var model=System.Xml.Linq.XDocument.Load(file).Root!;
            foreach(string side in new[]{"l","r"})
            {
                string name=$"body_caster_wheel_{side}_link";
                if(!rest.TryGetValue(name,out var wheel))continue;
                var cylinder=model.Elements("link").First(l=>(string?)l.Attribute("name")==name).Element("visual")?.Element("geometry")?.Element("cylinder");
                double radius=cylinder==null ? .075 : UrdfReader.Number(cylinder,"radius");
                casterWheels[side]=(wheel.Translation,Math.Max(.001,radius));
            }
        }
        MouthGain = rigConfiguration.AudioLedGain;
        EyeIntensity = rigConfiguration.EyeLightIntensity;
        VentIntensity = rigConfiguration.VentLightIntensity;
    }
    private Dictionary<string,Matrix4x4> SecondaryTransforms(double turn,TravelState travel)
    {
        var belts=ServoAnimator.DifferentialDrive.BeltTravel(travel.Forward,turn,TreadGauge);TreadDistances[0]=belts.Left;TreadDistances[1]=belts.Right;
        var transforms=new Dictionary<string,Matrix4x4>();
        if(Treads!=null)
        {
            for(int side=0;side<2;side++)TreadPhases[side]=Treads.Phase(TreadDistances[side]);
            foreach(var rotor in Treads.Rotors)
            {
                double phase=-TreadDistances[rotor.Side]/Treads.Step;
                var rotation=Matrix4x4.CreateTranslation(-rotor.Centre)*Matrix4x4.CreateFromAxisAngle(rotor.Axis,(float)((phase*rotor.DegreesPerPhase%360)*Math.PI/180))*Matrix4x4.CreateTranslation(rotor.Centre);
                foreach(string link in rotor.Links)transforms[link]=rotation;
            }
        }
        var swivel=Matrix4x4.CreateTranslation(-casterPivot)*Matrix4x4.CreateRotationZ((float)(travel.Caster*Math.PI/180))*Matrix4x4.CreateTranslation(casterPivot);
        foreach(string link in casterLinks)
        {
            var matrix=Matrix4x4.Identity;
            string part=link[12..];
            if(part.StartsWith("wheel")||part.StartsWith("hub"))
                foreach(var pair in casterWheels)
                    if(part.Contains("_"+pair.Key+"_",StringComparison.Ordinal))
                    {
                        var wheel=pair.Value;double distance=pair.Key=="l"?travel.CasterLeft:travel.CasterRight;
                        matrix=Matrix4x4.CreateTranslation(-wheel.Centre)*Matrix4x4.CreateRotationY((float)(distance/wheel.Radius%(2*Math.PI)))*Matrix4x4.CreateTranslation(wheel.Centre);break;
                    }
            transforms[link]=matrix*swivel;
        }
        return transforms;
    }
}
