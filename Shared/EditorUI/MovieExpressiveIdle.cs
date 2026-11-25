using System.Diagnostics;

namespace ServoAnimator;

public partial class MainWindow
{
    private bool _movieIdleActive;
    private IdleMood _movieIdleMood;
    private readonly Stopwatch _movieIdleClock=new();
    private readonly Dictionary<ServoNames,double> _movieIdleBaseline=new();
    private readonly Dictionary<ServoNames,int> _movieIdleLastSent=new();
    private double _movieIdleNextFrame;

    private void MovieIdleRequested(int index,IdleMood mood)
    {
        if(index<0||index>=_movieItems.Count)return;
        bool runningHere=_movieIdleActive&&index==_moviePlaybackIndex;
        if(runningHere)EndMovieIdle();
        _movieItems[index].IdleMood=mood;
        if(mood!=IdleMood.None)_movieItems[index].IsLooping=false;
        RefreshMovieTimelineView();
        if(runningHere&&mood!=IdleMood.None)BeginMovieIdle(mood);
        ShowStatus(mood==IdleMood.None?"Expressive idle off":$"{mood} idle after sequence · Next advances");
    }

    private void BeginMovieIdle(IdleMood mood)
    {
        PausePlayback();
        _movieIdleMood=mood;
        _movieIdleBaseline.Clear();
        foreach(var name in ExpressiveIdle.Offsets(mood,0).Keys)
            _movieIdleBaseline[name]=_rows.First(row=>row.Servo==name).Value;
        _movieIdleLastSent.Clear();
        _movieIdleNextFrame=0;
        _movieIdleClock.Restart();
        _movieIdleActive=true;
        UrdfRenderLoop.Current.AddAnimation(RenderMovieIdle);
        MoviePlayButton.Content="▶ Next";
        PlayPauseBtn.Content="▶ Sequence";
        ShowStatus($"Waiting · {mood} idle · Next starts the next sequence");
    }

    private void RenderMovieIdle(TimeSpan frameTime)
    {
        if(!_movieIdleActive)return;
        double seconds=_movieIdleClock.Elapsed.TotalSeconds;
        if(seconds<_movieIdleNextFrame)return;
        _movieIdleNextFrame=seconds+.05;
        if(_moviePlaybackIndex<0||_moviePlaybackIndex>=_movieItems.Count||
            _movieItems[_moviePlaybackIndex].IdleMood!=_movieIdleMood)
        {EndMovieIdle();return;}
        if(!PlaybackOutputAllowed(false))return;
        var commands=new List<ServoCommand>();
        foreach(var (servo,offset) in ExpressiveIdle.Offsets(_movieIdleMood,seconds))
        {
            double value=ExpressiveIdle.Value(servo,_movieIdleBaseline[servo],offset);
            ForEachHeadView(view=>{PrepareDirectMotion(view,servo,null,ServoSpeed.NoChange);view.SetServo(servo,value);});
            int numeric=(int)Math.Round(value);
            if(_movieIdleLastSent.GetValueOrDefault(servo,int.MinValue)==numeric)continue;
            _movieIdleLastSent[servo]=numeric;
            commands.Add(new(){Servo=servo,NumericValue=numeric,Speed=ServoSpeed.NoChange});
        }
        if(commands.Count>0)PlayBackServoValues(commands.ToArray());
    }

    private void EndMovieIdle()
    {
        if(!_movieIdleActive)return;
        _movieIdleActive=false;
        _movieIdleClock.Stop();
        UrdfRenderLoop.Current.RemoveAnimation(RenderMovieIdle);
        foreach(var (servo,value) in _movieIdleBaseline)ForEachHeadView(view=>{PrepareDirectMotion(view,servo,null,ServoSpeed.NoChange);view.SetServo(servo,value);});
        if(MoviePlayButton!=null)MoviePlayButton.Content="▶ Movie";
    }
}
