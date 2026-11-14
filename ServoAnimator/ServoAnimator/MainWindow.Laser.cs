using System.IO;

namespace ServoAnimator;

public partial class MainWindow
{
    /// <summary>Accessory commands override each view's configured default.
    /// Before any accessory commands, None, Toolbox or Laser applies.</summary>
    private void ApplyLaserTimeline()
    {
        ForEachHeadView(view => view.ApplyAccessoryTimeline(_doc.Commands, _cursorTime));
    }

    /// <summary>Controller actions: flip the laser shown/hidden, or toolbox/laser. A visual preview,
    /// like the controller Beam: the timeline's commands take over again when the cursor moves.</summary>
    private void ToggleControllerLaser(bool toolbox)
    {
        if (!ControllerOutputAllowed(false)) return;
        bool? target = null;
        ForEachHeadView(v => { if (v.HasLaser) target ??= toolbox ? !v.ToolboxShown : !v.LaserHidden; });
        if (target is not bool value) return;
        ForEachHeadView(v => { if (toolbox) v.SetToolboxShown(value); else v.SetLaserHidden(value); });
    }

    /// <summary>insert_lcd_show, insert_hide_laser and insert_toolbox_swap: one command at the
    /// captured cursor, as one undo step.</summary>
    private object HandleLaserApi(EditorApiRequest request, string payload)
    {
        if (ApiUrdfView == null) throw new InvalidOperationException("Open a URDF view first.");
        ServoNames servo; string text, label;
        switch (request.Method)
        {
            case "insert_hide_laser":
                var hide = new HideLaserCommand { Hide = request.Hide ?? true };
                (servo, text, label) = (ServoNames.HideLaser, hide.ToJson(), hide.Label());
                break;
            case "insert_toolbox_swap":
                var swap = new ToolboxSwapCommand { Swap = request.Swap ?? true };
                (servo, text, label) = (ServoNames.ToolboxSwap, swap.ToJson(), swap.Label());
                break;
            default:
                bool image = !string.IsNullOrWhiteSpace(request.Image);
                var lcd = new LcdShowCommand { Text = request.Text ?? (image ? "" : LcdShowCommand.DefaultText), Color = request.Color ?? LcdShowCommand.DefaultColor };
                if (!string.IsNullOrWhiteSpace(request.Fit))
                    lcd.Fit = Enum.TryParse<TvFit>(request.Fit, true, out var fit) && Enum.IsDefined(fit) ? fit : throw new InvalidOperationException("fit must be contain, cover or stretch.");
                if (image)
                {
                    string full = Path.IsPathRooted(request.Image) ? request.Image : Path.Combine(ConfigRoot, request.Image);
                    if (!File.Exists(full)) throw new InvalidOperationException("LCD picture not found: " + full);
                    lcd.Image = ConfigPathService.IsWithin(ConfigRoot, full) ? Path.GetRelativePath(ConfigRoot, full) : full;
                }
                lcd.Validate();
                (servo, text, label) = (ServoNames.LcdShow, lcd.ToJson(), lcd.Label());
                break;
        }
        var command = new ServoCommand { Servo = servo, TextValue = text, OffsetSeconds = ServoCommand.TimeKey(_cursorTime),
            Reason = string.IsNullOrWhiteSpace(request.Description) ? label : request.Description };
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
        object result = new { ok = true, requestId = request.RequestId, inserted = 1, text = command.TextValue, state = EditorApiState() };
        _apiReceipts.Add(request.RequestId, (payload, result)); return result;
    }
}
