# New feature validation — 2026-09-26

Release and Debug solution builds succeeded. Two existing CS0649 warnings remain for RobotHeadView vent-center fields.

32 automated suites passed, totaling 14,755 assertions across their final successful runs. This includes the general suite (6,594), new body/audio-trim/conversion checks (139), new feature UI checks (15), URDF presentation (247), URDF model (5), meeting (18), recording audio (7), piston signals (118), face tracking (17), playback controls (9), mapping files (6), persistent groups (21), controller recording (49), splitter rounding (3), Break Spline (38), FPS counter (37), playback performance (8), controller feedback (21), controller layout (22), layout recovery (195), controller speeds (37), render loop (2,205), collision performance (606), controller safeguard (20), speed calibration (289), library images (11), preview cadence (3,659), exterior fallback (7), controllers (241), editor API (90), audio movie (13), and sequence triggers (8).

## Coverage

- All 29 independent body joints match the full-body URDF ranges. Native normalization, zero, reversal, joint limits, and both neck mimic ratios are checked. Body entries appear in command and controller picklists and the Body configuration panel.
- Audio trims preserve hidden commands through save/reopen and independent Undo snapshots. Expanding bounds and disabling scrubs restore commands. Visible timing edits and unrelated takes are preserved. Copied takes receive new identities. Audio reads stop at the requested sample count.
- UI checks exercise Skip/End Commit locking, unlocking, reapplying, restoring commands, persisted locks, cursor-relative audio text/color, and recording conversion preferences. Audio timing UI was rendered and visually inspected.
- Voice conversion was exercised with temporary mock batch scripts and generated WAV data: paths with spaces, no-pause environment, successful output, existing-output protection, failed exit, missing output, corrupt output, and preservation of the original recording. Real Applio inference and voice quality were not tested.

## Corrections made during validation

- Fixed the audio-relative time XAML: both time labels now share a StackPanel inside their Border.
- Converter asynchronous operations no longer capture the UI synchronization context.
- Updated older regression fixtures for the current toolbar structure, theme resources, combined timeline default, additional help tutorials, FPS implementation, and clean undocked meeting behavior.
- Disabled WPF automatic application startup in all UI fixtures. An older rendering fixture unexpectedly launched the editor before this correction; that owned test process was stopped. Subsequent fixtures remained unshown and did not initialize hardware through MainWindow.Loaded.
- Connection-routing tests now isolate collision and calibrated-motion behavior, which have separate suites.

## Explicit limits

- External StreamDeckXL Node plugin integration was skipped because its sources are absent from this FaceTracking solution. The editor named-pipe API checks passed.
- Optional exterior mesh optimization masks are absent from this model folder. The check verifies identical full geometry and collision proxies when optimization falls back; optimized-mask rendering was not tested.
- No real controller, webcam, microphone recording, or physical robot end-to-end session was performed.
- No deployment package or repository commit was requested or created.
