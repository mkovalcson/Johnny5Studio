using System.IO;
using System.Windows;

namespace ServoAnimator;

public sealed partial class RobotHeadView
{
    internal void ReleasePackageMedia()
    {
        _backdropVideo.Close(); _backdropVideo.Source = null; _backdropVideoPath = null; _backdropVideoDuration = null; _backdropVideoPlaying = false; _backdropVideo.Visibility = Visibility.Collapsed;
        _tvVideo.Close(); _tvVideo.Source = null; _tvVideoPath = null; _tvVideoDuration = null; _tvVideoPlaying = false; _tvVideo.Visibility = Visibility.Collapsed;
        _tvImage.Source = null; _tvImagePath = null;
        _lcdKey = null; _presentationSignature = null;
        SetStageHiddenForVideo(false);
    }
    internal void RefreshPackagePresentation() => ApplyPresentation();
    internal void ReloadPackageModels(IReadOnlyList<string> files)
    {
        var frame = CaptureCurrentFraming();
        if (!string.IsNullOrWhiteSpace(LoadedModelFile) && files.Any(f => ConfigPathService.IsWithin(Path.GetDirectoryName(LoadedModelFile), f)))
            InstallModel(LoadedModelFile, UrdfScene.Load(LoadedModelFile), frame);
        if (_stageFile != null && files.Any(f => ConfigPathService.IsWithin(Path.GetDirectoryName(_stageFile), f)))
        {
            _stageFile = null; _stageScene = null; _preparedStageFile = null; _preparedStageScene = null; _presentationSignature = null;
            ApplyPresentation(); ApplyFraming(frame);
        }
    }
}
