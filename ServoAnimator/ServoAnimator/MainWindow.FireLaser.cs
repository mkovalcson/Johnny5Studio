using System.IO;
using System.Windows.Media;

namespace ServoAnimator;

public partial class MainWindow
{
    private ServoCommand _controllerFireCommand;
    private readonly System.Diagnostics.Stopwatch _controllerFireClock = new();
    // One player per bolt, round robin: a bolt never rewinds a sound that is still playing (seeking a playing
    // player fades it, so a bolt fired over the last one's tail came out quieter), and close bolts can overlap.
    private readonly MediaPlayer[] _fireSounds = Enumerable.Range(0, 4).Select(_ => new MediaPlayer()).ToArray();
    private readonly bool[] _fireSoundPlaying = new bool[4];
    private int _fireSoundNext;
    private string _fireSoundPath;
    private double _fireSoundShot = double.NaN;
    private bool _fireSoundHooked;

    /// <summary>Bolts in flight at the cursor. Runs after the body pose is applied, so new bolts leave the laser
    /// where it is now.</summary>
    private void ApplyFireLaserTimeline()
    {
        if (!PlaybackOutputAllowed(false)) return;
        if (_controllerFireCommand != null && !IsRunning) return;
        if (IsRunning) StopControllerFire();
        var shots = FireLaserTimeline.ShotsAt(_doc.Commands, _cursorTime);
        ForEachHeadView(view => view.SetLaserShots(shots));
        SyncFireSound(shots.OrderBy(s => s.Start).Cast<LaserShot?>().LastOrDefault(), _cursorTime, IsRunning);
    }

    /// <summary>Controller Fire Laser: the default burst as a visual preview with its sound, on its own clock.</summary>
    private void StartControllerFire()
    {
        if (!ControllerOutputAllowed(false)) return;
        _controllerFireCommand = new ServoCommand { Servo = ServoNames.FireLaser, TextValue = new FireLaserCommand().ToJson() };
        _controllerFireClock.Restart(); AdvanceControllerFire();
    }
    private void StopControllerFire()
    {
        if (_controllerFireCommand == null) return;
        _controllerFireCommand = null; _controllerFireClock.Reset();
        ForEachHeadView(v => v.SetLaserShots(Array.Empty<LaserShot>()));
        SyncFireSound(null, 0, false);
    }
    private void AdvanceControllerFire()
    {
        if (_controllerFireCommand == null || IsRunning) return;
        double time = _controllerFireClock.Elapsed.TotalSeconds;
        var shots = FireLaserTimeline.ShotsAt(new[] { _controllerFireCommand }, time);
        ForEachHeadView(v => v.SetLaserShots(shots));
        SyncFireSound(shots.OrderBy(s => s.Start).Cast<LaserShot?>().LastOrDefault(), time, true);
        if (time >= FireLaserCommand.Read(_controllerFireCommand.TextValue).Duration) StopControllerFire();
    }

    /// <summary>Each bolt's sound plays from the moment it fires while time runs; silent while paused or scrubbing.</summary>
    private void SyncFireSound(LaserShot? latest, double time, bool running)
    {
        if (!_fireSoundHooked)
        {
            _fireSoundHooked = true;
            for (int i = 0; i < _fireSounds.Length; i++)
            {
                int index = i;
                _fireSounds[i].MediaFailed += (_, e) => { ShowStatus("Fire Laser sound: " + e.ErrorException?.Message); _fireSoundPath = null; };
                _fireSounds[i].MediaEnded += (_, _) => _fireSoundPlaying[index] = false;
            }
        }
        string path = latest?.Settings.SoundFile(ConfigRoot);
        bool audible = running && path != null && File.Exists(path) && AnyViewCanFireLaser();
        if (!audible)
        {
            // Pause, not Stop: Stop rewinds, and the rewind can blip the start of the clip (its highest pitch).
            for (int i = 0; i < _fireSounds.Length; i++)
                if (_fireSoundPlaying[i]) { _fireSounds[i].Pause(); _fireSoundPlaying[i] = false; }
            _fireSoundShot = double.NaN;
            return;
        }
        var shot = latest.Value;
        if (!string.Equals(path, _fireSoundPath, StringComparison.OrdinalIgnoreCase))
        {
            _fireSoundPath = path; _fireSoundShot = double.NaN;
            foreach (var player in _fireSounds) player.Open(new Uri(path));
        }
        if (shot.Start == _fireSoundShot) return;                           // this bolt's sound is already playing
        _fireSoundShot = shot.Start;
        // Always from the top: frames arrive a little after each bolt fires, and seeking that far into a short
        // "pew" cuts its loud attack (a newly opened player ignored the seek, so only the first bolt was full).
        // Joining well after a bolt fired (playback started mid-burst) plays nothing for it.
        if (time - shot.Start > .1) return;
        int next = _fireSoundNext; _fireSoundNext = (next + 1) % _fireSounds.Length;
        var sound = _fireSounds[next];
        sound.Volume = shot.Settings.Volume / 100;
        sound.SpeedRatio = Math.Clamp(_playbackClock?.Rate ?? 1, .1, 4);
        sound.Position = TimeSpan.Zero;
        sound.Play(); _fireSoundPlaying[next] = true;
    }

