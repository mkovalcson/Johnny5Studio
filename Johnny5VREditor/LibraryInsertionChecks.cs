using System.IO;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
namespace ServoAnimator;
public partial class MainWindow
{
    private void RunLibraryInsertionChecks(Action<bool,string> check,string report)
    {
        _doc.Commands.Clear();ClearManualPoseOverrides();_vrPoseTouched.Clear();
        _doc.Commands.AddRange(new[]{new ServoCommand{Servo=ServoNames.NeckTurn,OffsetSeconds=5,NumericValue=10},new ServoCommand{Servo=ServoNames.NeckTurn,OffsetSeconds=6,NumericValue=15},new ServoCommand{Servo=ServoNames.NeckTurn,OffsetSeconds=8,NumericValue=20}});
        RefreshAfterEdit();SetCursor(5);int undo=_undoStack.Count;
        string path=Path.Combine(Path.GetDirectoryName(report),"library-insertion-check.json");
        File.WriteAllText(path,"{\"commands\":[{\"servo\":\"NeckTurn\",\"offsetSeconds\":0,\"value\":40},{\"servo\":\"NeckTurn\",\"offsetSeconds\":2,\"value\":60}]}");
        void Review(bool accept,bool screenshot)
        {
            var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};
            timer.Tick+=(_,_)=>
            {
                var window=Application.Current.Windows.OfType<SequenceOverlapWindow>().FirstOrDefault();if(window==null)return;timer.Stop();
                if(screenshot)
                {
                    window.UpdateLayout();var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(window);
                    var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(Path.GetDirectoryName(report),"sequence-overlap-review.png"));png.Save(file);
                }
                foreach(var key in window.Choices.Keys.ToArray())window.Choices[key]=SequenceOverlapChoice.UseInserted;
                window.DialogResult=accept;
            };
            timer.Start();try{InsertLibrarySequence(path,5);}finally{timer.Stop();}
        }
        Review(false,true);check(_doc.Commands.Count==3&&_undoStack.Count==undo,"Canceling the actual sequence overlap window preserves timeline and Undo history.");
        Review(true,false);check(_doc.Commands.Count==3&&_doc.Commands.Any(c=>c.OffsetSeconds==8&&c.NumericValue==20)&&!_doc.Commands.Any(c=>c.OffsetSeconds==6),"Accepting Use inserted removes original commands only inside the insertion span.");
        var inserted=_doc.Commands.Where(c=>c.OffsetSeconds<=7).ToArray();check(inserted.Length==2&&inserted.Select(c=>c.GroupId).Distinct().Count()==1&&!string.IsNullOrEmpty(inserted[0].GroupId),"Actual library sequence insertion creates one fresh group at the intended timeline offset.");
        check(_undoStack.Count==undo+1,"Sequence overlap replacement creates one Undo step.");
        UndoSteps(1);check(_doc.Commands.Count==3&&_doc.Commands.Any(c=>c.OffsetSeconds==6&&c.NumericValue==15),"One Undo restores commands removed by sequence overlap replacement.");
    }
}
