using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Johnny5.VrBridge;

namespace ServoAnimator;

public partial class MainWindow
{
    private async Task ServeVrDesktop(CancellationToken stop)
    {
        while(!stop.IsCancellationRequested)
        {
            try
            {
                using var pipe=new NamedPipeServerStream("Johnny5VR.Desktop."+VrEditorMode.Pipe,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                using var cancel=stop.Register(()=>pipe.Dispose());await pipe.WaitForConnectionAsync(stop);
                using var reader=new BinaryReader(pipe,System.Text.Encoding.UTF8,true);using var writer=new BinaryWriter(pipe,System.Text.Encoding.UTF8,true);
                while(pipe.IsConnected&&!stop.IsCancellationRequested)
                {
                    int request=await Task.Run(()=>reader.ReadByte(),stop);if(request!=1)throw new InvalidDataException("Unknown desktop request.");
                    var frame=await Dispatcher.InvokeAsync(CaptureVrDesktop,System.Windows.Threading.DispatcherPriority.Background);
                    var bytes=DesktopWire.Encode(frame);writer.Write(bytes.Length);await pipe.WriteAsync(bytes,stop);await pipe.FlushAsync(stop);
                }
            }
            catch(Exception ex) when(ex is IOException or OperationCanceledException or ObjectDisposedException){if(stop.IsCancellationRequested)return;}
            catch(Exception ex){System.Diagnostics.Trace.TraceError("VR desktop: "+ex);try{await Task.Delay(500,stop);}catch(OperationCanceledException){return;}}
        }
    }
    private VrDesktopFrame CaptureVrDesktop()=>VrDesktopCapture.Capture();
}
