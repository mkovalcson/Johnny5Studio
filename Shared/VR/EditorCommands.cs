namespace Johnny5.VrBridge;

internal enum VrEditorCommand : byte { PlayPause=1,Stop=2,Undo=3,Redo=4,PreviewFireLaser=5,PreviewDisassemble=6,PreviewReassemble=7,ClearEffects=8 }
internal sealed record VrEditorNavigation(int X,int Y,float Pan,float Zoom);
