using System.Runtime.InteropServices;
using NAudio.MediaFoundation;

namespace ServoAnimator;

internal static partial class VideoFrames
{
    // Retain the decoder between VR video frames. Seeking/opening a file for
    // every picture would compete with editing and the scene bridge.
    internal sealed class Reader : IDisposable
    {
        private readonly string path;
        private readonly double duration;
        private IMFSourceReader reader;
        private int width,height,stride;
        private double position,lastTime,lastEnd;
        private bool seekable=true;
        private byte[] last;
        internal Reader(string path)
        {
            if(!System.IO.File.Exists(path))throw new System.IO.FileNotFoundException("Video not found.",path);
            this.path=path;duration=VideoClipCommand.ProbeSeconds(path);
            try{Open();}catch{Dispose();throw;}
        }
        private void Open()
        {
            Dispose();MediaFoundationApi.Startup();
            var attributes=MediaFoundationApi.CreateAttributes(1);
            try{attributes.SetUINT32(EnableVideoProcessing,1);MediaFoundationInterop.MFCreateSourceReaderFromURL(path,attributes,out reader);}
            finally{Marshal.ReleaseComObject(attributes);}
            reader.SetStreamSelection(AllStreams,false);reader.SetStreamSelection(FirstVideoStream,true);
            var wanted=MediaFoundationApi.CreateMediaType();
            try{wanted.SetGUID(MediaFoundationAttributes.MF_MT_MAJOR_TYPE,VideoMajorType);wanted.SetGUID(MediaFoundationAttributes.MF_MT_SUBTYPE,Rgb32);reader.SetCurrentMediaType(FirstVideoStream,IntPtr.Zero,wanted);}
            finally{Marshal.ReleaseComObject(wanted);}
            reader.GetCurrentMediaType(FirstVideoStream,out var actual);
            try
            {
                actual.GetUINT64(FrameSize,out long size);width=(int)(size>>32);height=(int)(size&0xffffffff);stride=width*4;
                try{actual.GetUINT32(DefaultStride,out int reported);if(reported!=0)stride=reported;}catch(COMException){}
            }
            finally{Marshal.ReleaseComObject(actual);}
            position=lastTime=lastEnd=0;last=null;
        }
        internal Frame Read(double seconds)
        {
            double target=Math.Clamp(seconds,0,Math.Max(0,duration-.001));
            if(target<position-1e-9||target>lastEnd+ReadAheadSeconds)
            {
                last=null;lastEnd=0;
                if(seekable){try{Seek(reader,target);}catch(COMException){seekable=false;}}
                if(!seekable&&target<position-1e-9)Open();
            }
            bool ended=false;
            while(last==null||lastEnd<=target)
            {
                reader.ReadSample(FirstVideoStream,0,out _,out var flags,out ulong timestamp,out var sample);
                if(((int)flags&EndOfStream)!=0){ended=true;if(sample!=null)Marshal.ReleaseComObject(sample);break;}
                if(sample==null)continue;
                try
                {
                    sample.GetSampleDuration(out long length);lastTime=(long)timestamp/1e7;lastEnd=((long)timestamp+Math.Max(1,length))/1e7;position=lastTime;
                    if(last==null||lastEnd>target)last=Copy(sample,width,height,stride);
                }
                finally{Marshal.ReleaseComObject(sample);}
            }
            if(last==null)throw new InvalidOperationException("No picture could be decoded from this video.");
            if(ended)position=double.PositiveInfinity;
            return new(width,height,last,lastTime);
        }
        public void Dispose(){if(reader!=null){Marshal.ReleaseComObject(reader);reader=null;}}
    }
}
