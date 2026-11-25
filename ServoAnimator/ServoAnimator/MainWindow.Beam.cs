using System.IO;
using System.Windows.Media;

namespace ServoAnimator;

public partial class MainWindow
{
    private ServoCommand _controllerBeamCommand;
    private readonly System.Diagnostics.Stopwatch _controllerBeamClock = new();
    private void StartControllerBeam(bool beamIn)
    {
        if (!ControllerOutputAllowed(false)) return;
        // Direct controller preview is visual only. Timeline Beam commands retain their sound/RGB settings.
        var settings = new BeamCommand { Direction = beamIn ? BeamDirection.In : BeamDirection.Out, Sound = BeamCommand.NoSound, Rgb = false };
        _controllerBeamCommand = new ServoCommand { Servo = ServoNames.UrdfBeam, TextValue = settings.ToJson() };
        _controllerBeamClock.Restart(); AdvanceControllerBeam();
    }
    private void StopControllerBeam()
    {
        if (_controllerBeamCommand == null) return;
        _controllerBeamCommand = null; _controllerBeamClock.Reset();
        ForEachHeadView(v => v.SetTimelineBeam(BeamState.Visible, 0));
    }
    private void AdvanceControllerBeam()
    {
        if (_controllerBeamCommand == null || IsRunning) return;
        double time = _controllerBeamClock.Elapsed.TotalSeconds;
        var state = BeamTimeline.StateAt(new[] { _controllerBeamCommand }, time);
        ForEachHeadView(v => v.SetTimelineBeam(state, time));
        if (!state.Active) { _controllerBeamClock.Stop(); _controllerBeamCommand = null; }
    }

    private readonly MediaPlayer _beamSound = new();
    private string _beamSoundPath;
    private double? _beamSoundDuration;
    private bool _beamSoundPlaying, _beamSoundHooked;
    private double _beamSoundCueStart = double.NaN;

    private readonly PlaybackFrameCadence _beamFrameCadence = new(60, earlyToleranceSeconds: .001);
    private bool _refreshBeamThisFrame;
    private ServoCommand[] _beamPlaybackSource;
    private (double Time, BeamCommand Settings)[] _beamPlaybackCues = Array.Empty<(double, BeamCommand)>();

    private void ApplyBeamTimeline(bool syncSound = true)
    {
        if (_controllerBeamCommand != null && !IsRunning) return;
        if (IsRunning) StopControllerBeam();
        BeamState state;
        if (IsRunning)
        {
            if (!ReferenceEquals(_beamPlaybackSource, _orderedCommands))
            {
                _beamPlaybackSource = _orderedCommands;
                _beamPlaybackCues = BeamTimeline.Prepare(_orderedCommands);
            }
            if (_beamPlaybackCues.Length == 0 && !syncSound) return;
            state = BeamTimeline.PreparedStateAt(_beamPlaybackCues, _cursorTime);
        }
        else state = BeamTimeline.StateAt(_doc.Commands, _cursorTime);
        ForEachHeadView(view => view.SetTimelineBeam(state, _cursorTime));
        if (syncSound) SyncBeamSound(state);
    }

    /// <summary>The latest beam's sound plays from its command time while the timeline runs (following the playback
    /// speed), re-syncing on drift; it is silent while paused, scrubbing, or outside the sound.</summary>
    private void SyncBeamSound(BeamState state)
    {
        if (!_beamSoundHooked)
        {
            _beamSoundHooked = true;
            _beamSound.MediaOpened += (_, _) => _beamSoundDuration = _beamSound.NaturalDuration.HasTimeSpan ? _beamSound.NaturalDuration.TimeSpan.TotalSeconds : null;
            _beamSound.MediaFailed += (_, e) => { ShowStatus("URDF Beam sound: " + e.ErrorException?.Message); _beamSoundPath = null; };
        }
        string path = state.Settings?.SoundFile(ConfigRoot);
        double offset = _cursorTime - state.StartSeconds;
        bool sameFile = string.Equals(path, _beamSoundPath, StringComparison.OrdinalIgnoreCase);
        double length = (sameFile ? _beamSoundDuration : null) ?? (state.Settings?.Seconds ?? 0) + 2;
        bool inWindow = path != null && state.Settings.Volume > 0 && File.Exists(path) && offset >= 0 && offset < length;
        if (!inWindow || !IsRunning)
        {
            if (_beamSoundPlaying) { _beamSound.Pause(); _beamSoundPlaying = false; }
            return;
        }
        if (!string.Equals(path, _beamSoundPath, StringComparison.OrdinalIgnoreCase))
        {
            _beamSoundPath = path; _beamSoundDuration = null; _beamSoundPlaying = false;
            _beamSound.Open(new Uri(path));
        }
        _beamSound.Volume = state.Settings.Volume / 100;
        _beamSound.SpeedRatio = Math.Clamp(_playbackClock?.Rate ?? 1, .1, 4);
        var target = TimeSpan.FromSeconds(offset);
        if (!_beamSoundPlaying || _beamSoundCueStart != state.StartSeconds)
        {
            _beamSound.Position = target;
            _beamSound.Play();
            _beamSoundPlaying = true;
            _beamSoundCueStart = state.StartSeconds;
        }
        else if (Math.Abs((_beamSound.Position - target).TotalSeconds) > .15) _beamSound.Position = target;
    }

    /// <summary>RGB ring commands implied by beam commands (for hardware playback, the ring preview and exports).</summary>
    internal static ServoCommand BeamRgbCommand(ServoCommand beam)
    {
        if (beam.Servo != ServoNames.UrdfBeam || beam.Disable) return null;
        string text;
        try { text = BeamCommand.Read(beam.TextValue).RgbText(); } catch (Exception) { return null; }
        return text == null ? null : new ServoCommand { Servo = ServoNames.RGBCommand, TextValue = text, OffsetSeconds = beam.OffsetSeconds, Speed = ServoSpeed.NoChange, Reason = "URDF Beam ring sparkle" };
    }

    private object HandleBeamApi(EditorApiRequest request, string payload)
    {
        if (ApiUrdfView == null) throw new InvalidOperationException("Open a URDF view first.");
        var settings = new BeamCommand
        {
            Direction = string.IsNullOrWhiteSpace(request.Direction) ? BeamDirection.In
                : Enum.TryParse<BeamDirection>(request.Direction, true, out var d) ? d : throw new InvalidOperationException("direction must be in or out."),
            Seconds = request.Seconds ?? 3.5, Particles = request.Particles ?? true, Volume = request.Volume ?? 100, Rgb = request.Rgb ?? true,
        };

        settings.Validate();
        double end = _cursorTime + settings.Seconds;
        if (end > 86400) throw new InvalidOperationException("The effect would end after the 24-hour timeline limit.");
        var command = new ServoCommand { Servo = ServoNames.UrdfBeam, OffsetSeconds = ServoCommand.TimeKey(_cursorTime),
            Reason = string.IsNullOrWhiteSpace(request.Description) ? settings.Label() : request.Description };
        if (CommandConflicts.Find(_doc.Commands.Append(command)).Count != 0) throw new InvalidOperationException("A command already occupies that target/time. Nothing changed.");
        if (!string.IsNullOrWhiteSpace(request.Sound)) settings.Sound = BeamCommand.ImportSound(ConfigRoot, request.Sound);
        command.TextValue = settings.ToJson();
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
