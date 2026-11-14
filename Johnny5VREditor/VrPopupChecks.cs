using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Johnny5.VrBridge;

namespace ServoAnimator;

internal static class VrPopupChecks
{
    [DllImport("user32.dll")]private static extern IntPtr GetLastActivePopup(IntPtr owner);
    [DllImport("user32.dll")]private static extern bool PostMessage(IntPtr handle,uint message,IntPtr wParam,IntPtr lParam);
    private static void Pump()
    {
        var frame=new DispatcherFrame();
        var timer=new DispatcherTimer(TimeSpan.FromMilliseconds(250),DispatcherPriority.Background,(_,_)=>frame.Continue=false,Dispatcher.CurrentDispatcher);
        Dispatcher.PushFrame(frame);timer.Stop();
    }
    internal static void Run(string report)
    {
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        var combo=new ComboBox{ItemsSource=new[]{"First sequence","Second sequence"},Width=220,Height=32,SelectedIndex=0};
        var panel=new StackPanel{Background=Brushes.DarkSlateBlue};panel.Children.Add(combo);
        var owner=new Window{Title="VR popup check",Content=panel,Left=120,Top=100,Width=700,Height=500};
        var lines=new List<string>();
        void Check(bool valid,string message){if(!valid)throw new InvalidOperationException(message);lines.Add("PASS: "+message);}
        VrDesktopImage Find(VrDesktopFrame frame,DependencyObject element)
        {
            var handle=((HwndSource)PresentationSource.FromDependencyObject(element)).Handle.ToInt64();
            var image=frame.Windows.FirstOrDefault(w=>w.Handle==handle);
            Check(image!=null,"Captured "+element.GetType().Name+" window.");return image;
        }
        void Save(VrDesktopImage image,string name)
        {
            Check(image.Bgra.Where((_,i)=>i%4!=3).Distinct().Count()>8,name+" contains rendered controls.");
            var bitmap=System.Windows.Media.Imaging.BitmapSource.Create(image.PixelWidth,image.PixelHeight,96,96,PixelFormats.Pbgra32,null,image.Bgra,image.PixelWidth*4);
            var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var stream=File.Create(Path.Combine(Path.GetDirectoryName(report),name+".png"));encoder.Save(stream);
        }
        void NativeDialog(Action show,string name)
        {
            Exception error=null;var ownerHandle=new WindowInteropHelper(owner).Handle;
            var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(600)};
            timer.Tick+=(_,_)=>
            {
                timer.Stop();var handle=GetLastActivePopup(ownerHandle);
                try
                {
                    Check(handle!=ownerHandle,name+" opened as a separate native window.");
                    var frame=DesktopWire.Decode(DesktopWire.Encode(VrDesktopCapture.Capture()));
                    var image=frame.Windows.FirstOrDefault(w=>w.Handle==handle.ToInt64());
                    Check(image!=null,name+" is included in the VR stream.");
                    Check(frame.Windows[0].Handle==handle.ToInt64(),name+" is layered above its editor owner.");
                    Save(image,name);
                }
                catch(Exception ex){error=ex;}
                finally{if(handle!=ownerHandle)PostMessage(handle,0x10,IntPtr.Zero,IntPtr.Zero);}
            };
            timer.Start();try{show();}finally{timer.Stop();}
            if(error!=null)throw error;
        }
        try
        {
            owner.Show();owner.Activate();Pump();Find(VrDesktopCapture.Capture(),owner);
            combo.IsDropDownOpen=true;Pump();
            var popup=(Popup)combo.Template.FindName("PART_Popup",combo);
            Save(Find(VrDesktopCapture.Capture(),popup.Child),"combo-popup");combo.IsDropDownOpen=false;
            var menu=new ContextMenu();menu.Items.Add(new MenuItem{Header="Insert command"});menu.Items.Add(new MenuItem{Header="Edit sequence"});
            menu.PlacementTarget=combo;menu.Placement=PlacementMode.Bottom;menu.IsOpen=true;Pump();
            Save(Find(VrDesktopCapture.Capture(),menu),"context-popup");menu.IsOpen=false;
            var child=new Window{Owner=owner,Title="Configuration dialog",Content=new Button{Content="Apply"},Width=400,Height=250,WindowStartupLocation=WindowStartupLocation.CenterOwner};
            child.Show();Pump();Save(Find(VrDesktopCapture.Capture(),child),"wpf-dialog");child.Close();
            NativeDialog(()=>MessageBox.Show(owner,"Test confirmation appears in VR.","VR confirmation",MessageBoxButton.OKCancel),"native-confirmation");
            NativeDialog(()=>new Microsoft.Win32.OpenFileDialog{Title="VR file picker test",CheckFileExists=true}.ShowDialog(owner),"native-file-picker");
            Find(VrDesktopCapture.Capture(),owner);
            File.WriteAllLines(report,lines);
        }
        finally{owner.Close();app.Shutdown();}
    }
}
