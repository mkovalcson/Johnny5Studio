using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ServoAnimator;

public sealed partial class RobotHeadView
{
    private bool _exportHidesStage;
    private Brush _exportBackground;
    private Rectangle _exportMarker, _exportMarkerEnd;
    /// <summary>Size of the markers at two opposite corners of the framed area that prove a capture shows the latest
    /// render all the way across (they are cleared from the frames).</summary>
    internal const double ExportMarkerSize = 4;

    /// <summary>Robot Frames export: keep the 3D stage out of the scene so frames hold only the robot.</summary>
    internal void SetExportHidesStage(bool hidden)
    {
        if (_exportHidesStage == hidden) return;
        _exportHidesStage = hidden;
        ApplyStageHidden();
    }

    /// <summary>During an export the video is not drawn (frames are transparent); only its stage-hiding effect applies.</summary>
    internal void SetVideoForExport(bool clipActive)
    {
        PauseBackdropVideo();
        _backdropVideo.Visibility = Visibility.Collapsed;
        SetStageHiddenForVideo(clipActive);
    }

    /// <summary>The area, in this view's device-independent pixels, where a video of the given size is drawn with
    /// <paramref name="fit"/>; without a video, the largest centred area of the output's shape.</summary>
    internal Rect ExportReferenceArea(int videoWidth, int videoHeight, TvFit fit, double outputAspect)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 2 || h < 2) throw new InvalidOperationException("The URDF view is not on screen.");
        return ExportArea(w, h, videoWidth, videoHeight, fit, outputAspect);
    }

    internal static Rect ExportArea(double w, double h, int videoWidth, int videoHeight, TvFit fit, double outputAspect)
    {
        if (videoWidth <= 0 || videoHeight <= 0) return Fit(w, h, outputAspect, cover: false);
        if (fit == TvFit.Stretch) return new Rect(0, 0, w, h);
        // Cover: the picture overflows the view; robot parts beyond the view's edges are simply not drawn.
        return Fit(w, h, (double)videoWidth / videoHeight, cover: fit == TvFit.Cover);

        static Rect Fit(double w, double h, double aspect, bool cover)
        {
            double fw = w, fh = w / aspect;
            if (cover ? fh < h : fh > h) { fh = h; fw = h * aspect; }
            return new Rect((w - fw) / 2, (h - fh) / 2, fw, fh);
        }
    }

    /// <summary>Turn this view into an export renderer: no buttons, legend or overlays, a solid background that the
    /// export switches between black and white, and two tiny markers.</summary>
    internal void PrepareExportView()
    {
        foreach (UIElement e in new UIElement[] { _poseOverlay, _bottomControls, _status, _fps, _verticalResizeHandle, _tvLayer })
            e.Visibility = Visibility.Collapsed;
        Rectangle Marker() => new() { Width = ExportMarkerSize, Height = ExportMarkerSize, HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Fill = Brushes.Black };
        _exportMarker = Marker(); _exportMarkerEnd = Marker();
        Children.Add(_exportMarker); Children.Add(_exportMarkerEnd);
        SetExportBackground(Brushes.Black);
    }

    /// <summary>Place the markers inside the top-left and bottom-right corners of <paramref name="area"/> (this view's DIPs).</summary>
    internal void SetExportMarkerArea(Rect area)
    {
        _exportMarker.Margin = new Thickness(area.X, area.Y, 0, 0);
        _exportMarkerEnd.Margin = new Thickness(area.Right - ExportMarkerSize, area.Bottom - ExportMarkerSize, 0, 0);
    }

    internal void SetExportBackground(Brush background)
    {
        _exportBackground = background;
        Background = background;
    }

    internal void SetExportMarker(Color color) { var fill = new SolidColorBrush(color); _exportMarker.Fill = fill; _exportMarkerEnd.Fill = fill; }

    /// <summary>Release export-only state on the user's views after an export.</summary>
    internal void EndFrameExport() => SetExportHidesStage(false);
}
