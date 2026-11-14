namespace ServoAnimator;

internal static class VrMotionChecks
{
    internal static void Run(string report)
    {
        int count=0;
        void Check(bool valid,string message){if(!valid)throw new InvalidOperationException(message);count++;}
        TrackForwardPath Path(params ServoCommand[] commands)=>new(commands,0,0,null,null,0,0,null,null,.573765047,.095);
        ServoCommand Command(ServoNames servo,double time,int value)=>new(){Servo=servo,OffsetSeconds=time,NumericValue=value};
        var path=Path(Command(ServoNames.TrackForward,0,20),Command(ServoNames.TrackForward,1,0));
        var end=path.CasterAt(1);
        Check(Math.Abs(end.Left-.2)<1e-9&&end.Left==end.Right,"VR editor straight caster rolling distance.");
        Check(path.CasterAt(2)==end,"VR editor stopped wheels do not drift.");
        Check(path.CasterAt(.25)==Path(Command(ServoNames.TrackForward,0,20),Command(ServoNames.TrackForward,1,0)).CasterAt(.25),"VR editor backward seeks restore wheel positions.");
        var reverse=Path(Command(ServoNames.TrackForward,0,-20)).CasterAt(.02);
        Check(reverse.Left<0&&reverse.Right<0,"VR editor reverse starts with reverse wheel rotation.");
        var spin=Path(Command(ServoNames.URDFSpin,0,0),Command(ServoNames.URDFSpin,.5,90)).CasterAt(1);
        Check(Math.Abs(spin.Left-spin.Right)>.001,"VR editor caster wheels follow separate turning distances.");
        var waveform=new WaveformView{Duration=100};waveform.Measure(new System.Windows.Size(800,200));waveform.Arrange(new System.Windows.Rect(0,0,800,200));
        waveform.SetViewStart(10);double anchor=waveform.TimeAtX(350);waveform.NavigateVr(350,0,1);
        Check(Math.Abs(waveform.TimeAtX(350)-anchor)<1e-9,"Waveform VR zoom stays anchored to the pointed time.");
        double start=waveform.ViewStart;waveform.NavigateVr(350,1,0);Check(waveform.ViewStart>start,"VR horizontal stick pans the waveform.");
        var movie=new MovieTimelineView();movie.Measure(new System.Windows.Size(800,100));movie.Arrange(new System.Windows.Rect(0,0,800,100));
        movie.SetItems(new[]{new MovieSequenceItem{DurationSeconds=30},new MovieSequenceItem{DurationSeconds=40},new MovieSequenceItem{DurationSeconds=30}});
        movie.ZoomBy(4);movie.PanTo(10);anchor=movie.TimeAtX(350);movie.NavigateVr(350,0,1);
        Check(Math.Abs(movie.TimeAtX(350)-anchor)<1e-8,"Movie VR zoom stays anchored to the pointed time.");
        start=movie.ViewStart;movie.NavigateVr(350,1,0);Check(movie.ViewStart>start,"VR horizontal stick pans the movie timeline.");
        System.IO.File.WriteAllText(report,$"PASS: {count} VR editor caster and pointer-anchored timeline navigation checks.");
    }
}
