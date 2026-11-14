using System.IO;
using System.Windows;

namespace ServoAnimator;

public partial class MainWindow
{
    private BackdropCommand _sequenceBackground;
    private BackgroundLibraryItem _sequenceSceneItem;
    private string _sequenceBackgroundFile;
    private int _sequenceBackgroundRevision;
    private sealed record SceneViewState(RobotHeadView View, string Path, UrdfScene Model, UrdfFraming Frame, UrdfPresentationSettings TimelineBackground, UrdfStageSettings Stage = null);
    private readonly Dictionary<RobotHeadView, SceneViewState> _sequenceModelBaselines = new();
    private List<SceneViewState> CaptureSceneViews()
    {
        var states = new List<SceneViewState>();
        ForEachHeadView(v => states.Add(new(v, v.LoadedModelFile, v.CurrentModelScene, v.CaptureCurrentFraming(), v.CaptureTimelineBackground(), v.CaptureStage())));
        return states;
    }
    private void RestoreSceneViews(IEnumerable<SceneViewState> states)
    {
        foreach (var state in states)
        {
            if (state.Model != null && !ReferenceEquals(state.View.CurrentModelScene, state.Model))
                state.View.InstallModel(state.Path, state.Model, state.Frame);
            state.View.SetTimelineBackground(state.TimelineBackground); state.View.ApplyFraming(state.Frame);
            if (state.Stage != null)
            {
                state.View.ApplyStageCameraSettings(state.Stage);
                state.View.ApplyStageMotion(state.Stage.RobotX, state.Stage.RobotY, state.Stage.HeadingDegrees);
            }
        }
    }
    private bool CanEditBackground()
    {
        if (!_meetingActive && !RecordingController && !IsRunning) return true;
        ShowStatus("Stop playback, recording or Meeting before changing scenes."); return false;
    }
    private BackdropCommand CurrentBackground()
    {
        var view = ApiUrdfView;
        var background = view?.CaptureBackground() ?? _urdfConfig.Presentation.Clone();
        if (view?.CaptureStage() is UrdfStageSettings stage) background.Stage = stage;
        return new() { Background = background, Framing = view?.CaptureCurrentFraming() };
    }
    private string CurrentRobotModel => ApiUrdfView?.LoadedModelFile ?? UrdfModelSettings.Load(ConfigRoot).SelectedFile;
    private void ApplyLibraryScene(BackgroundLibraryItem item)
    {
        item.Settings.Validate(ConfigRoot);
        if (string.IsNullOrWhiteSpace(item.RobotModelFile) || !File.Exists(item.RobotModelFile))
            throw new InvalidDataException("Choose an existing robot URDF model for the scene.");
        string path = Path.GetFullPath(item.RobotModelFile);
        var before = CaptureSceneViews();
        // Parse every required scene before altering either viewport.
        var prepared = before.Select(s => (State: s, Model: string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase) ? s.Model : UrdfScene.Load(path))).ToArray();
        foreach (var s in before) s.View.PrepareBackdrop(item.Settings.Background);
        var oldPresentation = _urdfConfig.Presentation;
        try
        {
            foreach (var p in prepared)
            {
                var frame = item.Settings.Framing ?? p.State.Frame;
                if (!ReferenceEquals(p.Model, p.State.Model)) p.State.View.InstallModel(path, p.Model, frame);
                p.State.View.SetTimelineBackground(item.Settings.Background); p.State.View.ApplyFraming(frame);
            }
            var settings = UrdfModelSettings.Load(ConfigRoot);
            foreach (var old in before) if (!string.IsNullOrWhiteSpace(old.Path)) settings.Framing[old.Path] = old.Frame;
            settings.ModelFile = path; settings.ActiveSceneFile = item.FileName ?? "";
            settings.Framing[path] = ApiUrdfView?.CaptureCurrentFraming() ?? item.Settings.Framing ?? new();
            _urdfConfig.Presentation = item.Settings.Background.Clone();
            _urdfConfig.Save(Path.Combine(ConfigRoot, "URDFconfig.json")); settings.Save(ConfigRoot);
            ApplyUrdfConfigurationToViews();
            foreach (var p in prepared)
            {
                p.State.View.SetTimelineBackground(null); p.State.View.ApplyFraming(item.Settings.Framing ?? p.State.Frame); p.State.View.RememberSavedFraming();
            }
            _sequenceModelBaselines.Clear(); _timelineSceneModels.Clear(); _backdropTimeline.Clear(); _urdfTimelineBaselines.Clear();
            ResetControllerMotion(); _controllerPreview.Clear(); ClearCollisionCommandWarnings(); PushHeadPose();
        }
        catch { _urdfConfig.Presentation = oldPresentation; RestoreSceneViews(before); throw; }
    }
    private void ApplyLibraryBackground(BackgroundLibraryItem item)
    {
        var current = CurrentBackground();
        var scenery = BackgroundLibrary.SceneryOnly(item.Settings);
        // Preserve the robot's room position as well as its camera framing.
        if (current.Background.Stage != null && scenery.Background.Stage != null)
        {
            scenery.Background.Stage.RobotX = current.Background.Stage.RobotX;
            scenery.Background.Stage.RobotY = current.Background.Stage.RobotY;
            scenery.Background.Stage.HeadingDegrees = current.Background.Stage.HeadingDegrees;
        }
        scenery.Framing = current.Framing; scenery.Background.Stage ??= new();
        scenery.Background.Stage.RobotFraming = current.Framing;
        ApplyLibraryScene(new() { Name = item.Name, Settings = scenery, RobotModelFile = CurrentRobotModel });
        ShowStatus($"Loaded background: {item.Name}. Robot model and framing preserved.");
    }
    private void LibraryProps_Click(object sender, RoutedEventArgs e)
    {
        try { new UrdfPropLibraryWindow(ConfigRoot, null) { Owner = this }.ShowDialog(); }
        catch (Exception ex) { ShowStatus("Prop library: " + ex.Message); }
    }
    private void LibraryBackgrounds_Click(object sender, RoutedEventArgs e) => OpenSceneLibrary(false);
    private void LibraryScenes_Click(object sender, RoutedEventArgs e) => OpenSceneLibrary(true);
    private void OpenSceneLibrary(bool scenes)
    {
        if (!CanEditBackground()) return;
        try
        {
            var window = new BackgroundLibraryWindow(ConfigRoot, CurrentBackground(), scenes: scenes, robotModel: CurrentRobotModel) { Owner = this };
            if (window.ShowDialog() == true && window.Selected != null)
            {
                if (scenes) ApplyLibraryScene(window.Selected); else ApplyLibraryBackground(window.Selected);
            }
            _sequenceBackgroundFile = null;
        }
        catch (Exception ex) { ShowStatus("Scene/background library: " + ex.Message); }
    }
    private void AssignSequenceScene(BackgroundLibraryItem item)
    {
        PushUndo("Set sequence scene");
        _doc.UrdfBackgroundFile = item?.FileName ?? "";
        _sequenceBackgroundFile = null;
        ApplyBackdropTimeline(); UpdateDocumentStatusIndicators();
    }
    private void SequenceBackground_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEditBackground()) return;
        try
        {
            var window = new BackgroundLibraryWindow(ConfigRoot, CurrentBackground(), true, _doc.UrdfBackgroundFile, scenes: true, robotModel: CurrentRobotModel) { Owner = this };
            if (window.ShowDialog() != true) return;
            AssignSequenceScene(window.Selected);
            ShowStatus(window.Selected == null ? "Sequence scene cleared. Save the sequence to keep this change." :
                $"Sequence uses {window.Selected.Name}. Save the sequence to keep this assignment.");
        }
        catch (Exception ex) { ShowStatus("Sequence scene: " + ex.Message); }
    }
    private void RestoreSequenceModels()
    {
        RestoreSceneViews(_sequenceModelBaselines.Values); _sequenceModelBaselines.Clear();
    }
    private void RefreshSequenceBackground()
    {
        string file = _doc.UrdfBackgroundFile ?? "";
        if (_sequenceBackgroundFile != file)
        {
            RestoreSequenceModels();
            _sequenceBackgroundFile = file; _sequenceBackground = null; _sequenceSceneItem = null; _trackPath = null; _sequenceBackgroundRevision++;
            if (file.Length > 0)
            {
                try
                {
                    var item = BackgroundLibrary.Read(ConfigRoot, file);
                    _sequenceBackground = item.Settings;
                    if (!string.IsNullOrWhiteSpace(item.RobotModelFile)) _sequenceSceneItem = item;
                }
                catch (Exception ex) { ShowStatus("Sequence scene could not be loaded: " + ex.Message); }
            }
        }
        if (_sequenceSceneItem == null) return;
        // Also handles a second viewport opened after the sequence was selected.
        try
        {
            List<SceneViewState> missing = null;
            ForEachHeadView(v =>
            {
                if (!_sequenceModelBaselines.ContainsKey(v))
                    (missing ??= new()).Add(new(v, v.LoadedModelFile, v.CurrentModelScene, v.CaptureCurrentFraming(), v.CaptureTimelineBackground()));
            });
            if (missing == null) return;
            var ready = missing.Select(s => (State: s, Model: string.Equals(s.Path, _sequenceSceneItem.RobotModelFile, StringComparison.OrdinalIgnoreCase)
                ? s.Model : UrdfScene.Load(_sequenceSceneItem.RobotModelFile))).ToArray();
            foreach (var p in ready) p.State.View.PrepareBackdrop(_sequenceBackground.Background);
            foreach (var p in ready)
            {
                _sequenceModelBaselines[p.State.View] = p.State;
                if (!ReferenceEquals(p.Model, p.State.Model))
                    p.State.View.InstallModel(_sequenceSceneItem.RobotModelFile, p.Model, _sequenceBackground.Framing ?? p.State.Frame);
            }
            if (ready.Length > 0) { ClearCollisionCommandWarnings(); PushHeadPose(); }
        }
        catch (Exception ex)
        {
            RestoreSequenceModels(); _sequenceSceneItem = null; _sequenceBackground = null;
            ShowStatus("Sequence robot model could not be loaded: " + ex.Message);
        }
    }
}
