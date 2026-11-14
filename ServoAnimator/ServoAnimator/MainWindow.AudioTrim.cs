using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ServoAnimator;

public partial class MainWindow
{
    private static readonly Brush ClipTimeBrush = new SolidColorBrush(Color.FromRgb(235, 170, 70));
    private static readonly Brush PrimaryTimeBrush = new SolidColorBrush(Color.FromRgb(120, 220, 140));
    private void UpdateAudioRelativeTime()
    {
        if (AudioRelativeTimeText == null || Waveform == null) return;
        AudioClipVisual clip = null;
        foreach (var candidate in Waveform.AudioClips)
            if (candidate.Start <= _cursorTime + 1e-9 && (clip == null || candidate.Start >= clip.Start)) clip = candidate;
        bool primary = _primaryDuration > 0 && _audioOffset <= _cursorTime + 1e-9 && (clip == null || _audioOffset > clip.Start);
        if (!primary && clip == null) { AudioRelativeTimeText.Text = ""; return; }
        double start = primary ? _audioOffset : clip.Start;
        AudioRelativeTimeText.Text = $"Audio +{Math.Max(0, _cursorTime - start):F3} s";
        AudioRelativeTimeText.Foreground = primary ? PrimaryTimeBrush : ClipTimeBrush;
    }

    private void ShowAudioTrimDialog(AudioClipVisual clip)
    {
        if (!ApplyOpenCommandEditor()) return;
        if (IsRunning) PausePlayback();
        var dialog = CreateAudioTrimDialog(clip);
        dialog.Owner = this;
        dialog.ShowDialog();
    }

    internal Window CreateAudioTrimDialog(AudioClipVisual clip)
    {
        var command = clip.Command;
        var panel = new StackPanel { Margin = new Thickness(16) };
        var win = new Window
        {
            Title = "Audio timing — " + clip.Name, Content = panel, Width = 720,
            SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(35, 38, 44)), Foreground = Brushes.WhiteSmoke
        };
        HelpSystem.EnableContextHelp(win, "recording-audio");
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gold, Margin = new Thickness(0, 10, 0, 10) };
        StackPanel Row(string label, out TextBox value, out Button commit)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 5) };
            row.Children.Add(new TextBlock { Text = label, Width = 170, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.WhiteSmoke });
            value = new TextBox { Width = 100, Margin = new Thickness(0, 0, 8, 0) };
            commit = new Button { Content = "Commit", MinWidth = 80, Margin = new Thickness(0, 0, 12, 0), Padding = new Thickness(8, 4, 8, 4) };
            row.Children.Add(value); row.Children.Add(commit); panel.Children.Add(row); return row;
        }
        Row("Timeline start (seconds):", out var timeline, out var move);
        timeline.Text = command.OffsetSeconds.ToString("F3"); move.Content = "Apply";
        bool Number(TextBox box, out double number) => double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out number) && double.IsFinite(number) && number >= 0;
        move.Click += (_, _) =>
        {
            if (!Number(timeline, out double value)) { status.Text = "Enter a non-negative timeline start in seconds."; return; }
            if (!_doc.Commands.Contains(command))
            {
                PushUndo("Move primary audio and following commands"); EnsureAudioCommand(clip); _clipDragUndoPushed = true;
            }
            Waveform_ClipMoveRequested(clip, value); _clipDragUndoPushed = false;
            if (!RefreshAfterEdit()) { win.Close(); return; }
            status.Text = "Timeline start applied.";
        };
        panel.Children.Add(new TextBlock { Text = "Skip to and End at are positions in the original audio file (seconds).", Foreground = Brushes.WhiteSmoke, Margin = new Thickness(0, 10, 0, 5) });
        void TrimRow(bool skip)
        {
            var initial = command.AudioTrim ?? new AudioTrimSettings();
            bool locked = skip ? initial.SkipCommitted : initial.EndCommitted;
            var row = Row(skip ? "Skip to:" : "End at:", out var value, out var commit);
            value.Text = (skip ? locked ? initial.Start : Math.Clamp(_cursorTime, 0, clip.SourceDuration)
                : initial.EndSeconds ?? clip.SourceDuration).ToString("F3");
            var scrub = new CheckBox { Content = skip ? "Scrub commands before skip to" : "Scrub commands after end at",
                Foreground = Brushes.WhiteSmoke, VerticalAlignment = VerticalAlignment.Center,
                IsChecked = skip ? initial.ScrubBefore : initial.ScrubAfter };
            row.Children.Add(scrub);
            void Lock()
            {
                value.IsEnabled = scrub.IsEnabled = !locked;
                commit.ToolTip = locked ? "Committed. Click to unlock, edit, then click Commit again to apply." : "Apply and lock this value and its scrub setting";
                commit.FontWeight = locked ? FontWeights.Bold : FontWeights.Normal;
            }
            Lock();
            commit.Click += (_, _) =>
            {
                if (locked) { locked = false; Lock(); value.Focus(); value.SelectAll(); return; }
                if (!Number(value, out double seconds) || clip.SourceDuration <= 0 || seconds > clip.SourceDuration + .0005)
                { status.Text = $"Enter an audio-file position between 0 and {clip.SourceDuration:F3} seconds. The audio file must be available."; return; }
                seconds = Math.Min(seconds, clip.SourceDuration);
                var next = command.AudioTrim?.Clone() ?? new AudioTrimSettings();
                if (skip) { next.SkipSeconds = seconds; next.SkipCommitted = true; next.ScrubBefore = scrub.IsChecked == true; }
                else { next.EndSeconds = seconds; next.EndCommitted = true; next.ScrubAfter = scrub.IsChecked == true; }
                if (next.Start >= (next.EndCommitted ? next.EndSeconds.Value : clip.SourceDuration))
                { status.Text = "End at must be after Skip to."; return; }
                PushUndo(skip ? "Set audio Skip to" : "Set audio End at");
                EnsureAudioCommand(clip);
                AudioTrimSettings.Apply(_doc, command, next);
                if (!RefreshAfterEdit()) { win.Close(); return; }
                locked = true; Lock();
                status.Text = $"Committed. {next.HiddenCommands.Count} commands hidden; save the sequence to retain these settings.";
            };
        }
        TrimRow(true); TrimRow(false);
        var reset = new Button { Content = "Reset trims", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0,8,0,0),
            ToolTip = "Clear Skip to / End at, restore hidden commands, and undo the scrub shift." };
        reset.Click += (_, _) =>
        {
            if (command.AudioTrim == null) return;
            PushUndo("Reset audio trims");
            AudioTrimSettings.Apply(_doc, command, new AudioTrimSettings { CommandsLinked = command.AudioTrim.CommandsLinked });
            RefreshAfterEdit(); win.Close();
        };
        panel.Children.Add(reset);
        panel.Children.Add(status);
        panel.Children.Add(new TextBlock { Text = "Commit locks each row. Click Commit again to edit it, then commit the new value. Hidden commands are retained and restored when the bounds expand. Uncommitted edits are ignored when closing.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray });
        var close = new Button { Content = "Close", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 90, Margin = new Thickness(0, 12, 0, 0) };
        close.Click += (_, _) => win.Close(); panel.Children.Add(close);
        return win;
    }
}
