using System.Text.Json.Serialization;

namespace ServoAnimator;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IdleMood { None, Attentive, Curious, Tired, Impatient }

public static class ExpressiveIdle
{
    public static IReadOnlyDictionary<ServoNames,double> Offsets(IdleMood mood,double seconds)
    {
        if(mood==IdleMood.None)return new Dictionary<ServoNames,double>();
        double fade=Math.Clamp(seconds/1.5,0,1);
        double Wave(double period)=>Math.Sin(seconds*2*Math.PI/period);
        double turn=0,nod=0,tilt=0,eyeX=0,eyeY=0,flaps=0;
        switch(mood)
        {
            case IdleMood.Attentive:
                turn=3*Wave(11);nod=2*Wave(7);eyeX=6*Wave(5);eyeY=3*Wave(8);
                break;
            case IdleMood.Curious:
                turn=7*Wave(9);tilt=8*Wave(13);eyeX=12*Wave(4);eyeY=5*Wave(7);flaps=5*(1-Math.Cos(seconds*.8));
                break;
            case IdleMood.Tired:
                nod=-7*(1-Math.Cos(seconds*.45));tilt=3*Wave(17);eyeY=-4*(1-Math.Cos(seconds*.4));flaps=-5*(1-Math.Cos(seconds*.6));
                break;
            case IdleMood.Impatient:
                turn=7*Wave(4);nod=3*Wave(2.7);eyeX=16*Wave(2.4);tilt=3*Wave(5);flaps=4*Wave(3.3);
                break;
        }
        return new Dictionary<ServoNames,double>
        {
            [ServoNames.NeckTurn]=fade*turn,[ServoNames.NeckNodUp]=fade*nod,
            [ServoNames.NeckTiltRight]=fade*tilt,[ServoNames.EyesHorizontalRight]=fade*eyeX,
            [ServoNames.EyesVerticalUp]=fade*eyeY,[ServoNames.FlapTiltUp]=fade*flaps
        };
    }

    public static IdleMood For(MovieDocument movie,int index)=>
        index<movie.SequenceIdles.Count&&Enum.IsDefined(movie.SequenceIdles[index])?movie.SequenceIdles[index]:IdleMood.None;

    public static double Value(ServoNames servo,double baseline,double offset)
    {
        var range=ControlDefinitions.RangeFor(servo);
        return Math.Clamp(baseline+offset,range.Min,range.Max);
    }
}
