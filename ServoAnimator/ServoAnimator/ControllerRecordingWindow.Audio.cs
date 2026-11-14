using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NAudio.Wave;

namespace ServoAnimator;

internal sealed partial class ControllerRecordingWindow
{
    private readonly CheckBox _recordAudio = new() { Content = "Record Audio", Margin = new Thickness(0, 8, 0, 4) };
    private readonly CheckBox _convertVoice = new() { Content = "Automatically Convert Voice when Recording Finishes", Margin = new Thickness(0, 0, 0, 8), IsEnabled = false };
    private readonly ComboBox _microphone = new() { MinWidth = 230, DisplayMemberPath = "Name", ToolTip = "Select a microphone to monitor its level" };
    private readonly ProgressBar _audioPeak = new() { Minimum = -60, Maximum = 0, Value = -60, Height = 14, Width = 190, Background = Brushes.DarkSlateGray, Foreground = Brushes.LimeGreen };
    private readonly TextBlock _audioLevel = new() { Text = "−∞ dBFS", Width = 85 };
    private readonly Button _clip = new() { Content = "CLIP", Foreground = Brushes.White, Background = Brushes.DimGray, ToolTip = "Click to clear the clip indicator" };
    private readonly TakeAudioCapture _audioCapture = new();
    private readonly DispatcherTimer _audioMeterTimer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private record Microphone(int Index, string Name);
    internal bool RecordAudio => _recordAudio.IsChecked == true;
    internal bool AutomaticallyConvertVoice => _convertVoice.IsChecked == true;
    internal string AudioError => _audioCapture.Error;
    internal string BeginAudio(string folder, string name) => _audioCapture.BeginTake(folder, name);
    internal void EndAudio() => _audioCapture.EndTake();
    private void AddAudioControls()
    {
        HelpSystem.SetTopic(_recordAudio, "recording-audio");
        HelpSystem.SetTopic(_microphone, "recording-audio");
        HelpSystem.SetTopic(_clip, "recording-audio");
        _setup.Children.Add(_recordAudio);
        _setup.Children.Add(_convertVoice);
        HelpSystem.SetTopic(_convertVoice, "recording-audio");
        _recordAudio.Checked += (_, _) => _convertVoice.IsEnabled = true;
        _recordAudio.Unchecked += (_, _) => _convertVoice.IsEnabled = false;
        var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        row.Children.Add(_microphone);
        var refresh = new Button { Content = "Refresh microphones", Margin = new Thickness(8, 0, 0, 0) }; row.Children.Add(refresh);
        HelpSystem.SetTopic(row, "recording-audio");
        _setup.Children.Add(row);
        var meter = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        meter.Children.Add(_audioPeak); meter.Children.Add(_audioLevel); meter.Children.Add(_clip); _setup.Children.Add(meter);
        void Devices()
        {
            var devices = Enumerable.Range(0, WaveIn.DeviceCount).Select(i => new Microphone(i, WaveIn.GetCapabilities(i).ProductName)).ToList();
            _microphone.ItemsSource = devices;
            _microphone.SelectedItem = devices.Where(d => d.Name == _preferredMicrophone).Skip(Math.Max(0, _preferredMicrophoneOccurrence)).FirstOrDefault();
        }
        refresh.Click += (_, _) => { try { Devices(); } catch (Exception ex) { SetStatus(ex.Message); } };
        _microphone.SelectionChanged += (_, _) =>
        {
            try
            {
                _audioCapture.Dispose();
                if (_microphone.SelectedItem is Microphone mic)
                {
                    _preferredMicrophone = mic.Name;
                    _preferredMicrophoneOccurrence = _microphone.Items.OfType<Microphone>().TakeWhile(d => d != mic).Count(d => d.Name == mic.Name);
                    _audioCapture.Monitor(mic.Index); _audioMeterTimer.Start();
                }
            }
            catch (Exception ex) { SetStatus("Microphone: " + ex.Message); }
        };
        _clip.Click += (_, _) => _audioCapture.ClearClip();
        _audioMeterTimer.Tick += (_, _) =>
        {
            float peak = _audioCapture.TakePeak(); double db = peak > 0 ? 20 * Math.Log10(peak) : -60;
            _audioPeak.Value = Math.Max(-60, db); _audioLevel.Text = peak > 0 ? $" {db:0.0} dBFS" : " −∞ dBFS";
            _clip.Background = _audioCapture.Clipped ? Brushes.Red : Brushes.DimGray;
            if (_audioCapture.Error != null) SetStatus("Microphone: " + _audioCapture.Error);
        };
        Loaded += (_, _) => { if (_microphone.ItemsSource == null) { try { Devices(); } catch (Exception ex) { SetStatus(ex.Message); } } };
        Closed += (_, _) => { _audioMeterTimer.Stop(); _audioCapture.Dispose(); };
    }
}
