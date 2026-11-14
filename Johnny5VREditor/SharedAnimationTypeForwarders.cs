using System.Runtime.CompilerServices;

// Retain editor assembly-qualified lookups while implementation lives in Shared/Animation.
[assembly: TypeForwardedTo(typeof(ServoAnimator.ServoCommand))]
[assembly: TypeForwardedTo(typeof(ServoAnimator.ServoCommandJsonConverter))]
[assembly: TypeForwardedTo(typeof(ServoAnimator.LibraryItemDocument))]
[assembly: TypeForwardedTo(typeof(ServoAnimator.MovieSequenceItem))]
[assembly: TypeForwardedTo(typeof(ServoAnimator.MovieDocument))]
[assembly: TypeForwardedTo(typeof(ServoAnimator.AnimationDocument))]
[assembly: TypeForwardedTo(typeof(ServoAnimator.AudioTrimSettings))]
[assembly: TypeForwardedTo(typeof(ServoAnimator.CommandConflicts))]
[assembly: TypeForwardedTo(typeof(ServoAnimator.CommandEditSession))]
[assembly: TypeForwardedTo(typeof(ServoAnimator.UrdfCameraCommands))]
[assembly: TypeForwardedTo(typeof(ServoAnimator.SplineUtil))]
[assembly: TypeForwardedTo(typeof(ServoAnimator.PlaybackClock))]
