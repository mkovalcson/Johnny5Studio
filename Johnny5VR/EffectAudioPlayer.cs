using NAudio.Wave;
namespace Johnny5VR;

internal sealed class EffectAudioPlayer:IDisposable
{
    private readonly List<(AudioFileReader Reader,WaveOutEvent Output)> sounds=[];
    private EffectCue[]? previous;
    private double last=-1;
    private bool wasRunning;
    internal void Step(EffectCue[] cues,double time,bool running,bool visible,string config)
    {
        if(running&&!wasRunning)last=time-.1;wasRunning=running;
        if(!ReferenceEquals(previous,cues)||time<last){Dispose();last=time-.1;previous=cues;}
        foreach(var p in sounds.Where(s=>s.Output.PlaybackState==PlaybackState.Stopped).ToArray()){p.Output.Dispose();p.Reader.Dispose();sounds.Remove(p);}
        if(!running||!visible){Dispose();last=time;return;}
        foreach(var cue in cues.Where(c=>c.Servo=="FireLaser"&&c.Time<=time))
        {
            var settings=LaserSettings.Read(cue.Text);
            for(int i=0;i<settings.Shots;i++)
            {
                double start=cue.Time+i*settings.Interval;if(start<=last||start>time||time-start>.1||settings.Sound=="none")continue;
                string file=settings.Sound switch{"default"=>"Library/Audio/Laser_Fire.wav","medium"=>"Library/Audio/Laser_Medium.wav","heavy"=>"Library/Audio/Laser_Heavy.wav",_=>settings.Sound};
                string path=LibraryJson.Resolve(config,file);if(!File.Exists(path))continue;
                if(sounds.Count>=8){var first=sounds[0];first.Output.Stop();first.Output.Dispose();first.Reader.Dispose();sounds.RemoveAt(0);}
                AudioFileReader? reader=null;WaveOutEvent? output=null;
                try{reader=new(path){Volume=(float)settings.Volume/100};output=new(){DesiredLatency=60};output.Init(reader);output.Play();sounds.Add((reader,output));}
                catch{output?.Dispose();reader?.Dispose();throw;}
            }
        }
        last=time;
    }
    public void Dispose(){foreach(var p in sounds){p.Output.Stop();p.Output.Dispose();p.Reader.Dispose();}sounds.Clear();}
}
