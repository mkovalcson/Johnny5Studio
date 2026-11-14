# VR tracking and timing diagnostics

If a calibration step fails, note its name and the amber message. The live gauges show how far you moved, how far apart the two endpoint directions are, and whether the roll axis resembles the bend axis. Reposition and hold still after arming; the capture retries without another pull.

The VR Player's **Diagnostics** folder contains timestamped tracking captures (`tracking-*.bin` for Follow, `tracking-calibration-*.bin` for calibration), bridge timing CSVs and Follow problem reports. Calibration attempts log accepted steps and changed rejection messages. To investigate a tracking issue, keep the matching capture, calibration profile and timing CSV. A capture contains headset and controller poses and available finger data.

For offline diagnosis, run `Johnny5VR.exe --replay-tracking recording.bin --calibration profile.json --out replay.csv`. This runs the tracked samples through the solver and writes motion targets; it does not replay into the Editor or drive hardware. For a recording slowdown, distinguish headset rendering rate from the editor scene update rate.

In Follow, the bridge adapts its scene rate from about 30 to 75 Hz based on available time. The Player blends scene changes and animates supported tread belts locally between editor updates. The headset refresh rate is separate. If Follow holds or drops out, use the latest problem message and timestamp to find the corresponding bridge log interval. Stop, regain tracking and re-center before trying again.
