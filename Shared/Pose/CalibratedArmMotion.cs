using System.Numerics;
namespace ServoAnimator;
public static class CalibratedArmMotion
{
    public static Vector3 RotationVector(Quaternion q)
    {
        q=Quaternion.Normalize(q);if(q.W<0)q=new(-q.X,-q.Y,-q.Z,-q.W);
        var v=new Vector3(q.X,q.Y,q.Z);float length=v.Length();
        return length<1e-6f?v*2:v*(2*MathF.Atan2(length,q.W)/length);
    }
    // Two endpoints count as opposite when their rotation directions are at least
    // OppositeMinimumDegrees apart (they need not be exactly opposite: the axis is the
    // difference of the two). A learned roll axis may be up to AxisSimilarityMaximum
    // (cosine) similar to the bend axis; the Wrist() mapping separates them from there.
    public const float MinimumEndpointRadians=.17f,OppositeMinimumDot=-.25f,AxisSimilarityMaximum=.8f;
    public static readonly float OppositeMinimumDegrees=MathF.Acos(OppositeMinimumDot)*180/MathF.PI;
    public static (Vector3 Axis,float[] Range) Learn(Quaternion positive,Quaternion negative)
    {
        var a=RotationVector(positive);var b=RotationVector(negative);
        if(a.Length()<MinimumEndpointRadians||b.Length()<MinimumEndpointRadians||Vector3.Dot(Vector3.Normalize(a),Vector3.Normalize(b))>OppositeMinimumDot)
            throw new InvalidDataException("Move to opposite sides of a straight wrist, at least 10 degrees each way. Use Back to repeat the first endpoint if needed.");
        var axis=Vector3.Normalize(a-b);return(axis,[Vector3.Dot(b,axis),Vector3.Dot(a,axis)]);
    }
    public static (float Pitch,float Roll) Wrist(BodyTrackingCalibration p,bool left,Quaternion delta)
    {
        if(p.ArmCalibrationVersion==0)return(BodyTrackingCalibration.Twist(delta,Vector3.UnitX),BodyTrackingCalibration.Twist(delta,-Vector3.UnitZ));
        var pitch=left?p.LeftPitchAxis:p.RightPitchAxis;var roll=left?p.LeftRollAxis:p.RightRollAxis;
        var v=RotationVector(delta);float cross=Vector3.Dot(pitch,roll),denom=Math.Max(.1f,1-cross*cross);
        float a=Vector3.Dot(v,pitch),b=Vector3.Dot(v,roll);
        return((a-cross*b)/denom,(b-cross*a)/denom);
    }
    public static float ArmAzimuth(Vector3 shoulder,Vector3 hand,bool left)=>(left?-1:1)*MathF.Atan2(hand.X-shoulder.X,-(hand.Z-shoulder.Z));
    public static float ArmTwist(BodyTrackingCalibration p,bool left,Vector3 hand,float bend)
    {
        if(p.ArmCalibrationVersion==0||bend<.26f)return 0;
        var shoulder=left?p.LeftShoulder:p.RightShoulder;var range=left?p.LeftTwist:p.RightTwist;
        float delta=BodyTrackingFrame.Wrap(ArmAzimuth(shoulder,hand,left)-(left?p.LeftTwistNeutral:p.RightTwistNeutral));
        return Math.Clamp(delta/(delta<0?-range[0]:range[1]),-1,1)*1.57f;
    }
    public static bool Valid(BodyTrackingCalibration p)
    {
        if(p.ArmCalibrationVersion==0)return true;if(p.ArmCalibrationVersion!=1)return false;
        bool Axis(Vector3 v)=>float.IsFinite(v.LengthSquared())&&Math.Abs(v.Length()-1)<.01f;
        return new[]{p.LeftPitchAxis,p.RightPitchAxis,p.LeftRollAxis,p.RightRollAxis}.All(Axis)
            &&Math.Abs(Vector3.Dot(p.LeftPitchAxis,p.LeftRollAxis))<=AxisSimilarityMaximum+.01f&&Math.Abs(Vector3.Dot(p.RightPitchAxis,p.RightRollAxis))<=AxisSimilarityMaximum+.01f
            &&new[]{p.LeftTwist,p.RightTwist}.All(r=>r?.Length==2&&r.All(float.IsFinite)&&r[0]<-.1f&&r[1]>.1f)
            &&float.IsFinite(p.LeftTwistNeutral)&&float.IsFinite(p.RightTwistNeutral);
    }
}