    /// <summary>No sound when no view can show the laser firing (hidden, or the toolbox is out).</summary>
    private bool AnyViewCanFireLaser()
    {
        bool ready = false;
        ForEachHeadView(v => ready |= v.LaserReadyToFire);
        return ready;
    }

    /// <summary>insert_fire_laser: one Fire Laser command at the captured cursor, as one undo step.</summary>
    private object HandleFireLaserApi(EditorApiRequest request, string payload)
    {
        if (ApiUrdfView == null) throw new InvalidOperationException("Open a URDF view first.");
        var settings = new FireLaserCommand();
        if (request.Shots is int shots) settings.Shots = shots;
        if (request.Interval is double interval) settings.Interval = interval;
        if (request.Speed is double speed) settings.Speed = speed;
        if (request.Range is double range) settings.Range = range;
        if (request.Length is double length) settings.Length = length;
        if (request.Color != null) settings.Color = request.Color;
        if (request.Volume is double volume) settings.Volume = volume;
        if (!string.IsNullOrWhiteSpace(request.Sound))
        {
            if (FireLaserCommand.BuiltInSounds.ContainsKey(request.Sound) || request.Sound.Equals(FireLaserCommand.NoSound, StringComparison.OrdinalIgnoreCase))
                settings.Sound = request.Sound.ToLowerInvariant();
            else
            {
                string full = Path.IsPathRooted(request.Sound) ? request.Sound : Path.Combine(ConfigRoot, request.Sound);
                if (!File.Exists(full)) throw new InvalidOperationException("Fire Laser sound file not found: " + full);
                settings.Sound = ConfigPathService.IsWithin(ConfigRoot, full) ? Path.GetRelativePath(ConfigRoot, full) : full;
            }
        }
        settings.Validate();
        double end = _cursorTime + settings.Duration;
        if (end > 86400) throw new InvalidOperationException("The effect would end after the 24-hour timeline limit.");
        var command = new ServoCommand { Servo = ServoNames.FireLaser, TextValue = settings.ToJson(), OffsetSeconds = ServoCommand.TimeKey(_cursorTime),
            Reason = string.IsNullOrWhiteSpace(request.Description) ? settings.Label() : request.Description };
        if (CommandConflicts.Find(_doc.Commands.Append(command)).Count != 0) throw new InvalidOperationException("A command already occupies that target/time. Nothing changed.");
        var previousUndo = _undoStack.ToArray(); var previousRedo = _redoStack.ToArray();
        try { PushUndo("Codex: " + request.Method); _doc.Commands.Add(command); RefreshAfterEdit(); }
        catch
        {
            _doc.Commands.Remove(command);
            _undoStack.Clear(); _undoStack.AddRange(previousUndo);
            _redoStack.Clear(); _redoStack.AddRange(previousRedo); RefreshAfterEdit(); throw;
        }
        _apiEditGeneration++;
        object result = new { ok = true, requestId = request.RequestId, inserted = 1, text = command.TextValue, endSeconds = ServoCommand.TimeKey(end), state = EditorApiState() };
        _apiReceipts.Add(request.RequestId, (payload, result)); return result;
    }
}
