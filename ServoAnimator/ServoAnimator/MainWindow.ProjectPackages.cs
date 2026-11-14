using System.IO;
using System.Windows;
using Microsoft.Win32;
using NAudio.Wave;

namespace ServoAnimator;

public partial class MainWindow
{
    private void ExportMovieZip_Click(object sender, RoutedEventArgs e) => ExportProjectZip(ProjectPackageKind.Movie);
    private void ExportSequenceZip_Click(object sender, RoutedEventArgs e) => ExportProjectZip(ProjectPackageKind.Sequence);
    private void ExportUrdfZip_Click(object sender, RoutedEventArgs e) => ExportProjectZip(ProjectPackageKind.UrdfModel);
    private void ImportMovieZip_Click(object sender, RoutedEventArgs e) => ImportProjectZip(ProjectPackageKind.Movie);
    private void ImportSequenceZip_Click(object sender, RoutedEventArgs e) => ImportProjectZip(ProjectPackageKind.Sequence);
    private void ImportUrdfZip_Click(object sender, RoutedEventArgs e) => ImportProjectZip(ProjectPackageKind.UrdfModel);
    private bool CanUseProjectPackages()
    {
        if (!CanEditBackground()) return false;
        if (_recordingWindow != null || _controllerConfigOpen || _speedCalibrationBusy)
        { ShowStatus("Close recording and controller/calibration windows before importing or exporting a package."); return false; }
        _sceneSetupWindow?.Close();
        return _sceneSetupWindow == null;
    }
    private void ExportProjectZip(ProjectPackageKind kind)
    {
        if (!CanUseProjectPackages() || !ApplyOpenCommandEditor()) return;
        try
        {
            string source;
            if (kind == ProjectPackageKind.UrdfModel)
            {
                var model = new OpenFileDialog { Title = "Choose URDF model to export", Filter = "URDF model|*.urdf", FileName = CurrentRobotModel };
                if (model.ShowDialog(this) != true) return;
                source = model.FileName;
            }
            else if (kind == ProjectPackageKind.Sequence)
            {
                if (string.IsNullOrWhiteSpace(_jsonPath)) { if (!SaveProjectAsInteractive()) return; }
                else if (SequenceHasUnsavedChanges() && !SaveProjectTo(_jsonPath)) return;
                source = _jsonPath;
            }
            else
            {
                if (CurrentMovieDraft != null || SequenceHasUnsavedChanges())
                {
                    if (string.IsNullOrWhiteSpace(_jsonPath)) { if (!SaveProjectAsInteractive()) return; }
                    else if (!SaveProjectTo(_jsonPath)) return;
                }
                if (string.IsNullOrWhiteSpace(_moviePath)) { if (!SaveMovieAsInteractive(saveSequence: false)) return; }
                else if (MovieHasUnsavedChanges() && !SaveMovieToPath(_moviePath)) return;
                source = _moviePath;
            }
            var zip = new SaveFileDialog { Title = "Export " + PackageLabel(kind) + " to ZIP", Filter = "ZIP package|*.zip", DefaultExt = ".zip", AddExtension = true,
                FileName = Path.GetFileNameWithoutExtension(source) + (kind == ProjectPackageKind.UrdfModel ? "-model.zip" : "-" + kind.ToString().ToLowerInvariant() + ".zip"),
                InitialDirectory = Path.GetDirectoryName(source) };
            if (zip.ShowDialog(this) != true) return;
            string root = ConfigRoot;
            int count = LoadingWindow.Wait(this, Task.Run(() => ProjectPackage.Export(root, source, zip.FileName, kind)), "Collecting files and creating ZIP…");
            ShowStatus($"Exported {count:N0} files to {ConfigPathService.Reference(ConfigRoot, zip.FileName)}");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Export " + PackageLabel(kind), MessageBoxButton.OK, MessageBoxImage.Error); }
    }
    private void ImportProjectZip(ProjectPackageKind kind)
    {
        if (!CanUseProjectPackages()) return;
        var zip = new OpenFileDialog { Title = "Import " + PackageLabel(kind) + " from ZIP", Filter = "ZIP package|*.zip", CheckFileExists = true };
        if (zip.ShowDialog(this) != true) return;
        if (kind != ProjectPackageKind.UrdfModel && !ConfirmDocumentSwitch(replaceMovie: kind == ProjectPackageKind.Movie)) return;
        if (kind == ProjectPackageKind.UrdfModel && ApiUrdfView?.PoseEditorActive == true)
        { ShowStatus("Leave pose editing before importing URDF model files."); return; }
        try
        {
            string root = ConfigRoot;
            using var plan = LoadingWindow.Wait(this, Task.Run(() => ProjectPackageImport.Prepare(zip.FileName, root, kind)), "Checking ZIP and comparing existing files…");
            if (new PackageImportReviewWindow(plan) { Owner = this }.ShowDialog() != true) return;
            // Release readers only after Proceed. Cancel leaves the current document and media alone.
            StopPlayback(); StopRecordingReplay(); _videoAudio.Stop();
            _beamSound.Close(); _beamSoundPath = null; _beamSoundDuration = null; _beamSoundPlaying = false;
            foreach (var player in _fireSounds) player.Close();
            Array.Clear(_fireSoundPlaying); _fireSoundPath = null; _fireSoundShot = double.NaN;
            _reader?.Dispose(); _reader = null;
            ForEachHeadView(v => v.ReleasePackageMedia());
            try
            {
                LoadingWindow.Wait(this, Task.Run(() => { plan.Commit(); return true; }), "Importing files into Configuration…");
                _mediaCache.Clear(); _silentVideos.Clear(); LcdRenderer.ClearCache();
                if (kind == ProjectPackageKind.Movie) LoadMovieFromPath(plan.EntryFile, alreadyConfirmed: true);
                else if (kind == ProjectPackageKind.Sequence)
                {
                    if (LoadSequenceFromPath(plan.EntryFile, 0, fitTimeline: true, recordRecent: true, setActiveDocument: false))
                    {
                        _activeDocumentKind = ActiveDocumentKind.Sequence;
                        RecordRecentFile(plan.EntryFile, ActiveDocumentKind.Sequence, setActive: true);
                        UpdateDocumentStatusIndicators();
                    }
                }
                else
                {
                    ForEachHeadView(v => v.ReloadPackageModels(plan.Files.Select(f => f.Destination).ToArray()));
                    MessageBox.Show(this, "URDF model imported to:\n" + ConfigPathService.Reference(ConfigRoot, plan.EntryFile) + "\n\nChoose this file in URDF Scene Setup when you want to use it.", "Import URDF Model", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                ShowStatus($"Imported {plan.Files.Count:N0} files: {ConfigPathService.Reference(ConfigRoot, plan.EntryFile)}");
            }
            finally
            {
                ForEachHeadView(v => v.RefreshPackagePresentation());
                ApplyUrdfTimelinePosition();
                // A failed transaction restores its original audio; keep that document usable without reloading it.
                if (_reader == null && !string.IsNullOrWhiteSpace(_audioPath) && File.Exists(_audioPath))
                    try { _reader = new AudioFileReader(_audioPath); } catch { }
            }
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Import " + PackageLabel(kind), MessageBoxButton.OK, MessageBoxImage.Error); }
    }
    private static string PackageLabel(ProjectPackageKind kind) => kind == ProjectPackageKind.UrdfModel ? "URDF Model" : kind.ToString();
}
