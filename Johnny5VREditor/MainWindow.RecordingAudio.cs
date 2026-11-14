using System.IO;
using System.Windows;
using NAudio.Wave;

namespace ServoAnimator;

public partial class MainWindow
{
    private string _recordingAudioPath;
    private ServoCommand _recordingAudioCommand;
    private double? _recordingOriginalRate;
    private WaveOutEvent _takeAudioOutput;
    private AudioFileReader _takeAudioReader;
    private void DiscardRecordingAudio()
    {
        try { _recordingWindow?.EndAudio(); } catch (Exception ex) { ShowStatus("Audio finalization: " + ex.Message); }
        bool changed = _recordingAudioCommand != null && _doc.Commands.Remove(_recordingAudioCommand);
        _recordingAudioCommand = null;
        if (_recordingAudioPath != null)
        {
            try { File.Delete(_recordingAudioPath); }
            catch (Exception ex) { ShowStatus("Could not remove discarded audio: " + ex.Message); }
            _recordingAudioPath = null;
        }
        if (changed) RefreshAfterEdit();
    }
    private void StartRecordedAudioReplay()
    {
        if (_recordingAudioPath == null) return;
        try
        {
            _takeAudioReader = new AudioFileReader(_recordingAudioPath);
            _takeAudioOutput = new WaveOutEvent(); _takeAudioOutput.Init(_takeAudioReader); _takeAudioOutput.Play();
        }
        catch (Exception ex) { StopRecordedAudioReplay(); _recordingWindow?.SetStatus("Audio replay unavailable: " + ex.Message); }
    }
    private void StopRecordedAudioReplay()
    {
        _takeAudioOutput?.Dispose(); _takeAudioOutput = null;
        _takeAudioReader?.Dispose(); _takeAudioReader = null;
    }
    private void EmptyNewSequence_Click(object sender, RoutedEventArgs e)
    {
        var name = new NewSequenceWindow(this, standalone: true);
        if (name.ShowDialog() != true) return;
        if (!ConfirmDocumentSwitch(replaceMovie: false)) return;
        var save = new Microsoft.Win32.SaveFileDialog { Title = "Create new sequence", FileName = name.SequenceName + ".json",
            Filter = "Sequence (*.json)|*.json", DefaultExt = ".json", InitialDirectory = _folders.ProjectFolderOrDefault };
        if (save.ShowDialog(this) != true || !RequireConfigPath(save.FileName, "sequence")) return;
        try
        {
            // Normal document metadata with an empty commands array; no pose or audio commands.
            new AnimationDocument { Description = name.SequenceName }.Save(save.FileName);
            if (LoadSequenceFromPath(save.FileName, 0, fitTimeline: true, setActiveDocument: false))
            { _activeDocumentKind = ActiveDocumentKind.Sequence; SaveLastActiveDocument(); }
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "New Sequence", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
}
