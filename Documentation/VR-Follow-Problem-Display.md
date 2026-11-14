# Follow problem display — 2026-11-08

Shows Follow problems as they happen, while the left-controller dropouts are being worked
out. VR Player only; the VR Editor is unchanged. Includes the bridge pace and memory changes
(see VR-Bridge-Pace-And-Memory.md).

## In the headset

The overhead status panel above Johnny pops up for 8 seconds when there is a problem, even
when "Show overhead Follow status" is off. It turns red with a gold border and the heading
"⚠ Follow problem". It shows the time and what happened, and a "Now:" line with the current
Follow status when that adds something.

A problem is:

- Tracking interrupted while following (headset, left or right controller), even for one
  frame. The panel names the device and SteamVR's tracking result, for example
  "Left: device 2, connected=True, valid=False, Running_OutOfRange".
- Tracking coming back, with how long it was gone: "Tracking back after 80 ms".
- Follow or recording stopping for any reason you didn't ask for: tracking lost over
  150 ms, menu or dashboard opened, timeline/cursor changed, playback or a dialog in the
  editor, editor disconnected, recording failed to start, or a motion capture error.

Stops you ask for are not problems: B button, transport Stop, and inserting a pose.

Follow behaviour is unchanged: it still stops after 150 ms of invalid tracking.

## Problem log

Diagnostics\follow-problems-<date-time>.txt (next to the bridge-timing CSV), one line per
event with millisecond times:

- Follow started / Follow stopped and the reason (including stops you asked for, for
  context).
- Headset, Left controller, Right controller: lost (with SteamVR's detail) and back after
  N ms. This records every dropout, including ones too short to stop Follow, and also
  outside Follow.
- Input focus lost / back (VR menu, SteamVR dashboard or another app taking input).

A device is only logged after it has been tracked once, so the headset warming up at launch
is not reported. The desktop window's timing summary shows the latest event.

## What to send after a session

The follow-problems file and the bridge-timing CSV from the same session. Together they show
whether a dropout is one controller (hardware, battery, line of sight) or everything at
once, how long each gap lasted, and what the player was doing at the time.

## Source

Johnny5VR/VrBodyMotion.cs (problem state, panel pop-up and red panel), BridgeTiming.cs
(problem log, device transitions), ViewerWindow.BodyMotion.cs and ViewerWindow.VrTasks.cs
(insertion and transport stops marked as yours), BodyMotionChecks.cs (10 new checks).
Backups: Archive/DesktopChanges/vr-problem-display-20261108.

## Validation

3,962 player checks passed. Real player/editor smoke test passed. The panel picture was
checked in Diagnostics/follow-problem-panel.png. Not yet seen in the headset.
