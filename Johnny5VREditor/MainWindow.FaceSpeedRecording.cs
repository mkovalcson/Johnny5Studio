namespace ServoAnimator;

public partial class MainWindow
{
    private void SampleControllerRecording(double time)
    {
        if (_controllerTake == null) return;
        if (_controllerTake.SpeedLimitedControls.Count > 0)
        {
            _controllerPreview.Flush();
            var view = (_urdfUndocked ? _head?.HeadView : EmbeddedHeadView) ?? EmbeddedHeadView;
            if (view != null)
                foreach (var command in view.CaptureCalibratedCommands(_controllerTake.SpeedLimitedControls))
                    if (!FaceOverrideOwns(command.Control.Value)) _controllerTake.Accept(command);
        }
        _controllerTake.Sample(time);
    }
}
