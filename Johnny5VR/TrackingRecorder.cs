using System.Numerics;
using ServoAnimator;
namespace Johnny5VR;

// Records the raw tracking frames (headset, both controllers, finger curls) while Follow
// runs, to Diagnostics/tracking-<date-time>.bin, so a session can be replayed through the
// body solver offline (Johnny5VR.exe --replay-tracking <file>). About 190 bytes per frame.
internal sealed class TrackingRecorder:IDisposable
{
    private const string Magic="J5TRK1";
    private readonly BinaryWriter writer;
    private string lastSource="";
    internal string Path {get;}
    internal int Frames {get;private set;}
    internal TrackingRecorder(string folder,string prefix="tracking")
    {
        Directory.CreateDirectory(folder);
        Path=System.IO.Path.Combine(folder,$"{prefix}-{DateTime.Now:yyyyMMdd-HHmmss}.bin");
        writer=new(new FileStream(Path,FileMode.CreateNew,FileAccess.Write,FileShare.Read,1<<16));
        writer.Write(Magic);
    }
    internal void Record(BodyTrackingFrame frame,float seconds)
    {
        writer.Write(seconds);writer.Write(frame.Valid);
        Write(frame.Head);Write(frame.Left);Write(frame.Right);
        Write(frame.LeftFingers);Write(frame.RightFingers);
        string source=frame.FingerSource??"";
        if(source!=lastSource){writer.Write(true);writer.Write(source);lastSource=source;}else writer.Write(false);
        if(++Frames%75==0)writer.Flush();
    }
    private void Write(Matrix4x4 m){foreach(float f in new[]{m.M11,m.M12,m.M13,m.M21,m.M22,m.M23,m.M31,m.M32,m.M33,m.M41,m.M42,m.M43})writer.Write(f);}
    private void Write(float[]? values){bool has=values?.Length==5;writer.Write(has);if(has)foreach(float v in values!)writer.Write(v);}
    public void Dispose(){try{writer.Flush();writer.Dispose();}catch(IOException){}}
    internal static IEnumerable<(float Seconds,BodyTrackingFrame Frame)> Read(string path)
    {
        using var reader=new BinaryReader(File.OpenRead(path));
        if(reader.ReadString()!=Magic)throw new InvalidDataException("Not a tracking recording.");
        string source="";
        while(reader.BaseStream.Position<reader.BaseStream.Length)
        {
            float seconds=reader.ReadSingle();bool valid=reader.ReadBoolean();
            var head=ReadMatrix(reader);var left=ReadMatrix(reader);var right=ReadMatrix(reader);
            var leftFingers=ReadFingers(reader);var rightFingers=ReadFingers(reader);
            if(reader.ReadBoolean())source=reader.ReadString();
            yield return(seconds,new(head,left,right,leftFingers,rightFingers,valid,source));
        }
    }
    private static Matrix4x4 ReadMatrix(BinaryReader r)=>new(r.ReadSingle(),r.ReadSingle(),r.ReadSingle(),0,r.ReadSingle(),r.ReadSingle(),r.ReadSingle(),0,r.ReadSingle(),r.ReadSingle(),r.ReadSingle(),0,r.ReadSingle(),r.ReadSingle(),r.ReadSingle(),1);
    private static float[]? ReadFingers(BinaryReader r){if(!r.ReadBoolean())return null;var v=new float[5];for(int i=0;i<5;i++)v[i]=r.ReadSingle();return v;}

    // Replays a recording through the solver with a calibration profile and writes one CSV
    // row per frame: time, tracking validity, confidence and every target the solver produced.
    internal static string Replay(string recording,string calibration,string output)
    {
        var profile=BodyTrackingCalibration.Load(calibration);var solver=new BodyTrackingSolver(profile);
        var rows=new List<(float Seconds,bool Valid,BodyTrackingResult? Result)>();bool centred=false;int dropouts=0;bool wasValid=true;
        foreach(var (seconds,frame) in Read(recording))
        {
            if(!frame.TrackingValid){rows.Add((seconds,false,null));if(wasValid)dropouts++;wasValid=false;continue;}
            wasValid=true;
            if(!centred){solver.Recenter(frame,0,0,0);centred=true;}
            rows.Add((seconds,true,solver.Solve(frame,seconds)));
        }
        var keys=rows.Where(r=>r.Result!=null).SelectMany(r=>r.Result!.Targets.Keys).Distinct().OrderBy(k=>k,StringComparer.Ordinal).ToArray();
        using(var w=new StreamWriter(output))
        {
            w.WriteLine("seconds,valid,confidence,"+string.Join(",",keys));
            double t=0;
            foreach(var r in rows)
            {
                t+=r.Seconds;
                w.Write(System.FormattableString.Invariant($"{t:F4},{(r.Valid?1:0)},\"{r.Result?.Confidence??""}\""));
                foreach(string key in keys)w.Write(r.Result!=null&&r.Result.Targets.TryGetValue(key,out float v)?System.FormattableString.Invariant($",{v:F5}"):",");
                w.WriteLine();
            }
        }
        double duration=rows.Sum(r=>r.Seconds);
        return $"{rows.Count} frames, {duration:F1} s, {rows.Count(r=>!r.Valid)} invalid frames in {dropouts} dropouts, {keys.Length} targets → {output}";
    }
}
