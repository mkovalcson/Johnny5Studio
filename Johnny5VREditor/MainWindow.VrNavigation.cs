using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Johnny5.VrBridge;

namespace ServoAnimator;

public partial class MainWindow
{
    private string _vrCommandStatus="";
    private void ExecuteVrInput(Action action)
    {
        try{action();}
        catch(Exception ex)
        {
            _vrCommandStatus=" · VR input error: "+ex.Message;
            System.Diagnostics.Trace.TraceError("VR editor input: "+ex);
            ShowStatus("VR input interrupted. Release controls and retry: "+ex.Message);
        }
    }
    private void RunVrCommand(VrEditorCommand command)
    {
        // Keep document actions behind modal dialogs, just like desktop input.
        if(Application.Current.Windows.OfType<Window>().Any(w=>w!=this&&w.IsVisible&&w.Owner==this))return;
        switch(command)
        {
            case VrEditorCommand.PlayPause:PlayPause_Click(this,new RoutedEventArgs());break;
            case VrEditorCommand.Stop:SequenceStop_Click(this,new RoutedEventArgs());break;
            case VrEditorCommand.Undo:UndoSteps(1);break;
            case VrEditorCommand.Redo:Redo_Executed(this,null);break;
            case VrEditorCommand.PreviewFireLaser:
            case VrEditorCommand.PreviewDisassemble:
            case VrEditorCommand.PreviewReassemble:
            case VrEditorCommand.ClearEffects:PreviewVrEffect(command);break;
        }
        _vrCommandStatus=" · VR: "+command;
    }
    private void NavigateVrEditor(VrEditorNavigation navigation)
    {
        double pan=Math.Clamp(navigation.Pan,-1,1),zoom=Math.Clamp(navigation.Zoom,-1,1);
        // Popups (including combo-box lists) have their own WPF source, and
        // are not members of Application.Windows. Prefer the actual input root.
        if(System.Windows.Input.Mouse.DirectlyOver is Visual visual&&PresentationSource.FromVisual(visual)?.RootVisual is UIElement root)
        {
            var point=root.PointFromScreen(new(navigation.X,navigation.Y));
            if(new Rect(new Point(),root.RenderSize).Contains(point)&&NavigateVrHit(root.InputHitTest(point) as DependencyObject,navigation,pan,zoom))return;
        }
        foreach(var window in Application.Current.Windows.OfType<Window>().Where(w=>w.IsVisible).OrderByDescending(w=>w.IsActive))
        {
            var point=window.PointFromScreen(new Point(navigation.X,navigation.Y));
            if(!new Rect(0,0,window.ActualWidth,window.ActualHeight).Contains(point))continue;
            NavigateVrHit(window.InputHitTest(point) as DependencyObject,navigation,pan,zoom);
            return;
        }
    }
    private static bool NavigateVrHit(DependencyObject hit,VrEditorNavigation navigation,double pan,double zoom)
    {
        while(hit!=null)
        {
            if(hit is MovieTimelineView movie){movie.NavigateVr(movie.PointFromScreen(new(navigation.X,navigation.Y)).X,pan,zoom);return true;}
            if(hit is WaveformView waveform){waveform.NavigateVr(waveform.PointFromScreen(new(navigation.X,navigation.Y)).X,pan,zoom);return true;}
            if(hit is SplineView spline&&spline.SyncTarget!=null){spline.SyncTarget.NavigateVr(spline.PointFromScreen(new(navigation.X,navigation.Y)).X,pan,zoom);return true;}
            if(hit is ScrollViewer scroll){scroll.ScrollToVerticalOffset(scroll.VerticalOffset-zoom*65);return true;}
            hit=hit is Visual?VisualTreeHelper.GetParent(hit):(hit as FrameworkContentElement)?.Parent;
        }
        return false;
    }
}
